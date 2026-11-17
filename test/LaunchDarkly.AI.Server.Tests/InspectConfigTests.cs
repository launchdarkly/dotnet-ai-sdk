using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Reading a variation without invoking a model. It never throws: a failure is the
    /// <see cref="InspectResult.Error"/>, with the other fields empty.
    /// </summary>
    public class InspectConfigTests : LdAiTestBase
    {
        private async Task<(FakeLdClient Fake, InspectResult Result)> InspectAsync(Action<FakeLdClient> setup)
        {
            var fake = UseFakeClient();
            setup(fake);
            fake.ClearTracks();
            var result = await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);
            return (fake, result);
        }

        private static void AssertEmptyWithError(InspectResult result)
        {
            Assert.False(result.Enabled);
            Assert.Null(result.Config);
            Assert.Null(result.Meta);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task EnabledVariationReturnsConfigAndMeta()
        {
            var (fake, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig());

            Assert.True(result.Enabled);
            Assert.Null(result.Error);
            Assert.Equal("test-model", result.Config!.Model.Name);
            Assert.True(result.Meta!.Enabled);
            Assert.Equal("v1", result.Meta.VariationKey);
            Assert.Equal(2, result.Meta.Version);
            Assert.Equal("messages", result.Meta.Mode);
            Assert.Equal(Fixtures.ConfigKey, fake.Evaluations.Single().Key);
            Assert.Equal(Fixtures.UserContext, fake.Evaluations.Single().Context);
        }

        [Fact]
        public async Task MetaKeepsModelKeyAndVersion()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j =>
            {
                var meta = (JsonObject)j["_ldMeta"]!;
                meta["modelKey"] = "my-model";
                meta["modelVersion"] = 3;
            }));

            Assert.Equal("my-model", result.Meta!.ModelKey);
            Assert.Equal(3L, result.Meta.ModelVersion);
        }

        [Fact]
        public async Task DisabledVariationReturnsMetaWithoutConfig()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] =
                Fixtures.EnabledConfig(edit: j => ((JsonObject)j["_ldMeta"]!)["enabled"] = false));

            Assert.False(result.Enabled);
            Assert.Null(result.Config);
            Assert.Null(result.Error);
            Assert.False(result.Meta!.Enabled);
            Assert.Equal("v1", result.Meta.VariationKey);
        }

        [Fact]
        public async Task MetaWithoutEnabledIsDisabled()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] =
                Fixtures.EnabledConfig(edit: j => ((JsonObject)j["_ldMeta"]!).Remove("enabled")));

            Assert.False(result.Enabled);
            Assert.Null(result.Config);
            Assert.Null(result.Error);
            Assert.NotNull(result.Meta);
        }

        [Fact]
        public async Task EnabledThatIsNotTrueIsDisabled()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] =
                Fixtures.EnabledConfig(edit: j => ((JsonObject)j["_ldMeta"]!)["enabled"] = "true"));

            Assert.False(result.Enabled);
            Assert.Null(result.Config);
        }

        [Fact]
        public async Task MissingMetaIsNotFound()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] =
                Fixtures.EnabledConfig(edit: j => j.Remove("_ldMeta")));

            AssertEmptyWithError(result);
            Assert.IsType<AiConfigNotFoundException>(result.Error);
        }

        [Fact]
        public async Task MissingVariationIsNotFound()
        {
            var (_, result) = await InspectAsync(_ => { });

            AssertEmptyWithError(result);
            Assert.IsType<AiConfigNotFoundException>(result.Error);
        }

        [Fact]
        public async Task InvalidEnabledConfigReturnsTheParseError()
        {
            var (_, result) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] =
                Fixtures.EnabledConfig(edit: j => j.Remove("provider")));

            AssertEmptyWithError(result);
        }

        [Fact]
        public async Task ClientErrorIsReturnedNotThrown()
        {
            var failure = new InvalidOperationException("evaluation failed");
            var (_, result) = await InspectAsync(f => f.VariationException = failure);

            AssertEmptyWithError(result);
            Assert.Same(failure, result.Error);
        }

        [Fact]
        public async Task InitErrorIsReturnedNotThrown()
        {
            var result = await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);

            AssertEmptyWithError(result);
            Assert.IsAssignableFrom<LdAiException>(result.Error);
        }

        [Fact]
        public async Task RecoversOnceTheSdkKeyIsAvailable()
        {
            var first = await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);
            Assert.NotNull(first.Error);

            Environment.SetEnvironmentVariable("LD_SDK_KEY", "sdk-key");
            NextClient.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            var second = await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);

            Assert.True(second.Enabled);
            Assert.Null(second.Error);
            Assert.Equal("sdk-key", Assert.Single(Built).SdkKey);
        }

        [Fact]
        public async Task EmitsNoGenerationDurationOrTokenEvents()
        {
            var (fake, _) = await InspectAsync(f => f.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig());

            Assert.Empty(fake.EventsStartingWith("$ld:ai:generation:"));
            Assert.Empty(fake.EventsStartingWith("$ld:ai:duration:"));
            Assert.Empty(fake.EventsStartingWith("$ld:ai:tokens:"));
        }

        [Fact]
        public async Task CancelledTokenStillThrows()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext, cts.Token));
        }

        [Fact]
        public async Task ThroughTheClientInterfaceBehavesTheSame()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();

            var result = await LdAi.GetClient().InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);

            Assert.True(result.Enabled);
            Assert.NotNull(result.Config);
        }
    }
}
