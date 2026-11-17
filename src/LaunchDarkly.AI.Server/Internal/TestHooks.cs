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
        /// effects: no package-information or usage events are sent, and nothing held for
        /// reporting is flushed. Helpers called after it see a client, as after an init.
        /// </summary>
        /// <param name="client">The client to install.</param>
        internal static void SetClient(ILdClient client)
        {
            Lifecycle.InstallForTests(client ?? throw new ArgumentNullException(nameof(client)));
        }

        /// <summary>
        /// Returns the process-wide state to what a fresh process has: no client and no attempt in
        /// flight, nothing reported or held for reporting, the built-in defaults and no lock.
        /// Registered packages are kept, as <see cref="LdAi.ShutdownAsync(System.Threading.CancellationToken)"/> keeps them.
        /// <see cref="ClientFactory"/> and <see cref="LogAdapter"/> are left as the test set them.
        /// </summary>
        /// <remarks>
        /// Each piece of process-wide state (the singleton, the attempt in flight, the reported
        /// sets, the configured defaults, the lock and the judge warnings) is cleared here; anything new must be too.
        /// </remarks>
        internal static void Reset()
        {
            Lifecycle.ResetForTests();
            SdkState.Reset();
            SdkInfoReporter.ResetReported();
            UsageReporter.Reset();
            JudgeWarning.Reset();
            OnceKeys.Reset();
        }
    }
}
