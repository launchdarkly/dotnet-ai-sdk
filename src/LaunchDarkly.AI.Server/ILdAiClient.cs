using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// The process-wide AI client that <see cref="LdAi"/> manages, as an interface for injection and
    /// test doubles.
    /// </summary>
    public interface ILdAiClient : IDisposable
    {
        /// <summary>The LaunchDarkly server-side client this AI client uses.</summary>
        ILdClient LdClient { get; }

        /// <summary>
        /// Binds an AI Config to its handlers.
        /// </summary>
        /// <param name="args">The config key, handlers and tools.</param>
        /// <returns>The bound config.</returns>
        Config Config(ConfigArgs args);

        /// <summary>
        /// Reads an AI Config variation without invoking a model. A missing, invalid or unreadable
        /// config is reported in the result rather than thrown.
        /// </summary>
        /// <param name="key">The AI Config key.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The variation, or the reason it could not be read.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<InspectResult> InspectConfigAsync(string key, Context context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Flushes and closes the LaunchDarkly client and clears the singleton.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when the client is closed.</returns>
        Task ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
