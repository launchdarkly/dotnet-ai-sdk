using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The process-wide AI client and the attempt that builds it. Every change to the singleton is
    /// made under one lock; the base client is built and closed outside it.
    /// </summary>
    internal static class Lifecycle
    {
        /// <summary>How long to wait for the base client to initialize when the options set no time.</summary>
        internal static readonly TimeSpan DefaultStartWaitTime = TimeSpan.FromSeconds(10);

        private const string SdkKeyEnvironmentVariable = "LD_SDK_KEY";

        private static readonly object _lock = new object();

        // Written under _lock; read without it by the reporters, which only need the latest value.
        private static volatile LdAiClient? _current;

        // The attempt in flight, or null. Replaced (set to another value or null) by a passed
        // client or a shutdown, which is how the attempt learns not to install what it built.
        private static Attempt? _attempt;

        /// <summary>The singleton's LaunchDarkly client, or <see langword="null"/>.</summary>
        internal static ILdClient? CurrentLdClient => _current?.LdClient;

        /// <summary>The singleton, or <see langword="null"/>.</summary>
        internal static ILdAiClient? Current => _current;

        /// <summary>
        /// Returns the singleton's LaunchDarkly client, initializing the singleton from
        /// <c>LD_SDK_KEY</c> first when there is none.
        /// </summary>
        /// <param name="cancellationToken">Cancels this caller's wait.</param>
        /// <returns>The LaunchDarkly client.</returns>
        /// <exception cref="LdAiException">The client could not be initialized.</exception>
        internal static async Task<ILdClient> GetLdClientAsync(CancellationToken cancellationToken)
        {
            var current = _current;
            if (current != null)
            {
                return current.LdClient;
            }

            var client = await InitAsync(null, cancellationToken).ConfigureAwait(false);
            return client.LdClient;
        }

        /// <summary>
        /// Returns the singleton, or joins or starts the attempt that builds it.
        /// </summary>
        /// <param name="options">The caller's options. Ignored when a client exists or an attempt is in flight.</param>
        /// <param name="cancellationToken">Cancels this caller's wait only.</param>
        /// <returns>The AI client.</returns>
        internal static Task<ILdAiClient> InitAsync(InitOptions? options, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<ILdAiClient>(cancellationToken);
            }

            LdAiClient? existing;
            Task<ILdAiClient> attemptTask;
            lock (_lock)
            {
                existing = _current;
                if (existing is null)
                {
                    if (_attempt is null)
                    {
                        var sdkKey = ResolveSdkKey(options);
                        if (sdkKey is null)
                        {
                            return Task.FromException<ILdAiClient>(new LdAiException(
                                "No SDK key: set the LD_SDK_KEY environment variable or InitOptions.SdkKey."));
                        }

                        _attempt = Attempt.Start(sdkKey, options ?? new InitOptions());
                    }

                    attemptTask = _attempt.Task;
                }
                else
                {
                    attemptTask = Task.FromResult<ILdAiClient>(existing);
                }
            }

            if (existing != null)
            {
                // Packages registered since the last init report now, on the already-initialized path.
                ReportTo(existing.LdClient);
                return attemptTask;
            }

            return attemptTask.WaitForAsync(cancellationToken);
        }

        /// <summary>
        /// Installs a client the application built, replacing any attempt in flight. The caller
        /// owns that client's readiness, so it is not checked. When a client is already installed,
        /// that one is returned and <paramref name="client"/> is neither installed nor closed:
        /// replacing the installed client would leave it, and its streaming connection, open with
        /// nothing to close it, and the caller owns <paramref name="client"/>. Call
        /// <c>ShutdownAsync</c> first to switch clients.
        /// </summary>
        /// <param name="client">The LaunchDarkly client.</param>
        /// <returns>The AI client.</returns>
        internal static ILdAiClient InitWithClient(ILdClient client)
        {
            LdAiClient installed;
            lock (_lock)
            {
                if (_current is { } existing)
                {
                    installed = existing;
                }
                else
                {
                    _attempt = null;
                    installed = new LdAiClient(client);
                    _current = installed;
                }
            }

            // Packages registered since the last init report now, to whichever client is installed.
            ReportTo(installed.LdClient);
            return installed;
        }

        /// <summary>
        /// Clears the singleton and restores the built-in defaults, then returns the client that
        /// was installed, which the caller tears down. Abandons any attempt in flight.
        /// </summary>
        /// <param name="only">When set, clears the singleton only if it is this instance.</param>
        /// <returns>The client to tear down, or <see langword="null"/>.</returns>
        internal static ILdClient? Detach(LdAiClient? only = null)
        {
            ILdClient? client;
            lock (_lock)
            {
                if (only != null && !ReferenceEquals(_current, only))
                {
                    return null;
                }

                _attempt = null;
                client = _current?.LdClient;
                _current = null;
            }

            SdkState.Reset();
            SdkInfoReporter.ResetReported();
            UsageReporter.Reset();
            JudgeWarning.Reset();
            return client;
        }

        /// <summary>
        /// Flushes <paramref name="client"/> and, when it is disposable, closes it. Never throws: a failure is logged, and
        /// a flush failure does not stop the close.
        /// </summary>
        /// <param name="client">The client to tear down.</param>
        internal static void Teardown(ILdClient client)
        {
            try
            {
                client.Flush();
            }
            catch (Exception e)
            {
                SdkLog.Logger.Warn("Flushing the LaunchDarkly client during shutdown failed: {0}", e.Message);
            }
            finally
            {
                Close(client);
            }
        }

        /// <summary>
        /// Installs <paramref name="client"/> with no side effects, for tests.
        /// </summary>
        /// <param name="client">The client to install.</param>
        internal static void InstallForTests(ILdClient client)
        {
            lock (_lock)
            {
                _attempt = null;
                _current = new LdAiClient(client);
            }
        }

        /// <summary>
        /// Forgets the singleton and any attempt without closing anything, for tests.
        /// </summary>
        internal static void ResetForTests()
        {
            lock (_lock)
            {
                _attempt = null;
                _current = null;
            }
        }

        private static string? ResolveSdkKey(InitOptions? options)
        {
            var key = options?.SdkKey;
            if (string.IsNullOrEmpty(key))
            {
                key = Environment.GetEnvironmentVariable(SdkKeyEnvironmentVariable);
            }

            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static void ReportTo(ILdClient client)
        {
            SdkInfoReporter.ReportTo(client);
            UsageReporter.Flush(client);
        }

        private static void Close(ILdClient client)
        {
            try
            {
                (client as IDisposable)?.Dispose();
            }
            catch (Exception e)
            {
                SdkLog.Logger.Warn("Closing the LaunchDarkly client failed: {0}", e.Message);
            }
        }

        private static ILdClient BuildClient(string sdkKey, InitOptions options)
        {
            var factory = TestHooks.ClientFactory;
            if (factory != null)
            {
                return factory(sdkKey, options);
            }

            var startWait = options.StartWaitTime is TimeSpan wait && wait > TimeSpan.Zero ? wait : DefaultStartWaitTime;
            var builder = Configuration.Builder(sdkKey).StartWaitTime(startWait);
            if (options.BaseUri != null || options.StreamUri != null || options.EventsUri != null)
            {
                var endpoints = Components.ServiceEndpoints();
                if (options.BaseUri != null)
                {
                    endpoints = endpoints.Polling(options.BaseUri);
                }

                if (options.StreamUri != null)
                {
                    endpoints = endpoints.Streaming(options.StreamUri);
                }

                if (options.EventsUri != null)
                {
                    endpoints = endpoints.Events(options.EventsUri);
                }

                builder = builder.ServiceEndpoints(endpoints);
            }

            if (TestHooks.LogAdapter != null)
            {
                builder = builder.Logging(TestHooks.LogAdapter);
            }

            return new LdClient(builder.Build());
        }

        /// <summary>
        /// One attempt to build and install the base client. Concurrent callers share it.
        /// </summary>
        private sealed class Attempt
        {
            private Attempt()
            {
            }

            internal Task<ILdAiClient> Task { get; private set; } = null!;

            internal static Attempt Start(string sdkKey, InitOptions options)
            {
                var attempt = new Attempt();

                // The base client's constructor blocks until it connects or StartWaitTime passes,
                // so it runs on a pool thread rather than the caller's.
                attempt.Task = System.Threading.Tasks.Task.Run(() => attempt.Run(sdkKey, options));

                // A caller that stopped waiting leaves the failure unobserved; observe it here.
                attempt.Task.ContinueWith(
                    t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return attempt;
            }

            private ILdAiClient Run(string sdkKey, InitOptions options)
            {
                ILdClient client;
                try
                {
                    client = BuildClient(sdkKey, options);
                }
                catch
                {
                    Drop();
                    throw;
                }

                LdAiClient? installed = null;
                var replaced = false;
                lock (_lock)
                {
                    if (!ReferenceEquals(_attempt, this))
                    {
                        // A shutdown or a passed client replaced this attempt while it ran.
                        replaced = true;
                    }
                    else
                    {
                        _attempt = null;
                        if (client.Initialized)
                        {
                            installed = new LdAiClient(client);
                            _current = installed;
                        }
                    }
                }

                if (installed is null)
                {
                    Close(client);
                    throw new LdAiException(replaced
                        ? "LaunchDarkly AI client initialization was abandoned: the SDK was shut down or given a client while it ran."
                        : "The LaunchDarkly client failed to initialize. Check the SDK key and the connection to LaunchDarkly.");
                }

                ReportTo(client);
                return installed;
            }

            private void Drop()
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_attempt, this))
                    {
                        _attempt = null;
                    }
                }
            }
        }
    }
}
