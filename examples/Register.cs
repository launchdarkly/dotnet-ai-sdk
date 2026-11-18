using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.AI.Server.ExtensionsAI;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// Builds the registry every scenario uses. The SDK has no process-wide registry: the
    /// application owns this one and passes it to each config.
    /// </summary>
    internal static class Register
    {
        /// <summary>
        /// Returns a registry holding every handler this repository provides and every example tool,
        /// keyed exactly as the flag variations name them (<see cref="Tools.All"/>).
        /// </summary>
        /// <remarks>
        /// The one handler is <see cref="ChatClientHandler"/>: it serves messages-mode configs for
        /// any provider (<c>["*", "messages"]</c>), on the chat client <see cref="ChatClientFor"/>
        /// returns for the evaluated config.
        /// </remarks>
        internal static Registry BuildRegistry() =>
            new Registry(
                new[] { ChatClientHandler.Create((config, _) => new ValueTask<IChatClient>(ChatClientFor(config))) },
                Tools.All);

        /// <summary>
        /// The chat client for an evaluated config. The examples ship no model provider package, so
        /// this is always the scripted client, which needs no network or API key. To call a real
        /// model, return that provider's <see cref="IChatClient"/> here, chosen by
        /// <c>config.Provider.Name</c>.
        /// </summary>
        private static IChatClient ChatClientFor(AiConfig config) => new ScriptedChatClient();
    }
}
