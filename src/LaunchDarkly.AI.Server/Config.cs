using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.Internal;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// Arguments for <see cref="LdAi.Config(ConfigArgs)"/>.
    /// </summary>
    public sealed class ConfigArgs
    {
        /// <summary>The AI Config key. Required.</summary>
        public string Key { get; init; } = "";

        /// <summary>A single handler.</summary>
        public ProviderHandler? Handler { get; init; }

        /// <summary>Several handlers; the one matching the variation runs.</summary>
        public IReadOnlyList<ProviderHandler>? Handlers { get; init; }

        /// <summary>Tools, by tool key. Only those the variation attaches reach the handler.</summary>
        public IReadOnlyDictionary<string, ToolHandler>? ToolHandlers { get; init; }

        /// <summary>A registry of further handlers and tools. Local handlers and tools win.</summary>
        public Registry? Registry { get; init; }
    }

    /// <summary>
    /// An AI Config bound to its handlers, returned by <see cref="LdAi.Config(ConfigArgs)"/>.
    /// </summary>
    /// <remarks>
    /// A bound config is safe to invoke from several threads. Its local handlers and tools are
    /// copied when it is bound; a registry is read on each invocation.
    /// </remarks>
    public sealed class Config
    {
        private const string ToolCallEvent = "$ld:ai:tool_call";

        private readonly string _key;
        private readonly IReadOnlyList<ProviderHandler> _localHandlers;
        private readonly IReadOnlyDictionary<string, ToolHandler>? _localTools;
        private readonly Registry? _registry;

        private Config(string key, IReadOnlyList<ProviderHandler> localHandlers, IReadOnlyDictionary<string, ToolHandler>? localTools, Registry? registry)
        {
            _key = key;
            _localHandlers = localHandlers;
            _localTools = localTools;
            _registry = registry;
        }

        /// <summary>
        /// Evaluates the config for <paramref name="context"/>, runs the matching handler and tracks
        /// the result.
        /// </summary>
        /// <param name="input">The user's input.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="variables">Template variables.</param>
        /// <param name="history">Prior conversation turns.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The response, usage and tracking data.</returns>
        /// <exception cref="AiConfigNotFoundException">The config has no variation for the context.</exception>
        /// <exception cref="AiConfigDisabledException">The variation is not enabled.</exception>
        /// <exception cref="NoHandlerException">No handler matches the variation.</exception>
        public async Task<ProviderResponse> InvokeAsync(
            string? input,
            Context context,
            IReadOnlyDictionary<string, object?>? variables = null,
            IReadOnlyList<HistoryMessage>? history = null,
            CancellationToken cancellationToken = default)
        {
            UsageReporter.Report("client.config.invoke");
            cancellationToken.ThrowIfCancellationRequested();

            var client = await Lifecycle.GetLdClientAsync(cancellationToken).ConfigureAwait(false);
            var value = client.JsonVariation(_key, context, LdValue.Null);
            var (config, meta) = ConfigParser.Parse(_key, value);
            if (config.JudgeConfiguration != null)
            {
                JudgeWarning.WarnOnce(_key);
            }

            var handler = HandlerSelection.Select(ResolveHandlers(), config.Provider.Name, Modes.Normalize(meta.Mode));
            var trackData = InvokeTracking.CreateTrackData(config, meta);

            var scoped = ToolRouting.Scope(config.Tools, ResolveTools());
            var tools = ToolRouting.Wrap(scoped, toolKey => TrackToolCall(client, context, trackData, toolKey));

            var handlerVariables = Variables.WithContext(
                variables,
                context,
                trackData,
                handler.EffectiveCaptureContent ? ToJsonElement(value) : null);

            var request = new HandlerRequest(config)
            {
                UserInput = input,
                ToolHandlers = tools,
                Variables = handlerVariables,
                History = history,
            };

            var stopwatch = Stopwatch.StartNew();
            HandlerResult result;
            try
            {
                result = await handler.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                InvokeTracking.TrackError(client, context, trackData, stopwatch.Elapsed);
                throw;
            }

            var duration = stopwatch.Elapsed;
            var usage = UsageParser.Parse(result?.Usage);
            InvokeTracking.TrackSuccess(client, context, trackData, duration, usage);

            return new ProviderResponse
            {
                Response = StructuredOutput.Parse(result?.Output, config),
                Usage = usage,
                TrackData = trackData,
            };
        }

        /// <summary>
        /// Binds <paramref name="args"/>. Called by <see cref="LdAi.Config(ConfigArgs)"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The key is empty, or a handler is <see langword="null"/>.</exception>
        internal static Config Bind(ConfigArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (string.IsNullOrEmpty(args.Key))
            {
                throw new ArgumentException("An AI Config key is required.", nameof(args));
            }

            var handlers = new List<ProviderHandler>();
            if (args.Handler != null)
            {
                handlers.Add(args.Handler);
            }

            if (args.Handlers != null)
            {
                if (args.Handlers.Any(h => h == null))
                {
                    throw new ArgumentException("A handler is null.", nameof(args));
                }

                handlers.AddRange(args.Handlers);
            }

            var tools = args.ToolHandlers == null
                ? null
                : new Dictionary<string, ToolHandler>(args.ToolHandlers.ToDictionary(t => t.Key, t => t.Value), StringComparer.Ordinal);
            return new Config(args.Key, handlers.AsReadOnly(), tools, args.Registry);
        }

        /// <summary>Local handlers first, then the registry's, so a local handler wins a tie.</summary>
        private IReadOnlyList<ProviderHandler> ResolveHandlers()
        {
            if (_registry == null)
            {
                return _localHandlers;
            }

            return _localHandlers.Concat(_registry.Handlers).ToList();
        }

        /// <summary>The registry's tools overlaid with the local ones, so a local tool wins.</summary>
        private IReadOnlyDictionary<string, ToolHandler>? ResolveTools()
        {
            if (_registry == null)
            {
                return _localTools;
            }

            var merged = new Dictionary<string, ToolHandler>(StringComparer.Ordinal);
            foreach (var tool in _registry.Tools)
            {
                merged[tool.Key] = tool.Value;
            }

            if (_localTools != null)
            {
                foreach (var tool in _localTools)
                {
                    merged[tool.Key] = tool.Value;
                }
            }

            return merged;
        }

        /// <summary>Tracks one tool call. A tracking failure is logged and never reaches the tool's caller.</summary>
        private static void TrackToolCall(ILdClient client, Context context, TrackData trackData, string toolKey)
        {
            try
            {
                InvokeTracking.Track(client, ToolCallEvent, context, InvokeTracking.ToEventData(trackData with { ToolKey = toolKey }), 1);
            }
            catch (Exception e)
            {
                SdkLog.Logger.Warn("Could not track a tool call: {0}", e.Message);
            }
        }

        private static JsonElement ToJsonElement(LdValue value)
        {
            using var document = JsonDocument.Parse(value.ToJsonString());
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// Chooses the handler for a variation's provider and mode.
    /// </summary>
    internal static class HandlerSelection
    {
        /// <summary>
        /// Returns the handler for <paramref name="provider"/> in <paramref name="mode"/> (already
        /// normalized). A handler that names the provider wins; then the wildcard with the shortest
        /// provider list that contains it, the earlier one on a tie; then the first unscoped
        /// wildcard. The mode must match exactly.
        /// </summary>
        /// <exception cref="NoHandlerException">The provider is empty, or no handler matches.</exception>
        internal static ProviderHandler Select(IReadOnlyList<ProviderHandler> handlers, string provider, string mode)
        {
            if (string.IsNullOrEmpty(provider))
            {
                throw new NoHandlerException("Provider not found");
            }

            ProviderHandler? bestScoped = null;
            ProviderHandler? unscoped = null;
            foreach (var handler in handlers)
            {
                if (handler.ProvidesFor.Mode != mode)
                {
                    continue;
                }

                if (handler.ProvidesFor.Provider == provider)
                {
                    return handler;
                }

                if (!handler.WildcardCovers(provider))
                {
                    continue;
                }

                if (handler.Providers == null)
                {
                    unscoped ??= handler;
                }
                else if (bestScoped == null || handler.Providers.Count < bestScoped.Providers!.Count)
                {
                    bestScoped = handler;
                }
            }

            var chosen = bestScoped ?? unscoped;
            if (chosen != null)
            {
                return chosen;
            }

            if (!handlers.Any(h => h.CoversProvider(provider)))
            {
                throw new NoHandlerException($"Handler for provider {provider} not found");
            }

            throw new NoHandlerException($"Handler for provider {provider} with mode {mode} not found");
        }
    }
}
