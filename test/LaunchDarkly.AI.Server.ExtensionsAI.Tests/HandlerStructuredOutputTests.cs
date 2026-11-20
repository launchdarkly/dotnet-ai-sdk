using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The config's output format is sent as the chat client's JSON-schema response format.
    /// </summary>
    public class HandlerStructuredOutputTests : HandlerHarness
    {
        private static readonly JsonElement _schema =
            Fixtures.Json("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}");

        [Fact]
        public async Task OutputFormatIsSentAsAJsonSchemaResponseFormat()
        {
            await RunAsync(Config() with { OutputFormat = _schema });

            var format = Assert.IsType<ChatResponseFormatJson>(Chat.LastRequest.Options!.ResponseFormat);
            Assert.NotNull(format.Schema);
            Assert.Equal(_schema.GetRawText(), format.Schema!.Value.GetRawText());
        }

        [Fact]
        public async Task NoOutputFormatSendsNoResponseFormat()
        {
            await RunAsync(Config());
            Assert.Null(Chat.LastRequest.Options?.ResponseFormat);
        }

        [Fact]
        public async Task StructuredResponseIsParsedThroughConfig()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j =>
                j["outputFormat"] = System.Text.Json.Nodes.JsonNode.Parse(_schema.GetRawText()));
            Chat.ThenText("{\"name\":\"Ann\"}");

            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() })
                .InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal("Ann", Assert.IsType<JsonElement>(response.Response).GetProperty("name").GetString());
        }

        [Fact]
        public async Task OutputFormatAndToolsTogetherDoNotThrow()
        {
            Chat.ThenToolCall("a").ThenText("{\"name\":\"Ann\"}");
            var result = await RunAsync(Config() with { OutputFormat = _schema, Tools = ConfigTools("a") },
                tools: new Dictionary<string, ToolHandler> { ["a"] = Tool() });

            Assert.NotNull(result.Output);
            Assert.All(Chat.Requests, r => Assert.IsType<ChatResponseFormatJson>(r.Options!.ResponseFormat));
        }

        [Fact]
        public async Task UsageIsReportedWithAnOutputFormat()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j =>
                j["outputFormat"] = System.Text.Json.Nodes.JsonNode.Parse(_schema.GetRawText()));
            Chat.ThenText("{\"name\":\"Ann\"}", FakeChatClient.Usage(12, 3));

            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() })
                .InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal(12, response.Usage.Input);
            Assert.Equal(3, response.Usage.Output);
        }

        [Fact]
        public async Task SchemaIsNotCopiedIntoTheSystemPrompt()
        {
            await RunAsync(Config("Plain instructions.") with { OutputFormat = _schema });
            Assert.Equal("Plain instructions.", Chat.LastRequest.SystemText);
            Assert.DoesNotContain(Chat.LastRequest.Messages, m => m.Text.Contains("\"required\""));
        }
    }
}
