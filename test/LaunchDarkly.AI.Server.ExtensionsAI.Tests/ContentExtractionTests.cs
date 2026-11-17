using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Only visible text reaches the output: reasoning is dropped, bare text parts are kept, and
    /// a response with no text is empty rather than an error.
    /// </summary>
    public class ContentExtractionTests : HandlerHarness
    {
        [Fact]
        public async Task ReasoningNeverReachesTheOutput()
        {
            Chat.ThenContents(new TextReasoningContent("secret thoughts"), new TextContent("visible answer"));
            var result = await RunAsync(Config());

            Assert.Equal("visible answer", result.Output);
        }

        [Fact]
        public async Task ReasoningNeverReachesTheCallersResponse()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            Chat.ThenContents(new TextReasoningContent("secret thoughts"), new TextContent("visible answer"));

            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal("visible answer", response.Response);
        }

        [Fact]
        public async Task TextPartsAreConcatenated()
        {
            Chat.ThenContents(new TextContent("Hello "), new TextContent("world"));
            Assert.Equal("Hello world", (await RunAsync(Config())).Output);
        }

        [Fact]
        public async Task ResponseWithoutTextIsEmpty()
        {
            Chat.ThenContents(new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream"));
            Assert.Equal("", (await RunAsync(Config())).Output);
        }

        [Fact]
        public async Task ResponseWithNoContentIsEmpty()
        {
            Chat.Then(_ => new ChatResponse(new List<ChatMessage>()));
            Assert.Equal("", (await RunAsync(Config())).Output);
        }
    }
}
