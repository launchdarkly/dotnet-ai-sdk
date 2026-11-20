using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Initializing, reading and shutting down the process-wide client.
    /// </summary>
    public class LifecycleTests : LdAiTestBase
    {
        [Fact]
        public async Task InitWithoutSdkKeyThrowsNamingTheMissingKey()
        {
            var ex = await Assert.ThrowsAnyAsync<LdAiException>(() => LdAi.InitClientAsync());
            Assert.Contains("SDK key", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Built);
        }

        [Fact]
        public async Task InitReadsSdkKeyFromEnvironment()
        {
            Environment.SetEnvironmentVariable("LD_SDK_KEY", "sdk-from-env");
            await LdAi.InitClientAsync();
            Assert.Equal("sdk-from-env", Assert.Single(Built).SdkKey);
        }

        [Fact]
        public async Task SdkKeyOptionWinsOverEnvironment()
        {
            Environment.SetEnvironmentVariable("LD_SDK_KEY", "sdk-from-env");
            await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-from-option" });
            Assert.Equal("sdk-from-option", Assert.Single(Built).SdkKey);
        }

        [Fact]
        public async Task InitNeverLoadsADotEnvFile()
        {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ldai-dotenv-" + Guid.NewGuid().ToString("N")));
            var saved = Directory.GetCurrentDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, ".env"), "LD_SDK_KEY=sdk-from-dotenv\nLDAI_DOTENV_SENTINEL=loaded\n");
                Directory.SetCurrentDirectory(dir.FullName);

                await Assert.ThrowsAnyAsync<LdAiException>(() => LdAi.InitClientAsync());

                Assert.Null(Environment.GetEnvironmentVariable("LD_SDK_KEY"));
                Assert.Null(Environment.GetEnvironmentVariable("LDAI_DOTENV_SENTINEL"));
            }
            finally
            {
                Directory.SetCurrentDirectory(saved);
                dir.Delete(true);
                Environment.SetEnvironmentVariable("LDAI_DOTENV_SENTINEL", null);
            }
        }

        [Fact]
        public void CoreHasNoDotEnvDependency()
        {
            var references = typeof(LdAi).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
            Assert.DoesNotContain(references, n => n.IndexOf("dotenv", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task InitReturnsTheBuiltClient()
        {
            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Same(Assert.Single(Built), client.LdClient);
        }

        [Fact]
        public async Task InitPassesConnectionOptionsToTheBaseClient()
        {
            var options = new InitOptions
            {
                SdkKey = "sdk-key",
                BaseUri = new Uri("https://base.example"),
                StreamUri = new Uri("https://stream.example"),
                EventsUri = new Uri("https://events.example"),
            };
            await LdAi.InitClientAsync(options);
            Assert.Same(options, Assert.Single(Built).BuiltWith);
        }

        [Fact]
        public async Task InitIsIdempotent()
        {
            var first = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            var second = await LdAi.InitClientAsync(new InitOptions { SdkKey = "other-key" });
            Assert.Single(Built);
            Assert.Same(first.LdClient, second.LdClient);
        }

        [Fact]
        public async Task InitFailsWhenTheBaseClientDidNotInitialize()
        {
            NextClient.IsInitialized = false;
            await Assert.ThrowsAnyAsync<LdAiException>(() => LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }));

            var failed = Assert.Single(Built);
            Assert.Equal(1, failed.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());

            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Equal(2, Built.Count);
            Assert.Same(Built[1], client.LdClient);
        }

        [Fact]
        public async Task CloseErrorDoesNotReplaceTheInitError()
        {
            NextClient.IsInitialized = false;
            NextClient.CloseThrows = true;

            var ex = await Record.ExceptionAsync(() => LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }));

            Assert.IsAssignableFrom<LdAiException>(ex);
            Assert.DoesNotContain("close exploded", ex!.ToString(), StringComparison.Ordinal);
            var failed = Assert.Single(Built);
            Assert.Equal(1, failed.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task FailedInitIsNotCached()
        {
            await Assert.ThrowsAnyAsync<LdAiException>(() => LdAi.InitClientAsync());

            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            var built = Assert.Single(Built);
            Assert.Equal("sdk-key", built.SdkKey);
            Assert.Same(built, client.LdClient);
        }

        [Fact]
        public async Task ConstructionErrorDoesNotLeaveAClientBehind()
        {
            OnBuild = (_, _) => throw new InvalidOperationException("constructor failed");
            // The constructor's error reaches the caller, as itself or wrapped.
            var ex = await Record.ExceptionAsync(() => LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }));
            var original = ex as InvalidOperationException ?? ex?.InnerException as InvalidOperationException;
            Assert.Equal("constructor failed", original?.Message);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());

            OnBuild = null;
            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.NotNull(client.LdClient);
        }

        [Fact]
        public async Task InitDoesNotBlockTheCallerWhileTheClientIsBuilt()
        {
            using var release = new ManualResetEventSlim(false);
            OnBuild = (_, _) => release.Wait(TimeSpan.FromSeconds(10));

            var init = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            var other = Task.Run(() => 42);
            var otherFinished = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(5))) == other;
            Assert.False(init.IsCompleted);
            release.Set();
            await init;

            Assert.True(otherFinished);
        }

        [Fact]
        public async Task ConcurrentFirstCallsShareOneAttempt()
        {
            using var release = new ManualResetEventSlim(false);
            OnBuild = (_, _) => release.Wait(TimeSpan.FromSeconds(10));

            var calls = Enumerable.Range(0, 5)
                .Select(_ => LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }))
                .ToList();
            await Task.Delay(100);
            release.Set();
            var clients = await Task.WhenAll(calls);

            var built = Assert.Single(Built);
            Assert.All(clients, c => Assert.Same(built, c.LdClient));
        }

        [Fact]
        public async Task ConcurrentCallsAllFailTogetherAndTheNextCallRetries()
        {
            using var release = new ManualResetEventSlim(false);
            OnBuild = (_, _) => release.Wait(TimeSpan.FromSeconds(10));
            NextClient.IsInitialized = false;

            var calls = Enumerable.Range(0, 3)
                .Select(_ => LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }))
                .ToList();
            await Task.Delay(100);
            release.Set();
            foreach (var call in calls)
            {
                await Assert.ThrowsAnyAsync<LdAiException>(() => call);
            }

            var failed = Assert.Single(Built);
            Assert.Equal(1, failed.Closes);

            OnBuild = null;
            await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Equal(2, Built.Count);
        }

        [Fact]
        public async Task CancellingOneCallerDoesNotCancelTheSharedAttempt()
        {
            using var release = new ManualResetEventSlim(false);
            OnBuild = (_, _) => release.Wait(TimeSpan.FromSeconds(10));
            using var cts = new CancellationTokenSource();

            var cancelled = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" }, cts.Token);
            var other = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            release.Set();
            var client = await other;
            Assert.Same(Assert.Single(Built), client.LdClient);
        }

        [Fact]
        public async Task ShutdownDuringAnAttemptClosesTheClientItBuilds()
        {
            using var release = new ManualResetEventSlim(false);
            var building = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnBuild = (_, _) =>
            {
                building.TrySetResult(true);
                release.Wait(TimeSpan.FromSeconds(10));
            };

            var init = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            // Wait until the attempt is inside the blocked build.
            await building.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LdAi.ShutdownAsync();
            release.Set();

            await Assert.ThrowsAnyAsync<LdAiException>(() => init);
            Assert.Equal(1, Assert.Single(Built).Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task PassedClientDuringAnAttemptReplacesIt()
        {
            using var release = new ManualResetEventSlim(false);
            var building = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnBuild = (_, _) =>
            {
                building.TrySetResult(true);
                release.Wait(TimeSpan.FromSeconds(10));
            };
            var passed = new FakeLdClient();

            var init = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            // Wait until the attempt is inside the blocked build.
            await building.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LdAi.InitClientAsync(passed);
            release.Set();
            var ex = await Assert.ThrowsAsync<LdAiException>(() => init);
            Assert.Contains("abandoned", ex.Message, StringComparison.Ordinal);

            Assert.Equal(1, Assert.Single(Built).Closes);
            Assert.Same(passed, LdAi.GetClient().LdClient);
            Assert.Equal(0, passed.Closes);
        }

        [Fact]
        public async Task ShutdownAfterALaterAttemptCompletedStillRejectsTheFirstCaller()
        {
            using var release = new ManualResetEventSlim(false);
            var building = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var builds = 0;
            OnBuild = (_, _) =>
            {
                if (Interlocked.Increment(ref builds) == 1)
                {
                    building.TrySetResult(true);
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            };

            var first = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            // Wait until the attempt is inside the blocked build.
            await building.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LdAi.ShutdownAsync();
            var second = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            release.Set();

            await Assert.ThrowsAnyAsync<LdAiException>(() => first);
            // The factory records a client after OnBuild returns, so the first attempt's
            // client is recorded last; tell them apart by reference.
            Assert.Equal(2, Built.Count);
            var later = Assert.Single(Built, c => ReferenceEquals(c, second.LdClient));
            var abandoned = Assert.Single(Built, c => !ReferenceEquals(c, second.LdClient));
            Assert.Equal(1, abandoned.Closes);
            Assert.Same(later, LdAi.GetClient().LdClient);
            Assert.Equal(0, later.Closes);
        }

        [Fact]
        public async Task ShutdownAfterAPassedClientReplacedTheAttempt()
        {
            using var release = new ManualResetEventSlim(false);
            var building = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            OnBuild = (_, _) =>
            {
                building.TrySetResult(true);
                release.Wait(TimeSpan.FromSeconds(10));
            };
            var passed = new FakeLdClient();

            var init = LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            // Wait until the attempt is inside the blocked build.
            await building.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await LdAi.InitClientAsync(passed);
            await LdAi.ShutdownAsync();
            release.Set();

            await Assert.ThrowsAnyAsync<LdAiException>(() => init);
            Assert.Equal(1, Assert.Single(Built).Closes);
            Assert.Equal(1, passed.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task PassedClientIsReturnedWithoutBuildingOne()
        {
            var passed = new FakeLdClient { IsInitialized = false };
            var client = await LdAi.InitClientAsync(passed);

            Assert.Same(passed, client.LdClient);
            Assert.Same(passed, LdAi.GetClient().LdClient);
            Assert.Empty(Built);
        }

        [Fact]
        public async Task SecondPassedClientKeepsTheInstalledOne()
        {
            var first = new FakeLdClient();
            var second = new FakeLdClient();
            var installed = await LdAi.InitClientAsync(first);

            var again = await LdAi.InitClientAsync(second);

            Assert.Same(installed, again);
            Assert.Same(first, again.LdClient);
            Assert.Same(first, LdAi.GetClient().LdClient);
            Assert.Equal(0, first.Closes);
            Assert.Equal(0, second.Closes);
        }

        [Fact]
        public void GetClientThrowsBeforeInit()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
            Assert.Contains("InitClientAsync", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetClientReturnsTheClientAfterInit()
        {
            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Same(client, LdAi.GetClient());
        }

        [Fact]
        public async Task ShutdownClearsTheSingleton()
        {
            UseFakeClient();
            await LdAi.ShutdownAsync();
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task ShutdownFlushesAndClosesTheClient()
        {
            var fake = UseFakeClient();
            await LdAi.ShutdownAsync();
            Assert.True(fake.Flushes >= 1);
            Assert.Equal(1, fake.Closes);
        }

        [Fact]
        public async Task ShutdownThroughTheClientInterfaceBehavesTheSame()
        {
            var fake = UseFakeClient();
            await LdAi.GetClient().ShutdownAsync();
            Assert.Equal(1, fake.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task DisposingTheClientShutsItDown()
        {
            var fake = UseFakeClient();
            LdAi.GetClient().Dispose();
            Assert.Equal(1, fake.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
        }

        [Fact]
        public async Task InitWorksAgainAfterShutdown()
        {
            await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            await LdAi.ShutdownAsync();
            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Equal(2, Built.Count);
            Assert.Same(Built[1], client.LdClient);
        }

        [Fact]
        public async Task InitWorksAfterAFailedInitAndShutdown()
        {
            await Assert.ThrowsAnyAsync<LdAiException>(() => LdAi.InitClientAsync());
            await LdAi.ShutdownAsync();
            var client = await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Same(Assert.Single(Built), client.LdClient);
        }

        [Fact]
        public async Task ShutdownIsIdempotent()
        {
            await LdAi.ShutdownAsync();
            UseFakeClient();
            await LdAi.ShutdownAsync();
            await LdAi.ShutdownAsync();
        }

        [Fact]
        public async Task ShutdownCompletesTeardownWhenFlushThrows()
        {
            var fake = UseFakeClient(new FakeLdClient { FlushThrows = true });
            try
            {
                await LdAi.ShutdownAsync();
            }
            catch (InvalidOperationException)
            {
                // A flush failure may surface; the teardown below must still have happened.
            }

            Assert.Equal(1, fake.Closes);
            Assert.Throws<InvalidOperationException>(() => LdAi.GetClient());
            await LdAi.ShutdownAsync();
        }

        [Fact]
        public async Task ShutdownRestoresDefaultsEvenWithoutAClient()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            var handler = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages);
            await handler.InvokeAsync(new HandlerRequest(Fixtures.Config()));

            await LdAi.ShutdownAsync();

            Assert.True(handler.EffectiveCaptureContent);
            LdAi.Configure(new SdkOptions { CaptureContent = false });
        }
    }
}
