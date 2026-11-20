using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The SDK's own telemetry events, which are sent on an anonymous internal context rather than
    /// an application context.
    /// </summary>
    internal static class InternalEvents
    {
        /// <summary>The package-information event, sent once per package version per client.</summary>
        internal const string SdkInfo = "$ld:ai:sdk:info";

        /// <summary>The helper-usage event, sent once per helper per client.</summary>
        internal const string SdkUsage = "$ld:ai:sdk:usage";

        /// <summary>The context every internal event is sent on.</summary>
        internal static readonly Context Context =
            Context.Builder(ContextKind.Of("ld_ai"), "ld-internal-tracking").Anonymous(true).Build();
    }
}
