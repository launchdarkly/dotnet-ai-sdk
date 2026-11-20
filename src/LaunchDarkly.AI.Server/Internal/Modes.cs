namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Maps a variation's mode to the handler mode that serves it.
    /// </summary>
    internal static class Modes
    {
        /// <summary>
        /// Returns <see cref="HandlerModes.Agent"/> for <c>agent</c>, and
        /// <see cref="HandlerModes.Messages"/> for every other mode, including <c>completion</c>,
        /// <c>judge</c>, an absent mode and one the SDK does not know.
        /// </summary>
        /// <param name="variationMode">The variation's <c>_ldMeta.mode</c>.</param>
        /// <returns>One of <see cref="HandlerModes"/>.</returns>
        internal static string Normalize(string? variationMode) =>
            variationMode == HandlerModes.Agent ? HandlerModes.Agent : HandlerModes.Messages;
    }
}
