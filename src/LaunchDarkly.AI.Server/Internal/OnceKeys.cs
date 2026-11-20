using System;
using System.Collections.Generic;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Keys that should act once per process, such as a warning logged once per tool key. Each
    /// caller uses its own scope. Cleared only by the test hooks.
    /// </summary>
    internal static class OnceKeys
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<(string Scope, string Key)> _claimed = new HashSet<(string, string)>();

        /// <summary>
        /// Claims <paramref name="key"/> in <paramref name="scope"/>.
        /// </summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="key">The key.</param>
        /// <returns><see langword="true"/> the first time, <see langword="false"/> after.</returns>
        internal static bool TryClaim(string scope, string key)
        {
            lock (_lock)
            {
                return _claimed.Add((scope, key));
            }
        }

        /// <summary>Forgets every claimed key.</summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                _claimed.Clear();
            }
        }
    }
}
