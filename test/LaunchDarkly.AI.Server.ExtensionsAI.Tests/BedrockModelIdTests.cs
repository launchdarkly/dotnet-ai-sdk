using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Under a Bedrock provider the handler sends the model id with the config's cross-region
    /// inference-profile prefix (<c>model.region</c>), by the shared rule, and reports the id it
    /// sent on its spans.
    /// </summary>
    public sealed class BedrockModelIdTests : HandlerHarness, IDisposable
    {
        private const string Bare = "anthropic.claude-sonnet-4-5";

        private readonly SpanRecorder _spans = new SpanRecorder();

        public void Dispose() => _spans.Dispose();

        private async Task<string?> SentModelId(string provider, string? region, string name)
        {
            var config = Fixtures.Config(provider) with
            {
                Instructions = "x",
                Model = new ModelConfig(name) { Region = region },
            };
            await RunAsync(config);
            return Chat.LastRequest.Options!.ModelId;
        }

        [Theory]
        [InlineData("us", "us." + Bare)]
        [InlineData("global", "global." + Bare)]
        public async Task RegionIsPrependedToABareId(string region, string expected) =>
            Assert.Equal(expected, await SentModelId("Bedrock", region, Bare));

        [Fact]
        public async Task IdAlreadyCarryingTheRegionIsUnchanged() =>
            Assert.Equal("us." + Bare, await SentModelId("Bedrock", "us", "us." + Bare));

        [Theory]
        [InlineData("global.")]
        [InlineData("us-gov.")]
        [InlineData("eu.")]
        [InlineData("apac.")]
        [InlineData("au.")]
        [InlineData("jp.")]
        [InlineData("ca.")]
        [InlineData("in.")]
        public async Task IdCarryingAnotherKnownPrefixIsUnchanged(string prefix) =>
            Assert.Equal(prefix + Bare, await SentModelId("Bedrock", "us", prefix + Bare));

        [Fact]
        public async Task IdCarryingADifferentRegionIsUnchanged() =>
            Assert.Equal("us." + Bare, await SentModelId("Bedrock", "eu", "us." + Bare));

        [Fact]
        public async Task ArnIsUnchanged()
        {
            const string arn = "arn:aws:bedrock:us-east-1:123456789012:application-inference-profile/abc123";
            Assert.Equal(arn, await SentModelId("Bedrock", "us", arn));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task NoRegionMeansNoPrefix(string? region) =>
            Assert.Equal(Bare, await SentModelId("Bedrock", region, Bare));

        [Theory]
        [InlineData(" us ")]
        [InlineData("us.")]
        public async Task RegionIsTrimmedAndLosesTrailingDots(string region) =>
            Assert.Equal("us." + Bare, await SentModelId("Bedrock", region, Bare));

        [Theory]
        [InlineData("bedrock")]
        [InlineData("BEDROCK")]
        public async Task ProviderIsComparedCaseInsensitively(string provider) =>
            Assert.Equal("us." + Bare, await SentModelId(provider, "us", Bare));

        [Fact]
        public async Task NonBedrockProviderIgnoresTheRegion() =>
            Assert.Equal("gpt-4o", await SentModelId("OpenAI", "us", "gpt-4o"));

        [Fact]
        public async Task SpansReportTheIdSent()
        {
            Chat.ThenText("ok");
            await SentModelId("Bedrock", "us", Bare);

            var chat = Assert.Single(_spans.Named("chat "));
            Assert.Equal("chat us." + Bare, chat.DisplayName);
            Assert.Equal("us." + Bare, chat.AttrString("gen_ai.request.model"));
            Assert.Equal("us." + Bare, chat.AttrString("gen_ai.response.model"));
        }

        [Fact]
        public async Task RegionFromTheVariationIsApplied()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig("Bedrock", edit: j =>
                j["model"] = new JsonObject { ["name"] = Bare, ["region"] = "eu" });

            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal("eu." + Bare, Chat.LastRequest.Options!.ModelId);
        }

        [Fact]
        public async Task NonStringRegionInTheVariationIsAbsent()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig("Bedrock", edit: j =>
                j["model"] = new JsonObject { ["name"] = Bare, ["region"] = 5 });

            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal(Bare, Chat.LastRequest.Options!.ModelId);
        }

        [Fact]
        public async Task EveryToolLoopTurnSendsThePrefixedId()
        {
            Chat.ThenToolCall("a").ThenText("done");
            var config = Fixtures.Config("Bedrock") with
            {
                Instructions = "x",
                Model = new ModelConfig(Bare) { Region = "us" },
                Tools = ConfigTools("a"),
            };
            await RunAsync(config, tools: new System.Collections.Generic.Dictionary<string, ToolHandler> { ["a"] = Tool() });

            Assert.Equal(2, Chat.Requests.Count);
            Assert.All(Chat.Requests, r => Assert.Equal("us." + Bare, r.Options!.ModelId));
            Assert.All(_spans.Named("chat "), s => Assert.Equal("chat us." + Bare, s.DisplayName));
            Assert.Equal(2, _spans.Named("chat ").Count());
        }
    }
}
