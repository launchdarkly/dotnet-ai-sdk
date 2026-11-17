namespace LaunchDarkly.AI.Server.ExtensionsAI
{
    /// <summary>
    /// The identity this package registers with the core package and reports on usage events.
    /// </summary>
    internal static class PackageInfo
    {
        /// <summary>
        /// The NuGet package ID.
        /// </summary>
        internal const string Name = "LaunchDarkly.AI.Server.ExtensionsAI";

        /// <summary>
        /// The package version. Updated by hand for each working version, and by the release tooling at release time.
        /// </summary>
        internal const string Version = "0.0.0"; // x-release-please-version
    }
}
