using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The process-wide AI client. Its members act on the singleton it stands for; once the
    /// singleton is shut down or replaced, shutting this instance down does nothing.
    /// </summary>
    internal sealed class LdAiClient : ILdAiClient
    {
        internal LdAiClient(ILdClient ldClient)
        {
            LdClient = ldClient;
        }

        /// <inheritdoc/>
        public ILdClient LdClient { get; }

        /// <inheritdoc/>
        public Config Config(ConfigArgs args) => LdAi.Config(args);

        /// <inheritdoc/>
        public Task<InspectResult> InspectConfigAsync(string key, Context context, CancellationToken cancellationToken = default) =>
            LdAi.InspectConfigAsync(key, context, cancellationToken);

        /// <inheritdoc/>
        public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
            LdAi.ShutdownAsync(this, cancellationToken);

        /// <summary>
        /// Shuts the client down, as <see cref="ShutdownAsync"/> does, and waits for the close.
        /// </summary>
        public void Dispose()
        {
            var client = Lifecycle.Detach(this);
            if (client != null)
            {
                Lifecycle.Teardown(client);
            }
        }
    }
}
