using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.Internal;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// The entry point of the AI SDK. Owns the process-wide AI client.
    /// </summary>
    public static class LdAi
    {
        /// <summary>
        /// Initializes the process-wide client with an SDK key, or returns it when it already
        /// exists. Concurrent first calls share one attempt; a failed attempt is not cached.
        /// </summary>
        /// <returns>The AI client.</returns>
        /// <exception cref="LdAiException">No <c>LD_SDK_KEY</c>, or the LaunchDarkly client did not initialize.</exception>
        public static Task<ILdAiClient> InitClientAsync()
        => InitClientAsync(new InitOptions(), CancellationToken.None);

        /// <summary>
        /// Initializes the process-wide client with an SDK key, or returns it when it already
        /// exists. Concurrent first calls share one attempt; a failed attempt is not cached.
        /// </summary>
        /// <param name="options">The SDK key and connection options. A later call ignores them.</param>
        /// <returns>The AI client.</returns>
        /// <exception cref="LdAiException">No SDK key, or the LaunchDarkly client did not initialize.</exception>
        public static Task<ILdAiClient> InitClientAsync(InitOptions options)
        => InitClientAsync(options, CancellationToken.None);

        /// <summary>
        /// Initializes the process-wide client with an SDK key, or returns it when it already
        /// exists. Concurrent first calls share one attempt; a failed attempt is not cached.
        /// </summary>
        /// <param name="options">The SDK key and connection options. A later call ignores them.</param>
        /// <param name="cancellationToken">Cancels this caller's wait only, not the shared attempt.</param>
        /// <returns>The AI client.</returns>
        /// <exception cref="LdAiException">No SDK key, or the LaunchDarkly client did not initialize.</exception>
        public static Task<ILdAiClient> InitClientAsync(InitOptions options, CancellationToken cancellationToken)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return Lifecycle.InitAsync(options, cancellationToken);
        }

        /// <summary>
        /// Initializes the process-wide client with a LaunchDarkly client the application built.
        /// The caller owns that client's readiness.
        /// </summary>
        /// <param name="client">The LaunchDarkly server-side client.</param>
        /// <returns>The AI client.</returns>
        public static Task<ILdAiClient> InitClientAsync(ILdClient client)
        => InitClientAsync(client, CancellationToken.None);

        /// <summary>
        /// Initializes the process-wide client with a LaunchDarkly client the application built.
        /// The caller owns that client's readiness.
        /// </summary>
        /// <param name="client">The LaunchDarkly server-side client.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The AI client.</returns>
        public static Task<ILdAiClient> InitClientAsync(ILdClient client, CancellationToken cancellationToken)
        {
            if (client is null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<ILdAiClient>(cancellationToken);
            }

            return Task.FromResult(Lifecycle.InitWithClient(client));
        }

        /// <summary>
        /// Returns the process-wide client.
        /// </summary>
        /// <returns>The AI client.</returns>
        /// <exception cref="InvalidOperationException">No client has been initialized.</exception>
        public static ILdAiClient GetClient()
        => Lifecycle.Current ?? throw new InvalidOperationException(
                "No LaunchDarkly AI client has been initialized. Call LdAi.InitClientAsync first.");

        /// <summary>
        /// Flushes and closes the LaunchDarkly client, clears the singleton and restores the
        /// built-in defaults. Safe to call more than once.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when the client is closed.</returns>
        public static Task ShutdownAsync(CancellationToken cancellationToken = default)
        => ShutdownAsync(null, cancellationToken);

        /// <summary>
        /// Sets process-wide defaults. Different options are refused after the first AI invocation.
        /// </summary>
        /// <param name="options">The defaults.</param>
        /// <exception cref="ConfigureLockedException">An AI invocation has run and the options differ.</exception>
        public static void Configure(SdkOptions options)
        => SdkState.Configure(options);

        /// <summary>
        /// Binds an AI Config to its handlers.
        /// </summary>
        /// <param name="args">The config key, handlers and tools.</param>
        /// <returns>The bound config.</returns>
        public static Config Config(ConfigArgs args)
        {
            return Server.Config.Bind(args);
        }

        /// <summary>
        /// Reads an AI Config variation without invoking a model, initializing the client from the
        /// environment if needed. A missing, invalid or unreadable config, or a client that cannot be
        /// initialized, is reported in the result rather than thrown.
        /// </summary>
        /// <param name="key">The AI Config key.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The variation, or the reason it could not be read.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public static Task<InspectResult> InspectConfigAsync(string key, Context context, CancellationToken cancellationToken = default)
        {
            UsageReporter.Report(UsageReporter.InspectConfig);
            return InspectConfigInternalAsync(key, context, cancellationToken);
        }

        /// <summary>
        /// Wraps a handler function as a <see cref="ProviderHandler"/>.
        /// </summary>
        /// <param name="providesFor">The provider and mode the handler serves.</param>
        /// <param name="handler">The handler function.</param>
        /// <returns>The handler.</returns>
        public static ProviderHandler CreateHandler(
            ProvidesFor providesFor,
            Func<HandlerRequest, CancellationToken, Task<HandlerResult>> handler)
        {
            UsageReporter.Report("client.createHandler");
            return ProviderHandler.Create(providesFor, handler ?? throw new ArgumentNullException(nameof(handler)), null, null);
        }

        /// <summary>
        /// Wraps a handler function as a <see cref="ProviderHandler"/>.
        /// </summary>
        /// <param name="providesFor">The provider and mode the handler serves.</param>
        /// <param name="handler">The handler function.</param>
        /// <param name="options">Content capture and provider scoping.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentException">The provider list is empty, or holds a blank or padded name.</exception>
        public static ProviderHandler CreateHandler(
            ProvidesFor providesFor,
            Func<HandlerRequest, CancellationToken, Task<HandlerResult>> handler,
            CreateHandlerOptions options)
        {
            UsageReporter.Report("client.createHandler");
            return ProviderHandler.Create(
                providesFor,
                handler ?? throw new ArgumentNullException(nameof(handler)),
                options ?? throw new ArgumentNullException(nameof(options)),
                null);
        }

        /// <summary>
        /// Wraps an existing handler under new metadata, keeping its content-capture override and
        /// provider list.
        /// </summary>
        /// <param name="providesFor">The provider and mode the handler serves.</param>
        /// <param name="handler">The handler to wrap.</param>
        /// <returns>The handler.</returns>
        public static ProviderHandler CreateHandler(ProvidesFor providesFor, ProviderHandler handler)
        {
            UsageReporter.Report("client.createHandler");
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ProviderHandler.Create(providesFor, handler.InvokeAsync, null, handler);
        }

        /// <summary>
        /// Wraps an existing handler under new metadata. Settings not set in
        /// <paramref name="options"/> are copied from <paramref name="handler"/>.
        /// </summary>
        /// <param name="providesFor">The provider and mode the handler serves.</param>
        /// <param name="handler">The handler to wrap.</param>
        /// <param name="options">Content capture and provider scoping.</param>
        /// <returns>The handler.</returns>
        /// <exception cref="ArgumentException">The provider list is empty, or holds a blank or padded name.</exception>
        public static ProviderHandler CreateHandler(ProvidesFor providesFor, ProviderHandler handler, CreateHandlerOptions options)
        {
            UsageReporter.Report("client.createHandler");
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ProviderHandler.Create(providesFor, handler.InvokeAsync, options ?? throw new ArgumentNullException(nameof(options)), handler);
        }

        /// <summary>
        /// Replaces <c>{{name}}</c> and <c>{{a.b}}</c> placeholders with values from
        /// <paramref name="variables"/>. A placeholder with no value is left as it is.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="variables">The values. Nested values may be dictionaries, JSON values or a <see cref="Context"/>.</param>
        /// <returns>The rendered text.</returns>
        public static string ParseTemplate(string template, IReadOnlyDictionary<string, object?>? variables)
        {
            if (template is null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            return Template.Render(template, variables);
        }

        /// <summary>
        /// Registers a LaunchDarkly AI package so the SDK reports it once per client. For use by
        /// LaunchDarkly provider packages.
        /// </summary>
        /// <param name="name">The package ID.</param>
        /// <param name="version">The package version.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void RegisterAiSdkPackage(string name, string version)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A package name is required.", nameof(name));
            }

            if (string.IsNullOrEmpty(version))
            {
                throw new ArgumentException("A package version is required.", nameof(version));
            }

            SdkInfoReporter.Register(name, version);
        }

        /// <summary>
        /// Records that an application called a public helper. For use by LaunchDarkly provider
        /// packages.
        /// </summary>
        /// <param name="helper">The helper's usage ID.</param>
        /// <param name="packageName">The package the helper belongs to; the core package when omitted.</param>
        /// <param name="packageVersion">That package's version; the core version when omitted.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void ReportUsage(string helper, string? packageName = null, string? packageVersion = null)
        {
            if (string.IsNullOrEmpty(helper))
            {
                throw new ArgumentException("A helper usage ID is required.", nameof(helper));
            }

            UsageReporter.Report(helper, packageName, packageVersion);
        }

        /// <summary>
        /// <see cref="InspectConfigAsync(string, Context, CancellationToken)"/> without the usage
        /// report, for SDK code.
        /// </summary>
        /// <param name="key">The AI Config key.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The variation, or the reason it could not be read.</returns>
        internal static async Task<InspectResult> InspectConfigInternalAsync(string key, Context context, CancellationToken cancellationToken)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var client = await Lifecycle.GetLdClientAsync(cancellationToken).ConfigureAwait(false);
                var variation = client.JsonVariation(key, context, LdValue.Null);
                return ConfigParser.Inspect(key, variation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                return new InspectResult { Error = e };
            }
        }

        /// <summary>
        /// Clears the singleton and restores the built-in defaults at once, then flushes and closes
        /// the client off the caller's thread.
        /// </summary>
        /// <param name="only">When set, shuts down only if this instance is still the singleton.</param>
        /// <param name="cancellationToken">Cancels the wait, not the close.</param>
        /// <returns>A task that completes when the client is closed.</returns>
        internal static Task ShutdownAsync(LdAiClient? only, CancellationToken cancellationToken)
        {
            var client = Lifecycle.Detach(only);
            if (client is null)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => Lifecycle.Teardown(client), CancellationToken.None).WaitForAsync(cancellationToken);
        }
    }
}
