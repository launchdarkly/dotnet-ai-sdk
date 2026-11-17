using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Seams for this repository's unit tests, reachable only through InternalsVisibleTo.
    /// Production code never sets them.
    /// </summary>
    internal static class TestHooks
    {
        /// <summary>
        /// Replaces construction of the base SDK client. Called with the resolved SDK key and the
        /// caller's options, off the caller's thread. <see langword="null"/> builds a real client.
        /// </summary>
        internal static Func<string, InitOptions, ILdClient>? ClientFactory;

        /// <summary>
        /// Where the SDK writes its own log messages. <see langword="null"/> uses the default.
        /// </summary>
        internal static ILogAdapter? LogAdapter;

        /// <summary>
        /// Installs <paramref name="client"/> as the singleton without any of the init side
        /// effects: no package-information or usage events are sent.
        /// </summary>
        /// <param name="client">The client to install.</param>
        internal static void SetClient(ILdClient client)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns every piece of process-wide state to its initial value, including the
        /// registered packages, the reported sets, the configured defaults and the lock.
        /// </summary>
        internal static void Reset()
        {
            throw new NotImplementedException();
        }
    }
}
