using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI
{
    /// <summary>
    /// Options for <see cref="ChatClientMessages.InvokeAsync(string, string?, Context, ChatClientMessagesOptions, CancellationToken)"/>.
    /// </summary>
    public sealed class ChatClientMessagesOptions
    {
        /// <summary>The chat client the handler uses.</summary>
        public IChatClient? ChatClient { get; init; }

        /// <summary>Returns the chat client for an evaluated config, once per request.</summary>
        public Func<AiConfig, CancellationToken, ValueTask<IChatClient>>? ChatClientFactory { get; init; }

        /// <summary>Template variables.</summary>
        public IReadOnlyDictionary<string, object?>? Variables { get; init; }

        /// <summary>Prior conversation turns.</summary>
        public IReadOnlyList<HistoryMessage>? History { get; init; }

        /// <summary>Tools, by tool key.</summary>
        public IReadOnlyDictionary<string, ToolHandler>? ToolHandlers { get; init; }

        /// <summary>A registry of further handlers and tools.</summary>
        public Registry? Registry { get; init; }

        /// <summary>An explicit content-capture setting for the handler.</summary>
        public bool? CaptureContent { get; init; }

        /// <summary>The provider names the handler serves.</summary>
        public IReadOnlyList<string>? Providers { get; init; }
    }

    /// <summary>
    /// Runs an AI Config on a <see cref="IChatClient"/> in one call.
    /// </summary>
    public static class ChatClientMessages
    {
        /// <summary>
        /// Builds a <see cref="ChatClientHandler"/> and invokes the config with it.
        /// </summary>
        /// <param name="configKey">The AI Config key.</param>
        /// <param name="userInput">The user's input.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="options">The chat client (or client factory), and the other config and handler options.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The response, usage and tracking data.</returns>
        public static Task<ProviderResponse> InvokeAsync(
            string configKey,
            string? userInput,
            Context context,
            ChatClientMessagesOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
