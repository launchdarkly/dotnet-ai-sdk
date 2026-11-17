namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// Process-wide defaults, set with <see cref="LdAi.Configure(SdkOptions)"/>.
    /// </summary>
    public sealed record SdkOptions
    {
        /// <summary>
        /// Whether handlers record prompt and completion content on spans when they have no
        /// setting of their own. Defaults to <see langword="true"/>.
        /// </summary>
        public bool CaptureContent { get; init; } = true;
    }
}
