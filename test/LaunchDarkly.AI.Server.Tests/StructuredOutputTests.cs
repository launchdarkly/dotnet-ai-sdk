using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using LaunchDarkly.Logging;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Best-effort parsing of the response when the config sets an output format, including JSON
    /// wrapped in Markdown fences.
    /// </summary>
    public class StructuredOutputTests : InvokeHarness
    {
        private void WithOutputFormat() =>
            SetVariation(j => j["outputFormat"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });

        private async Task<object?> ResponseFor(object? output, bool outputFormat = true)
        {
            if (outputFormat)
            {
                WithOutputFormat();
            }
            else
            {
                SetVariation();
            }

            return (await InvokeAsync(Returning(output))).Response;
        }

        private static JsonElement AsJson(object? response) => Assert.IsType<JsonElement>(response);

        [Fact]
        public async Task PlainJsonIsParsed()
        {
            var json = AsJson(await ResponseFor("{\"a\":1}"));
            Assert.Equal(1, json.GetProperty("a").GetInt32());
        }

        [Fact]
        public async Task JsonFencedJsonIsParsed() =>
            Assert.Equal(1, AsJson(await ResponseFor("```json\n{\"a\":1}\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task BareFencedJsonIsParsed() =>
            Assert.Equal(1, AsJson(await ResponseFor("```\n{\"a\":1}\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task BlankLinesInsideFencesAreTolerated() =>
            Assert.Equal(1, AsJson(await ResponseFor("```json\n\n  {\"a\":1}  \n\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task WindowsLineEndingsWithJsonFence() =>
            Assert.Equal(1, AsJson(await ResponseFor("```json\r\n{\"a\":1}\r\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task WindowsLineEndingsWithBareFence() =>
            Assert.Equal(1, AsJson(await ResponseFor("```\r\n{\"a\":1}\r\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task LeadingWhitespaceBeforeTheFenceIsTolerated() =>
            Assert.Equal(1, AsJson(await ResponseFor("  ```json\n{\"a\":1}\n```")).GetProperty("a").GetInt32());

        [Fact]
        public async Task NestedValuesRoundTrip()
        {
            var json = AsJson(await ResponseFor("{\"a\":{\"b\":[1,{\"c\":true}]},\"d\":null}"));
            Assert.True(json.GetProperty("a").GetProperty("b")[1].GetProperty("c").GetBoolean());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("d").ValueKind);
        }

        [Fact]
        public async Task UnparseableTextIsReturnedRaw()
        {
            Assert.Equal("not json at all", await ResponseFor("not json at all"));
        }

        [Fact]
        public async Task UnparseableTextLogsNoError()
        {
            await ResponseFor("```json\n{broken\n```");
            Assert.DoesNotContain(Logs.GetMessages(), m => m.Level == LogLevel.Error);
        }

        [Fact]
        public async Task ObjectOutputIsReturnedAsIs()
        {
            var parsed = Fixtures.Json("{\"a\":1}");
            var response = await ResponseFor(parsed);
            Assert.Equal(parsed.GetRawText(), AsJson(response).GetRawText());
        }

        [Fact]
        public async Task NonJsonObjectOutputIsReturnedAsIs()
        {
            var output = new { a = 1 };
            Assert.Same(output, await ResponseFor(output));
        }

        [Fact]
        public async Task NoParsingWithoutAnOutputFormat()
        {
            Assert.Equal("{\"a\":1}", await ResponseFor("{\"a\":1}", outputFormat: false));
        }

        [Fact]
        public async Task HandlerReceivesTheOutputFormat()
        {
            await ResponseFor("{}");
            var request = Assert.Single(Calls);
            // The variation passes through LdValue, whose object keys have no order, so compare
            // the key set and the values, not the order.
            var format = request.Config.OutputFormat!.Value;
            Assert.Equal(new[] { "properties", "type" }, format.EnumerateObject().Select(p => p.Name).OrderBy(n => n, System.StringComparer.Ordinal));
            Assert.Equal("object", format.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, format.GetProperty("properties").ValueKind);
            Assert.Empty(format.GetProperty("properties").EnumerateObject());
        }
    }
}
