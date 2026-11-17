using System;
using System.Threading;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The process-wide defaults set by <see cref="LdAi.Configure(SdkOptions)"/> and the lock that
    /// fixes them once an AI invocation has run.
    /// </summary>
    internal static class SdkState
    {
        private static readonly object _lock = new object();
        private static SdkOptions _options = new SdkOptions();
        private static int _invoked;

        /// <summary>
        /// The content-capture default handlers use when they have no setting of their own:
        /// the latest accepted <see cref="LdAi.Configure(SdkOptions)"/> value, or <see langword="true"/>.
        /// </summary>
        internal static bool CaptureContentDefault => Volatile.Read(ref _options).CaptureContent;

        /// <summary>
        /// Whether an AI invocation has run since the process started or the last shutdown.
        /// </summary>
        internal static bool Invoked => Volatile.Read(ref _invoked) != 0;

        /// <summary>
        /// Records that an AI invocation has run. From then on, <see cref="Configure"/> accepts
        /// only the options already in effect.
        /// </summary>
        internal static void MarkInvoked() => Interlocked.Exchange(ref _invoked, 1);

        /// <summary>
        /// Replaces the defaults, unless an invocation has run and the options differ.
        /// </summary>
        /// <param name="options">The new defaults.</param>
        /// <exception cref="ConfigureLockedException">An invocation has run and the options differ.</exception>
        internal static void Configure(SdkOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            lock (_lock)
            {
                if (Invoked && !options.Equals(_options))
                {
                    throw new ConfigureLockedException(
                        "LdAi.Configure cannot change the SDK options after the first AI invocation. " +
                        "Call it before invoking a config or handler, or call LdAi.ShutdownAsync first.");
                }

                Volatile.Write(ref _options, options);
            }
        }

        /// <summary>
        /// Restores the built-in defaults and clears the lock.
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                Volatile.Write(ref _options, new SdkOptions());
                Interlocked.Exchange(ref _invoked, 0);
            }
        }
    }
}
