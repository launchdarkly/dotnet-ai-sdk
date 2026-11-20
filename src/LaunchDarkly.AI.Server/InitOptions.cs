using System;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// Options for <see cref="LdAi.InitClientAsync(InitOptions)"/>.
    /// </summary>
    public sealed class InitOptions
    {
        /// <summary>
        /// The server-side SDK key. When unset, the <c>LD_SDK_KEY</c> environment variable is used.
        /// </summary>
        public string? SdkKey { get; init; }

        /// <summary>
        /// The base URI for flag polling and other requests, when not the LaunchDarkly default.
        /// </summary>
        public Uri? BaseUri { get; init; }

        /// <summary>
        /// The streaming URI, when not the LaunchDarkly default.
        /// </summary>
        public Uri? StreamUri { get; init; }

        /// <summary>
        /// The events URI, when not the LaunchDarkly default.
        /// </summary>
        public Uri? EventsUri { get; init; }

        /// <summary>
        /// How long to wait for the base SDK client to initialize before failing. When unset, or
        /// zero or negative, 10 seconds.
        /// </summary>
        public TimeSpan? StartWaitTime { get; init; }
    }
}
