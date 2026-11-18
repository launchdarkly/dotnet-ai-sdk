using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.ExtensionsAI.Internal;
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
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
        public static ProviderHandler Create(IChatClient client) => Create(client, new ChatClientHandlerOptions());

        /// <summary>
        /// Creates a handler that sends every request to <paramref name="client"/>.
        /// </summary>
        /// <param name="client">The chat client.</param>
        /// <param name="options">Handler options.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentException">An option is invalid.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
        public static ProviderHandler Create(IChatClient client, ChatClientHandlerOptions options)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ReportCreate();
            return CreateCore((_, _) => new ValueTask<IChatClient>(client), options);
        }

        /// <summary>
        /// Creates a handler that asks <paramref name="clientFactory"/> for a chat client once per
        /// request, with the evaluated config.
        /// </summary>
        /// <param name="clientFactory">Returns the chat client for an evaluated config.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clientFactory"/> is <see langword="null"/>.</exception>
        public static ProviderHandler Create(Func<AiConfig, CancellationToken, ValueTask<IChatClient>> clientFactory) =>
            Create(clientFactory, new ChatClientHandlerOptions());

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
            if (clientFactory == null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ReportCreate();
            return CreateCore(clientFactory, options);
        }

        /// <summary>
        /// Builds the handler without reporting helper usage, for the convenience wrapper, which reports
        /// its own helper.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The provider list is empty, or a name in it is blank or padded.</exception>
        internal static ProviderHandler CreateCore(
            Func<AiConfig, CancellationToken, ValueTask<IChatClient>> clientFactory,
            ChatClientHandlerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            PackageRegistration.Register();
            var runner = new ChatClientRunner(clientFactory, FunctionInvokingToolLoop.Instance);
            ProviderHandler? handler = null;
            handler = ProviderHandler.Create(
                new ProvidesFor(ProvidesFor.Wildcard, HandlerModes.Messages),
                (request, cancellationToken) => runner.RunAsync(request, handler!.EffectiveCaptureContent, cancellationToken),
                new CreateHandlerOptions { CaptureContent = options.CaptureContent, Providers = options.Providers },
                inherit: null);
            return handler;
        }

        private static void ReportCreate() =>
            LdAi.ReportUsage(PackageRegistration.CreateHandlerUsage, PackageInfo.Name, PackageInfo.Version);
    }
}
