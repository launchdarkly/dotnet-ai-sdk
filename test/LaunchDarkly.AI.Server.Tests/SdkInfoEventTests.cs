using System;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using LaunchDarkly.Sdk;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The once-per-client <c>$ld:ai:sdk:info</c> event for each registered AI package.
    /// </summary>
    public class SdkInfoEventTests : LdAiTestBase
    {
        private const string SdkInfo = "$ld:ai:sdk:info";

        private static string UniquePackage() => "Test.Package." + Guid.NewGuid().ToString("N");

        private static TrackedEvent[] InfoFor(FakeLdClient client, string name) =>
            client.Events(SdkInfo).Where(e => e.DataString("aiSdkName") == name).ToArray();

        [Fact]
        public async Task CoreReportsItsOwnIdentityOnInit()
        {
            var fake = await InitWithFakeAsync();

            var core = Assert.Single(InfoFor(fake, "LaunchDarkly.AI.Server"));
            Assert.Equal(Server.SdkInfo.Version, core.DataString("aiSdkVersion"));
            Assert.Equal("c#", core.DataString("aiSdkLanguage"));
        }

        [Fact]
        public async Task EventPayloadMatchesTheContractExactly()
        {
            var fake = await InitWithFakeAsync();

            var core = Assert.Single(InfoFor(fake, "LaunchDarkly.AI.Server"));
            Assert.Equal(1.0, core.Metric);
            Assert.Equal("LaunchDarkly.AI.Server", core.DataString("aiSdkName"));
            Assert.Equal(Server.SdkInfo.Version, core.DataString("aiSdkVersion"));
            Assert.Equal("c#", core.DataString("aiSdkLanguage"));
            Assert.Equal(new[] { "aiSdkLanguage", "aiSdkName", "aiSdkVersion" },
                core.DataJson.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public async Task EventUsesTheInternalAnonymousContext()
        {
            var fake = await InitWithFakeAsync();

            var context = Assert.Single(InfoFor(fake, "LaunchDarkly.AI.Server")).Context;
            Assert.Equal(ContextKind.Of("ld_ai"), context.Kind);
            Assert.Equal("ld-internal-tracking", context.Key);
            Assert.True(context.Anonymous);
        }

        [Fact]
        public async Task SameNameAndVersionReportsOnce()
        {
            var name = UniquePackage();
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            var fake = await InitWithFakeAsync();
            await LdAi.InitClientAsync(fake);

            Assert.Single(InfoFor(fake, name));
        }

        [Fact]
        public async Task TwoVersionsOfOnePackageBothReport()
        {
            var name = UniquePackage();
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            LdAi.RegisterAiSdkPackage(name, "2.0.0");
            var fake = await InitWithFakeAsync();

            Assert.Equal(new[] { "1.0.0", "2.0.0" },
                InfoFor(fake, name).Select(e => e.DataString("aiSdkVersion")).OrderBy(v => v, StringComparer.Ordinal));
        }

        [Fact]
        public async Task LateRegistrationReportsOnTheNextInit()
        {
            var fake = await InitWithFakeAsync();
            var name = UniquePackage();
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            Assert.Empty(InfoFor(fake, name));

            await LdAi.InitClientAsync(new InitOptions { SdkKey = "ignored" });

            Assert.Single(InfoFor(fake, name));
        }

        [Fact]
        public async Task RepeatedInitDoesNotReportAgain()
        {
            var fake = await InitWithFakeAsync();
            await LdAi.InitClientAsync(fake);
            await LdAi.InitClientAsync(new InitOptions { SdkKey = "ignored" });

            Assert.Single(InfoFor(fake, "LaunchDarkly.AI.Server"));
        }

        [Fact]
        public async Task ShutdownClearsReportingButKeepsRegistrations()
        {
            var name = UniquePackage();
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            await InitWithFakeAsync();
            await LdAi.ShutdownAsync();

            var second = await InitWithFakeAsync();

            Assert.Single(InfoFor(second, name));
            Assert.Single(InfoFor(second, "LaunchDarkly.AI.Server"));
        }

        [Fact]
        public async Task ReplacingAPassedClientWithoutShutdownDoesNotReportAgain()
        {
            await InitWithFakeAsync();
            var replacement = await InitWithFakeAsync();

            Assert.Empty(InfoFor(replacement, "LaunchDarkly.AI.Server"));
        }

        [Fact]
        public async Task BuiltClientReceivesTheEvents()
        {
            await LdAi.InitClientAsync(new InitOptions { SdkKey = "sdk-key" });
            Assert.Single(InfoFor(Assert.Single(Built), "LaunchDarkly.AI.Server"));
        }

        [Fact]
        public async Task ThrowingTrackDoesNotFailInitAndIsNotRetried()
        {
            var fake = new FakeLdClient { TrackThrows = true };
            var client = await LdAi.InitClientAsync(fake);
            Assert.Same(fake, client.LdClient);

            fake.TrackThrows = false;
            await LdAi.InitClientAsync(fake);
            Assert.Empty(InfoFor(fake, "LaunchDarkly.AI.Server"));
        }

        [Fact]
        public void TestOnlyClientInjectionSendsNothing()
        {
            var name = UniquePackage();
            LdAi.RegisterAiSdkPackage(name, "1.0.0");
            var fake = new FakeLdClient();

            Server.Internal.TestHooks.SetClient(fake);

            Assert.Same(fake, LdAi.GetClient().LdClient);
            Assert.Empty(fake.Events(SdkInfo));
        }
    }
}
