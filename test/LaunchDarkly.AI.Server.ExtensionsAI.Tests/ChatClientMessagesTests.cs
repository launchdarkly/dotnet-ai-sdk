using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The one-call wrapper: it evaluates the given config key with a ChatClientHandler and passes
    /// the input, context and variables through.
    /// </summary>
    public class ChatClientMessagesTests : HandlerHarness
    {
        private async Task<FakeLdClient> SetUpAsync(string instructions = "Hello {{name}}")
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j => j["instructions"] = instructions);
            fake.Variations["other-config"] = Fixtures.EnabledConfig(edit: j => j["instructions"] = "other");
            return fake;
        }

        [Fact]
        public async Task EvaluatesTheGivenConfigKeyForTheContext()
        {
            var fake = await SetUpAsync();
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions { ChatClient = Chat });

            var evaluation = Assert.Single(fake.Evaluations);
            Assert.Equal(Fixtures.ConfigKey, evaluation.Key);
            Assert.Equal(Fixtures.UserContext, evaluation.Context);
        }

        [Fact]
        public async Task PassesTheInputAndReturnsTheResponse()
        {
            await SetUpAsync();
            Chat.ThenText("answer");
            var response = await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "the question", Fixtures.UserContext,
                new ChatClientMessagesOptions { ChatClient = Chat });

            Assert.Equal(("user", "the question"), Chat.LastRequest.Turns.Last());
            Assert.Equal("answer", response.Response);
            Assert.Equal(Fixtures.ConfigKey, response.TrackData.ConfigKey);
        }

        [Fact]
        public async Task ForwardsVariables()
        {
            await SetUpAsync();
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions
            {
                ChatClient = Chat,
                Variables = new Dictionary<string, object?> { ["name"] = "Ann" },
            });
            Assert.Equal("Hello Ann", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task WithoutVariablesOnlyLdContextIsAdded()
        {
            await SetUpAsync("Hi {{ldContext.key}} {{name}}");
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions { ChatClient = Chat });
            Assert.Equal("Hi user-123 {{name}}", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task ForwardsHistoryAndTools()
        {
            var fake = await SetUpAsync();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j => j["tools"] = new System.Text.Json.Nodes.JsonObject { ["a"] = Fixtures.ToolJson("a") });
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "now", Fixtures.UserContext, new ChatClientMessagesOptions
            {
                ChatClient = Chat,
                History = new[] { Hist("user", "before") },
                ToolHandlers = new Dictionary<string, ToolHandler> { ["a"] = Tool() },
            });

            Assert.Equal(new[] { ("user", "before"), ("user", "now") }, Chat.LastRequest.Turns);
            Assert.Equal(new[] { "a" }, Chat.LastRequest.ToolNames);
        }

        [Fact]
        public async Task ProvidersScopeTheHandler()
        {
            await SetUpAsync();
            await Assert.ThrowsAsync<NoHandlerException>(() =>
                ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext,
                    new ChatClientMessagesOptions { ChatClient = Chat, Providers = new[] { "Bedrock" } }));
            Assert.Empty(Chat.Requests);
        }

        [Fact]
        public async Task CaptureSettingReachesTheHandler()
        {
            await SetUpAsync();
            using var spans = new SpanRecorder();
            Chat.ThenText("secret");
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext,
                new ChatClientMessagesOptions { ChatClient = Chat, CaptureContent = false });

            Assert.Null(spans.Named("chat ").Single().Attr("gen_ai.completion.0.content"));
        }

        [Fact]
        public async Task WithoutOptionsTheCallIsRefused()
        {
            await SetUpAsync();
            var ex = await Assert.ThrowsAsync<System.ArgumentException>(() =>
                ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext));
            Assert.Equal("options", ex.ParamName);
        }

        [Fact]
        public async Task OptionsWithoutAClientOrFactoryAreRefused()
        {
            await SetUpAsync();
            var ex = await Assert.ThrowsAsync<System.ArgumentException>(() =>
                ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions()));
            Assert.Equal("options", ex.ParamName);
        }

        [Fact]
        public async Task ClientFactoryIsUsed()
        {
            await SetUpAsync();
            var calls = 0;
            await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions
            {
                ChatClientFactory = (_, _) =>
                {
                    calls++;
                    return new System.Threading.Tasks.ValueTask<Microsoft.Extensions.AI.IChatClient>(Chat);
                },
            });
            Assert.Equal(1, calls);
            Assert.Single(Chat.Requests);
        }
    }
}
