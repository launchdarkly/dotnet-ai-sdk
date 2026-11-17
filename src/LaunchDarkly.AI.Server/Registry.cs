using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.AI.Server.Internal;
using LaunchDarkly.Logging;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// A set of handlers and tools an application passes to <see cref="ConfigArgs.Registry"/>.
    /// There is no process-wide registry: the application owns each instance.
    /// </summary>
    /// <remarks>
    /// A registry is safe to use from several threads. <see cref="Handlers"/> and <see cref="Tools"/>
    /// return snapshots, so registering more entries does not change a collection already read.
    /// </remarks>
    public sealed class Registry
    {
        private readonly object _lock = new object();
        private readonly List<ProviderHandler> _handlers = new List<ProviderHandler>();
        private readonly Dictionary<string, ToolHandler> _tools = new Dictionary<string, ToolHandler>(StringComparer.Ordinal);

        /// <summary>
        /// Creates an empty registry.
        /// </summary>
        public Registry()
        {
        }

        /// <summary>
        /// Creates a registry holding the given handlers and tools.
        /// </summary>
        /// <param name="handlers">Handlers, registered in order.</param>
        /// <param name="tools">Tools, by tool key.</param>
        public Registry(IEnumerable<ProviderHandler>? handlers, IReadOnlyDictionary<string, ToolHandler>? tools)
        {
            Register(handlers, tools);
        }

        /// <summary>The registered handlers, in order.</summary>
        public IReadOnlyList<ProviderHandler> Handlers
        {
            get
            {
                lock (_lock)
                {
                    return _handlers.ToArray();
                }
            }
        }

        /// <summary>The registered tools, by tool key.</summary>
        public IReadOnlyDictionary<string, ToolHandler> Tools
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, ToolHandler>(_tools, StringComparer.Ordinal);
                }
            }
        }

        /// <summary>
        /// Adds handlers and tools. A handler with the same identity as a registered one, or a
        /// tool with the same key, replaces it in place and logs a warning.
        /// </summary>
        /// <remarks>
        /// A handler's identity is its provider and mode, plus, for a wildcard handler, the set of
        /// providers it lists. The order of that list and repeated names do not matter, and an
        /// unscoped wildcard has an identity of its own. A replacement keeps the position of the
        /// handler it replaces, because position breaks ties when several wildcards match.
        /// </remarks>
        /// <param name="handlers">Handlers to add.</param>
        /// <param name="tools">Tools to add, by tool key.</param>
        /// <exception cref="ArgumentException">A handler or tool is <see langword="null"/>.</exception>
        public void Register(IEnumerable<ProviderHandler>? handlers = null, IReadOnlyDictionary<string, ToolHandler>? tools = null)
        {
            // Validate and materialize first, so a bad argument leaves the registry unchanged.
            var newHandlers = handlers?.ToList() ?? new List<ProviderHandler>();
            if (newHandlers.Any(h => h == null))
            {
                throw new ArgumentException("A handler is null.", nameof(handlers));
            }

            var newTools = tools?.ToList() ?? new List<KeyValuePair<string, ToolHandler>>();
            if (newTools.Any(t => t.Key == null || t.Value == null))
            {
                throw new ArgumentException("A tool key or tool is null.", nameof(tools));
            }

            var warnings = new List<string>();
            lock (_lock)
            {
                foreach (var handler in newHandlers)
                {
                    var identity = HandlerIdentity.Of(handler);
                    var index = _handlers.FindIndex(h => HandlerIdentity.Of(h).Equals(identity));
                    if (index >= 0)
                    {
                        warnings.Add($"Multiple handlers registered for {identity}. This may be unintended. The last one will be used.");
                        _handlers[index] = handler;
                    }
                    else
                    {
                        _handlers.Add(handler);
                    }
                }

                foreach (var tool in newTools)
                {
                    if (_tools.ContainsKey(tool.Key))
                    {
                        warnings.Add($"Multiple tools registered for \"{tool.Key}\". This may be unintended. The last one will be used.");
                    }

                    _tools[tool.Key] = tool.Value;
                }
            }

            if (warnings.Count > 0)
            {
                var log = Log;
                foreach (var warning in warnings)
                {
                    log.Warn(warning);
                }
            }
        }

        /// <summary>
        /// Returns a new registry holding <paramref name="a"/>'s entries overlaid with
        /// <paramref name="b"/>'s. Neither input changes.
        /// </summary>
        /// <remarks>
        /// A handler of <paramref name="b"/> with the same identity as one of <paramref name="a"/>
        /// takes that handler's position; <paramref name="b"/>'s other handlers follow all of
        /// <paramref name="a"/>'s. A conflict is not logged.
        /// </remarks>
        /// <param name="a">The base registry.</param>
        /// <param name="b">The registry whose entries win.</param>
        /// <returns>The combined registry.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="a"/> or <paramref name="b"/> is <see langword="null"/>.</exception>
        public static Registry Compose(Registry a, Registry b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            var result = new Registry();
            foreach (var source in new[] { a, b })
            {
                foreach (var handler in source.Handlers)
                {
                    var identity = HandlerIdentity.Of(handler);
                    var index = result._handlers.FindIndex(h => HandlerIdentity.Of(h).Equals(identity));
                    if (index >= 0)
                    {
                        result._handlers[index] = handler;
                    }
                    else
                    {
                        result._handlers.Add(handler);
                    }
                }

                foreach (var tool in source.Tools)
                {
                    result._tools[tool.Key] = tool.Value;
                }
            }

            return result;
        }

        private static Logger Log => Logger.WithAdapter(TestHooks.LogAdapter ?? Logs.Default, "LaunchDarkly.AI");

        /// <summary>
        /// A handler's registration identity: provider, normalized mode and, for a wildcard, the
        /// sorted set of listed providers (or none).
        /// </summary>
        private readonly struct HandlerIdentity : IEquatable<HandlerIdentity>
        {
            private readonly string _provider;
            private readonly string _mode;
            private readonly string[]? _providers;

            private HandlerIdentity(string provider, string mode, string[]? providers)
            {
                _provider = provider;
                _mode = mode;
                _providers = providers;
            }

            public static HandlerIdentity Of(ProviderHandler handler)
            {
                var providers = handler.IsWildcard && handler.Providers != null
                    ? handler.Providers.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray()
                    : null;
                return new HandlerIdentity(handler.ProvidesFor.Provider, Modes.Normalize(handler.ProvidesFor.Mode), providers);
            }

            public bool Equals(HandlerIdentity other) =>
                _provider == other._provider
                && _mode == other._mode
                && (_providers == null
                    ? other._providers == null
                    : other._providers != null && _providers.SequenceEqual(other._providers, StringComparer.Ordinal));

            public override bool Equals(object? obj) => obj is HandlerIdentity other && Equals(other);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_provider) ^ StringComparer.Ordinal.GetHashCode(_mode);

            public override string ToString() =>
                _providers == null
                    ? $"[{_provider}, {_mode}]"
                    : $"[{_provider}, {_mode}] with providers [{string.Join(", ", _providers)}]";
        }
    }
}
