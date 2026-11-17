using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// The provider and mode a handler serves.
    /// </summary>
    public sealed record ProvidesFor
    {
        /// <summary>
        /// The provider name that matches any provider.
        /// </summary>
        public const string Wildcard = "*";

        /// <summary>
        /// Creates a provider and mode pair.
        /// </summary>
        /// <param name="provider">A provider name, compared exactly, or <see cref="Wildcard"/>.</param>
        /// <param name="mode">One of <see cref="HandlerModes"/>.</param>
        public ProvidesFor(string provider, string mode)
        {
            Provider = provider;
            Mode = mode;
        }

        /// <summary>A provider name, or <see cref="Wildcard"/>.</summary>
        public string Provider { get; init; }

        /// <summary>One of <see cref="HandlerModes"/>.</summary>
        public string Mode { get; init; }
    }

    /// <summary>
    /// What a handler is called with.
    /// </summary>
    public sealed record HandlerRequest
    {
        /// <summary>
        /// Creates a request.
        /// </summary>
        /// <param name="config">The evaluated config.</param>
        public HandlerRequest(AiConfig config)
        {
            Config = config;
        }

        /// <summary>The evaluated config.</summary>
        public AiConfig Config { get; init; }

        /// <summary>The user's input, or <see langword="null"/>.</summary>
        public string? UserInput { get; init; }

        /// <summary>The tools the config attaches, by tool key.</summary>
        public IReadOnlyDictionary<string, ToolHandler> ToolHandlers { get; init; } = new Dictionary<string, ToolHandler>();

        /// <summary>Template variables, including <c>ldContext</c> and the SDK's tracking entries.</summary>
        public IReadOnlyDictionary<string, object?> Variables { get; init; } = new Dictionary<string, object?>();

        /// <summary>Prior conversation turns, or <see langword="null"/>.</summary>
        public IReadOnlyList<HistoryMessage>? History { get; init; }
    }

    /// <summary>
    /// What a handler returns.
    /// </summary>
    public sealed record HandlerResult
    {
        /// <summary>The output: a string, or a parsed object from a provider with structured output.</summary>
        public object? Output { get; init; }

        /// <summary>The provider's raw token counts, in any of the spellings the SDK normalizes.</summary>
        public IReadOnlyDictionary<string, object?>? Usage { get; init; }
    }

    /// <summary>
    /// Options for <see cref="LdAi.CreateHandler(ProvidesFor, Func{HandlerRequest, CancellationToken, Task{HandlerResult}}, CreateHandlerOptions)"/>.
    /// </summary>
    public sealed class CreateHandlerOptions
    {
        /// <summary>
        /// An explicit content-capture setting, which wins over the configured default.
        /// <see langword="null"/> leaves it unset.
        /// </summary>
        public bool? CaptureContent { get; init; }

        /// <summary>
        /// For a wildcard handler, the provider names it serves. <see langword="null"/> serves any.
        /// </summary>
        public IReadOnlyList<string>? Providers { get; init; }
    }

    /// <summary>
    /// A handler that runs a config against a provider. Create one with
    /// <see cref="LdAi.CreateHandler(ProvidesFor, Func{HandlerRequest, CancellationToken, Task{HandlerResult}}, CreateHandlerOptions)"/>
    /// or a provider package's factory.
    /// </summary>
    public sealed class ProviderHandler
    {
        private ProviderHandler()
        {
        }

        /// <summary>The provider and mode this handler serves.</summary>
        public ProvidesFor ProvidesFor => throw new NotImplementedException();

        /// <summary>
        /// For a wildcard handler, the provider names it serves, each once, in first-seen order.
        /// <see langword="null"/> when unscoped.
        /// </summary>
        public IReadOnlyList<string>? Providers => throw new NotImplementedException();

        /// <summary>
        /// The explicit content-capture override, or <see langword="null"/> when unset. Setting it
        /// does not change the process default.
        /// </summary>
        public bool? CaptureContent
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        /// <summary>
        /// The content-capture setting in effect now: the override, else the configured default,
        /// else <see langword="true"/>.
        /// </summary>
        public bool EffectiveCaptureContent => throw new NotImplementedException();

        /// <summary>
        /// Runs the handler. This counts as an AI invocation for the
        /// <see cref="LdAi.Configure(SdkOptions)"/> lock.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The handler's result.</returns>
        public Task<HandlerResult> InvokeAsync(HandlerRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
