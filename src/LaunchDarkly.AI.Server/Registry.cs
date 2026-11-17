using System;
using System.Collections.Generic;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// A set of handlers and tools an application passes to <see cref="ConfigArgs.Registry"/>.
    /// There is no process-wide registry: the application owns each instance.
    /// </summary>
    public sealed class Registry
    {
        /// <summary>
        /// Creates an empty registry.
        /// </summary>
        public Registry()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a registry holding the given handlers and tools.
        /// </summary>
        /// <param name="handlers">Handlers, registered in order.</param>
        /// <param name="tools">Tools, by tool key.</param>
        public Registry(IEnumerable<ProviderHandler>? handlers, IReadOnlyDictionary<string, ToolHandler>? tools)
        {
            throw new NotImplementedException();
        }

        /// <summary>The registered handlers, in order.</summary>
        public IReadOnlyList<ProviderHandler> Handlers => throw new NotImplementedException();

        /// <summary>The registered tools, by tool key.</summary>
        public IReadOnlyDictionary<string, ToolHandler> Tools => throw new NotImplementedException();

        /// <summary>
        /// Adds handlers and tools. A handler with the same identity as a registered one, or a
        /// tool with the same key, replaces it in place and logs a warning.
        /// </summary>
        /// <param name="handlers">Handlers to add.</param>
        /// <param name="tools">Tools to add, by tool key.</param>
        public void Register(IEnumerable<ProviderHandler>? handlers = null, IReadOnlyDictionary<string, ToolHandler>? tools = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns a new registry holding <paramref name="a"/>'s entries overlaid with
        /// <paramref name="b"/>'s. Neither input changes.
        /// </summary>
        /// <param name="a">The base registry.</param>
        /// <param name="b">The registry whose entries win.</param>
        /// <returns>The combined registry.</returns>
        public static Registry Compose(Registry a, Registry b)
        {
            throw new NotImplementedException();
        }
    }
}
