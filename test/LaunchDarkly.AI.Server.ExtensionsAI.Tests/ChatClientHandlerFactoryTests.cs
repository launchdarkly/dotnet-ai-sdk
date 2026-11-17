using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The handler factory: metadata, content capture, provider scoping, the retry option and the
    /// two ways of supplying the chat client.
    /// </summary>
    public class ChatClientHandlerFactoryTests : HandlerHarness
    {
        [Fact]
        public void ServesAnyProviderInMessagesMode()
        {
            var handler = ChatClientHandler.Create(Chat);
            Assert.Equal("*", handler.ProvidesFor.Provider);
            Assert.Equal("messages", handler.ProvidesFor.Mode);
            Assert.Null(handler.Providers);
        }

        [Fact]
        public void EachCallReturnsAnIndependentHandler()
        {
            var a = ChatClientHandler.Create(Chat);
            var b = ChatClientHandler.Create(Chat);
            Assert.NotSame(a, b);
            Assert.Equal(a.ProvidesFor, b.ProvidesFor);
        }

        [Fact]
        public void OmittedCaptureIsUnset()
        {
            Assert.Null(ChatClientHandler.Create(Chat).CaptureContent);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExplicitCaptureIsAttached(bool value)
        {
            Assert.Equal(value, ChatClientHandler.Create(Chat, new ChatClientHandlerOptions { CaptureContent = value }).CaptureContent);
        }

        [Fact]
        public void ProvidersScopeTheWildcard()
        {
            var handler = ChatClientHandler.Create(Chat, new ChatClientHandlerOptions { Providers = new[] { "Bedrock", "Anthropic", "Bedrock" } });
            Assert.Equal("*", handler.ProvidesFor.Provider);
            Assert.Equal(new[] { "Bedrock", "Anthropic" }, handler.Providers);
        }

        public static TheoryData<string[]> BadProviderLists() => new TheoryData<string[]>
        {
            Array.Empty<string>(),
            new[] { "" },
            new[] { " Bedrock" },
        };

        [Theory]
        [MemberData(nameof(BadProviderLists))]
        public void EmptyBlankOrPaddedProvidersAreRejected(string[] providers)
        {
            var ex = Assert.Throws<ArgumentException>(() => ChatClientHandler.Create(Chat, new ChatClientHandlerOptions { Providers = providers }));
            Assert.Equal("providers", ex.ParamName);
        }

        [Fact]
        public async Task PrebuiltClientIsUsedAsIs()
        {
            await RunAsync(Config());
            Assert.Single(Chat.Requests);
        }

        [Fact]
        public async Task FactoryIsCalledOncePerRequestWithTheEvaluatedConfig()
        {
            var seen = new List<AiConfig>();
            var handler = ChatClientHandler.Create((config, _) =>
            {
                seen.Add(config);
                return new ValueTask<IChatClient>(Chat);
            });

            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j =>
                ((JsonObject)j["model"]!)["parameters"] = new JsonObject { ["temperature"] = 0.25 });
            var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler });
            await config.InvokeAsync("one", Fixtures.UserContext);
            await config.InvokeAsync("two", Fixtures.UserContext);

            Assert.Equal(2, seen.Count);
            Assert.Equal("test-model", seen[0].Model.Name);
            Assert.Equal(0.25, seen[0].Model.Parameters!["temperature"].GetDouble());
            Assert.Equal(2, Chat.Requests.Count);
        }

        [Fact]
        public async Task FactoryIsNotCalledWhenBuildingTheHandler()
        {
            var calls = 0;
            var handler = ChatClientHandler.Create((_, _) =>
            {
                calls++;
                return new ValueTask<IChatClient>(Chat);
            });
            Assert.Equal(0, calls);
            await RunAsync(Config(), handler: handler);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task FactoryReceivesTheCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            CancellationToken seen = default;
            var handler = ChatClientHandler.Create((_, ct) =>
            {
                seen = ct;
                return new ValueTask<IChatClient>(Chat);
            });
            await handler.InvokeAsync(new HandlerRequest(Config()) { UserInput = "hi" }, cts.Token);
            Assert.Equal(cts.Token, seen);
        }

        [Fact]
        public async Task HandlerDoesNotDisposeTheCallersClient()
        {
            await RunAsync(Config());
            Assert.Equal(0, Chat.Disposed);
        }

        [Fact]
        public async Task PackageRegistersItsIdentity()
        {
            ChatClientHandler.Create(Chat);
            var fake = await InitWithFakeAsync();

            var info = fake.Events("$ld:ai:sdk:info").Where(e => e.DataString("aiSdkName") == PackageInfo.Name).ToList();
            Assert.Equal(PackageInfo.Version, Assert.Single(info).DataString("aiSdkVersion"));
            Assert.Equal("c#", info[0].DataString("aiSdkLanguage"));
        }

        [Fact]
        public async Task ReturnsTheResponseTextAndUsage()
        {
            Chat.ThenText("the answer", FakeChatClient.Usage(10, 5));
            var result = await RunAsync(Config());

            Assert.Equal("the answer", result.Output);
            Assert.NotNull(result.Usage);
        }
    }
}
