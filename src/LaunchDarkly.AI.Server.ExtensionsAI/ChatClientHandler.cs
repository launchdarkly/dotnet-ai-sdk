using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI
{
    /// <summary>
    /// Options for <see cref="ChatClientHandler"/>.
    /// </summary>
    public sealed class ChatClientHandlerOptions
    {
        /// <summary>
        /// An explicit content-capture setting, which wins over the configured default.
        /// <see langword="null"/> leaves it unset.
        /// </summary>
        public bool? CaptureContent { get; init; }

        /// <summary>
        /// The provider names this wildcard handler serves. <see langword="null"/> serves any.
        /// </summary>
        public IReadOnlyList<string>? Providers { get; init; }

        /// <summary>
        /// The retry count for a chat client the handler builds itself. Refused with an injected
        /// client or client factory, whose own pipeline owns retries.
        /// </summary>
        public int? MaxRetries { get; init; }
    }

    /// <summary>
    /// A messages handler that runs AI Configs on a <see cref="IChatClient"/>. It serves any
    /// provider (<c>["*", "messages"]</c>), optionally scoped with
    /// <see cref="ChatClientHandlerOptions.Providers"/>.
    /// </summary>
    public static class ChatClientHandler
    {
        /// <summary>
        /// Creates a handler that sends every request to <paramref name="client"/>.
        /// </summary>
        /// <param name="client">The chat client.</param>
        /// <returns>The handler.</returns>
        public static ProviderHandler Create(IChatClient client)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a handler that sends every request to <paramref name="client"/>.
        /// </summary>
        /// <param name="client">The chat client.</param>
        /// <param name="options">Handler options.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        public static ProviderHandler Create(IChatClient client, ChatClientHandlerOptions options)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a handler that asks <paramref name="clientFactory"/> for a chat client once per
        /// request, with the evaluated config.
        /// </summary>
        /// <param name="clientFactory">Returns the chat client for an evaluated config.</param>
        /// <returns>The handler.</returns>
        public static ProviderHandler Create(Func<AiConfig, CancellationToken, ValueTask<IChatClient>> clientFactory)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a handler that asks <paramref name="clientFactory"/> for a chat client once per
        /// request, with the evaluated config.
        /// </summary>
        /// <param name="clientFactory">Returns the chat client for an evaluated config.</param>
        /// <param name="options">Handler options.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        public static ProviderHandler Create(
            Func<AiConfig, CancellationToken, ValueTask<IChatClient>> clientFactory,
            ChatClientHandlerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
