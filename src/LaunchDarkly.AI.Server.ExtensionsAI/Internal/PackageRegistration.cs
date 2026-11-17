namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// The package's identity and helper-usage IDs.
    /// </summary>
    internal static class PackageRegistration
    {
        /// <summary>The helper-usage ID of <see cref="ChatClientHandler"/>'s factory.</summary>
        internal const string CreateHandlerUsage = "extensionsai-messages.createChatClientMessagesHandler";

        /// <summary>The helper-usage ID of <see cref="ChatClientMessages"/>.</summary>
        internal const string ChatClientMessagesUsage = "extensionsai-messages.chatClientMessages";

        /// <summary>
        /// Registers the package with the core SDK, which reports it once per client. Repeat calls are
        /// harmless.
        /// </summary>
        internal static void Register() => LdAi.RegisterAiSdkPackage(PackageInfo.Name, PackageInfo.Version);
    }
}
