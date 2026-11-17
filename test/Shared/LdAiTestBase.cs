using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.AI.Server.Internal;
using LaunchDarkly.Logging;
using Xunit;

namespace LaunchDarkly.AI.TestHelpers
{
    /// <summary>
    /// Base class for tests that touch the process-wide client. Each test starts from a reset
    /// singleton, a client factory that builds <see cref="FakeLdClient"/>s instead of real clients,
    /// a captured log and no <c>LD_SDK_KEY</c>.
    /// </summary>
    public abstract class LdAiTestBase : IAsyncLifetime
    {
        private readonly string? _savedSdkKey = Environment.GetEnvironmentVariable("LD_SDK_KEY");
        private readonly List<FakeLdClient> _built = new List<FakeLdClient>();

        protected LdAiTestBase()
        {
            Logs = LaunchDarkly.Logging.Logs.Capture();
        }

        /// <summary>The SDK's captured log output.</summary>
        protected LogCapture Logs { get; }

        /// <summary>The client the next factory call returns. Replaced with a new one after each build.</summary>
        protected FakeLdClient NextClient { get; set; } = new FakeLdClient();

        /// <summary>Runs inside the factory before it returns, for tests that need to block or fail it.</summary>
        protected Action<string, InitOptions>? OnBuild { get; set; }

        /// <summary>The clients the factory built, in order.</summary>
        protected IReadOnlyList<FakeLdClient> Built
        {
            get
            {
                lock (_built)
                {
                    return _built.ToList();
                }
            }
        }

        public virtual Task InitializeAsync()
        {
            Environment.SetEnvironmentVariable("LD_SDK_KEY", null);
            TestHooks.Reset();
            TestHooks.LogAdapter = Logs;
            TestHooks.ClientFactory = (sdkKey, options) =>
            {
                OnBuild?.Invoke(sdkKey, options);
                FakeLdClient client;
                lock (_built)
                {
                    client = NextClient;
                    client.SdkKey = sdkKey;
                    client.BuiltWith = options;
                    _built.Add(client);
                    NextClient = new FakeLdClient();
                }

                return client;
            };
            return Task.CompletedTask;
        }

        public virtual Task DisposeAsync()
        {
            TestHooks.ClientFactory = null;
            TestHooks.LogAdapter = null;
            Environment.SetEnvironmentVariable("LD_SDK_KEY", _savedSdkKey);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Installs a fake client through the test seam, with no init side effects, and returns it.
        /// For tests whose subject is not initialization.
        /// </summary>
        protected static FakeLdClient UseFakeClient(FakeLdClient? client = null)
        {
            client ??= new FakeLdClient();
            TestHooks.SetClient(client);
            return client;
        }

        /// <summary>Installs a fresh fake client through the public BYOC init path and returns it.</summary>
        protected async Task<FakeLdClient> InitWithFakeAsync(FakeLdClient? client = null)
        {
            client ??= new FakeLdClient();
            await LdAi.InitClientAsync(client);
            return client;
        }

        /// <summary>The warning-level messages logged so far.</summary>
        protected IReadOnlyList<string> Warnings =>
            Logs.GetMessages().Where(m => m.Level == LogLevel.Warn).Select(m => m.Text).ToList();
    }
}
