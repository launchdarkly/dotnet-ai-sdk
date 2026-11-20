namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// Identifies this SDK in the telemetry it reports.
    /// </summary>
    internal static class SdkInfo
    {
        /// <summary>
        /// The NuGet package ID.
        /// </summary>
        internal const string Name = "LaunchDarkly.AI.Server";

        /// <summary>
        /// The package version. Updated by hand for each working version, and by the release tooling at release time.
        /// </summary>
        internal const string Version = "0.1.0"; // x-release-please-version

        /// <summary>
        /// The implementation language reported by every LaunchDarkly AI SDK for .NET.
        /// </summary>
        internal const string Language = "c#";
    }
}
