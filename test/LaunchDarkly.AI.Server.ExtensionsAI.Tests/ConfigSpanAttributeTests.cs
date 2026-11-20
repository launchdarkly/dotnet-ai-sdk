using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The launchdarkly.* attributes and the feature_flag event that tie the root span to the
    /// AI Config, written from the SDK's <c>__ld</c> tracking entry.
    /// </summary>
    public sealed class ConfigSpanAttributeTests : HandlerHarness, IDisposable
    {
        private readonly SpanRecorder _spans = new SpanRecorder();

        public void Dispose() => _spans.Dispose();

        private Task RunAsync(TrackData ld, object? variation = null, bool? capture = null)
        {
            var vars = new Dictionary<string, object?> { ["__ld"] = ld };
            if (variation != null)
            {
                vars["__ldValue"] = variation;
            }

            var handler = capture == null ? Handler() : Handler(new ChatClientHandlerOptions { CaptureContent = capture });
            return RunAsync(Config(), variables: vars, handler: handler);
        }

        private static JsonElement Variation => Fixtures.Json("{\"model\":{\"name\":\"test-model\"},\"instructions\":\"x\"}");

        [Fact]
        public async Task RootCarriesTheConfigAttributes()
        {
            await RunAsync(Fixtures.LdFixture());

            var root = _spans.Root;
            Assert.Equal("gen_ai", root.AttrString("launchdarkly.operation.type"));
            Assert.Equal("test-config", root.AttrString("launchdarkly.config.key"));
            Assert.Equal("variation-a", root.AttrString("launchdarkly.variation.key"));
            Assert.Equal("run-123", root.AttrString("launchdarkly.run.id"));
        }

        [Fact]
        public async Task OnlyTheRootCarriesTheConfigAttributes()
        {
            await RunAsync(Fixtures.LdFixture());
            Assert.All(_spans.Named("chat "), s =>
            {
                Assert.Null(s.Attr("launchdarkly.config.key"));
                Assert.Null(s.Event("feature_flag"));
            });
        }

        [Fact]
        public async Task GraphKeyIsSetOnlyWhenPresent()
        {
            await RunAsync(Fixtures.LdFixture(graphKey: "graph-1"));
            Assert.Equal("graph-1", _spans.Root.AttrString("launchdarkly.graph.key"));
        }

        [Fact]
        public async Task GraphKeyIsAbsentWithoutOne()
        {
            await RunAsync(Fixtures.LdFixture());
            Assert.Null(_spans.Root.Attr("launchdarkly.graph.key"));
        }

        [Fact]
        public async Task FeatureFlagEventCarriesTheVariant()
        {
            await RunAsync(Fixtures.LdFixture());
            var e = _spans.Root.Event("feature_flag");
            Assert.NotNull(e);
            Assert.Equal("variation-a", e!.Value.EventAttr("feature_flag.result.variant")?.ToString());
        }

        [Fact]
        public async Task EmptyVariationKeyWritesNoVariant()
        {
            await RunAsync(Fixtures.LdFixture(variationKey: ""));
            var e = _spans.Root.Event("feature_flag");
            Assert.False(e.HasValue && e.Value.HasEventAttr("feature_flag.result.variant"));
        }

        [Fact]
        public async Task ValueIsTheVariationJsonWhenCaptureIsOn()
        {
            await RunAsync(Fixtures.LdFixture(), Variation, capture: true);

            var value = _spans.Root.Event("feature_flag")!.Value.EventAttr("feature_flag.result.value")?.ToString();
            using var parsed = JsonDocument.Parse(value!);
            Assert.Equal("test-model", parsed.RootElement.GetProperty("model").GetProperty("name").GetString());
        }

        [Fact]
        public async Task ValueIsTheQuotedVariationKeyWhenCaptureIsOff()
        {
            await RunAsync(Fixtures.LdFixture(), Variation, capture: false);
            Assert.Equal("\"variation-a\"", _spans.Root.Event("feature_flag")!.Value.EventAttr("feature_flag.result.value")?.ToString());
        }

        [Fact]
        public async Task ValueIsTheQuotedVariationKeyWithoutAVariation()
        {
            await RunAsync(Fixtures.LdFixture());
            Assert.Equal("\"variation-a\"", _spans.Root.Event("feature_flag")!.Value.EventAttr("feature_flag.result.value")?.ToString());
        }

        [Fact]
        public async Task NoValueWithoutAVariationOrAVariationKey()
        {
            await RunAsync(Fixtures.LdFixture(variationKey: ""));
            var e = _spans.Root.Event("feature_flag");
            Assert.False(e.HasValue && e.Value.HasEventAttr("feature_flag.result.value"));
        }

        [Fact]
        public async Task ExplicitTrueOnTheHandlerBeatsAConfiguredFalse()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            await RunAsync(Fixtures.LdFixture(), Variation, capture: true);
            var value = _spans.Root.Event("feature_flag")!.Value.EventAttr("feature_flag.result.value")?.ToString();
            Assert.StartsWith("{", value, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MissingModelStampsDoNotFailTheRun()
        {
            await RunAsync(Fixtures.LdFixture() with { ModelKey = null, ModelVersion = null });
            Assert.Equal("test-config", _spans.Root.AttrString("launchdarkly.config.key"));
            Assert.DoesNotContain(_spans.Root.TagObjects, t => t.Key.IndexOf("model_key", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task WithoutTheTrackingEntryTheRunStillSucceeds()
        {
            await RunAsync(Config());
            Assert.Null(_spans.Root.Attr("launchdarkly.config.key"));
            Assert.Empty(_spans.Open);
        }

        [Fact]
        public async Task InvokingThroughConfigWritesTheSameAttributes()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() })
                .InvokeAsync("hi", Fixtures.UserContext);

            var root = _spans.Root;
            Assert.Equal(Fixtures.ConfigKey, root.AttrString("launchdarkly.config.key"));
            Assert.Equal("v1", root.AttrString("launchdarkly.variation.key"));
            Assert.Equal(response.TrackData.RunId, root.AttrString("launchdarkly.run.id"));
            Assert.Single(_spans.Finished, s => s.DisplayName == "invoke_agent");
        }
    }
}
