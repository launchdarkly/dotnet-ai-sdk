using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk;

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
    public sealed class Config
    {
        private Config()
        {
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
        public Task<ProviderResponse> InvokeAsync(
            string? input,
            Context context,
            IReadOnlyDictionary<string, object?>? variables = null,
            IReadOnlyList<HistoryMessage>? history = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
