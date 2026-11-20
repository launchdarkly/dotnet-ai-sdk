using System;
using System.Collections.Generic;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Warns that a config's judges do not run yet: once per config key per client, not once per
    /// invocation. Shutting down re-arms it.
    /// </summary>
    internal static class JudgeWarning
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Logs the warning for <paramref name="configKey"/> unless it has been logged for that key
        /// since the client was initialized. Never throws.
        /// </summary>
        /// <param name="configKey">The AI Config key whose variation has a judge configuration.</param>
        internal static void WarnOnce(string configKey)
        {
            try
            {
                lock (_lock)
                {
                    if (!_warned.Add(configKey))
                    {
                        return;
                    }
                }

                SdkLog.Logger.Warn(
                    "The AI Config \"{0}\" has a judge configuration, but this SDK does not run judges yet. " +
                    "The config runs without them.",
                    configKey);
            }
            catch (Exception)
            {
                // A warning never fails the invocation that asked for it.
            }
        }

        /// <summary>
        /// Forgets which keys have been warned about, so the next client warns again.
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                _warned.Clear();
            }
        }
    }
}
