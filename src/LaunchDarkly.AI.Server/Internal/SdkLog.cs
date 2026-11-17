using System;
using LaunchDarkly.Logging;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Where the SDK writes its own log messages: the LaunchDarkly client's logger when a client
    /// exists, otherwise the LaunchDarkly default (the console, at Info and above).
    /// </summary>
    internal static class SdkLog
    {
        private const string Name = "LaunchDarkly.AI";

        private static readonly Logger _fallback = Logs.Default.Logger(Name);

        /// <summary>The logger to write to now.</summary>
        internal static Logger Logger
        {
            get
            {
                var adapter = TestHooks.LogAdapter;
                if (adapter != null)
                {
                    return adapter.Logger(Name);
                }

                var client = Lifecycle.CurrentLdClient;
                if (client != null)
                {
                    try
                    {
                        return client.GetLogger().SubLogger("AI");
                    }
                    catch (Exception)
                    {
                        // A client that cannot supply a logger falls back to the default one.
                    }
                }

                return _fallback;
            }
        }
    }
}
