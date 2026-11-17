using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.ExtensionsAI.Internal;
using LaunchDarkly.Sdk;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI
{
    /// <summary>
    /// Options for <see cref="ChatClientMessages.InvokeAsync(string, string?, Context, ChatClientMessagesOptions?, CancellationToken)"/>.
    /// </summary>
    public sealed class ChatClientMessagesOptions
    {
        /// <summary>The chat client the handler uses. Set this or <see cref="ChatClientFactory"/>, not both.</summary>
        public IChatClient? ChatClient { get; init; }

        /// <summary>Returns the chat client for an evaluated config, once per request. Set this or <see cref="ChatClient"/>, not both.</summary>
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
        /// <param name="options">The chat client (or client factory), and the other config and handler options. Required in this version: without a client or factory the call throws.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The response, usage and tracking data.</returns>
        /// <exception cref="ArgumentException"><paramref name="options"/> gives no chat client and no client factory, or both;
        /// or <paramref name="configKey"/> is empty; or a provider name in the options is invalid.</exception>
        /// <exception cref="AiConfigNotFoundException">The config has no variation for the context.</exception>
        /// <exception cref="AiConfigDisabledException">The variation is not enabled.</exception>
        /// <exception cref="NoHandlerException">The handler does not serve the variation's provider.</exception>
        public static Task<ProviderResponse> InvokeAsync(
            string configKey,
            string? userInput,
            Context context,
            ChatClientMessagesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null || (options.ChatClient == null && options.ChatClientFactory == null))
            {
                throw new ArgumentException("A chat client or a chat client factory is required.", nameof(options));
            }

            if (options.ChatClient != null && options.ChatClientFactory != null)
            {
                throw new ArgumentException("Set a chat client or a chat client factory, not both.", nameof(options));
            }

            LdAi.ReportUsage(PackageRegistration.ChatClientMessagesUsage, PackageInfo.Name, PackageInfo.Version);
            var client = options.ChatClient;
            var factory = options.ChatClientFactory ?? ((_, _) => new ValueTask<IChatClient>(client!));
            var handler = ChatClientHandler.CreateCore(
                factory,
                new ChatClientHandlerOptions { CaptureContent = options.CaptureContent, Providers = options.Providers });
            var config = LaunchDarkly.AI.Server.Config.Bind(new ConfigArgs
            {
                Key = configKey,
                Handler = handler,
                ToolHandlers = options.ToolHandlers,
                Registry = options.Registry,
            });
            return config.InvokeWithoutUsageAsync(userInput, context, options.Variables, options.History, cancellationToken);
        }
    }
}
