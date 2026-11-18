using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Only allowlisted <c>model.parameters</c> reach <see cref="ChatOptions"/>; settings the handler
    /// owns win, and nothing else is forwarded.
    /// </summary>
    public class ParameterForwardingTests : HandlerHarness
    {
        private async Task<ChatOptions> OptionsFor(string parametersJson, AiConfig? config = null, IReadOnlyDictionary<string, ToolHandler>? tools = null)
        {
            var parameters = Fixtures.Json(parametersJson).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var baseConfig = config ?? Config("The handler's instructions.");
            await RunAsync(baseConfig with { Model = baseConfig.Model with { Parameters = parameters } }, tools: tools);
            return Chat.LastRequest.Options!;
        }

        [Fact]
        public async Task EveryAllowlistedKeyIsForwarded()
        {
            var options = await OptionsFor(
                "{\"temperature\":0.5,\"max_tokens\":100,\"top_p\":0.9,\"top_k\":40,\"frequency_penalty\":0.1," +
                "\"presence_penalty\":0.2,\"seed\":7,\"stop_sequences\":[\"END\"]}");

            Assert.Equal(0.5f, options.Temperature);
            Assert.Equal(100, options.MaxOutputTokens);
            Assert.Equal(0.9f, options.TopP);
            Assert.Equal(40, options.TopK);
            Assert.Equal(0.1f, options.FrequencyPenalty);
            Assert.Equal(0.2f, options.PresencePenalty);
            Assert.Equal(7L, options.Seed);
            Assert.Equal(new[] { "END" }, options.StopSequences);
        }

        [Fact]
        public async Task StopIsRenamedToStopSequences()
        {
            Assert.Equal(new[] { "X" }, (await OptionsFor("{\"stop\":[\"X\"]}")).StopSequences);
        }

        [Fact]
        public async Task StopSequencesWinOverStop()
        {
            Assert.Equal(new[] { "A" }, (await OptionsFor("{\"stop\":[\"B\"],\"stop_sequences\":[\"A\"]}")).StopSequences);
        }

        [Fact]
        public async Task ASingleStopStringIsAccepted()
        {
            Assert.Equal(new[] { "END" }, (await OptionsFor("{\"stop_sequences\":\"END\"}")).StopSequences);
        }

        [Fact]
        public async Task CamelCaseSpellingsAreNotRead()
        {
            var options = await OptionsFor("{\"maxTokens\":5,\"topP\":0.5,\"topK\":3,\"stopSequences\":[\"X\"],\"frequencyPenalty\":1,\"presencePenalty\":1}");
            Assert.Null(options.MaxOutputTokens);
            Assert.Null(options.TopP);
            Assert.Null(options.TopK);
            Assert.Null(options.StopSequences);
            Assert.Null(options.FrequencyPenalty);
            Assert.Null(options.PresencePenalty);
        }

        [Fact]
        public async Task SnakeCaseMaxTokensIsRead()
        {
            Assert.Equal(5, (await OptionsFor("{\"max_tokens\":5}")).MaxOutputTokens);
        }

        [Theory]
        [InlineData("{\"max_tokens\":\"100\"}")]
        [InlineData("{\"max_tokens\":1.5}")]
        [InlineData("{\"seed\":\"7\"}")]
        [InlineData("{\"top_k\":2.5}")]
        [InlineData("{\"temperature\":\"hot\"}")]
        [InlineData("{\"stop_sequences\":[1,2]}")]
        [InlineData("{\"stop_sequences\":{\"a\":1}}")]
        public async Task WrongTypedValuesAreDropped(string parametersJson)
        {
            var options = await OptionsFor(parametersJson);
            Assert.Null(options.MaxOutputTokens);
            Assert.Null(options.Seed);
            Assert.Null(options.TopK);
            Assert.Null(options.Temperature);
            Assert.Null(options.StopSequences);
        }

        [Theory]
        [InlineData("{\"temperature\":1e400}")]
        [InlineData("{\"temperature\":-1e400}")]
        [InlineData("{\"temperature\":1e100}")]
        public async Task OutOfRangeNumbersAreDropped(string parametersJson)
        {
            var options = await OptionsFor(parametersJson);
            Assert.Null(options.Temperature);
        }

        [Fact]
        public async Task UnknownKeysAreDropped()
        {
            var options = await OptionsFor("{\"not_a_setting\":1,\"reasoning\":{\"effort\":\"low\"},\"parallel_tool_calls\":false,\"max_completion_tokens\":9}");

            Assert.Null(options.AdditionalProperties);
            Assert.Null(options.Reasoning);
            Assert.Null(options.AllowMultipleToolCalls);
            Assert.Null(options.MaxOutputTokens);
        }

        [Fact]
        public async Task NeverForwardedKeysAreAllDropped()
        {
            var options = await OptionsFor(
                "{\"api_key\":\"sk-x\",\"apiKey\":\"sk-x\",\"base_url\":\"https://evil\",\"timeout\":1,\"max_retries\":50," +
                "\"headers\":{\"x\":\"y\"},\"extra_headers\":{\"x\":\"y\"},\"extra_body\":{\"x\":1},\"model_kwargs\":{\"x\":1}," +
                "\"mcp_servers\":[],\"inference_geo\":\"eu\",\"container\":\"c\",\"user\":\"u\",\"metadata\":{\"a\":\"b\"}," +
                "\"conversation\":\"c1\",\"previous_response_id\":\"r1\",\"store\":true,\"instructions\":\"evil\"," +
                "\"additional_properties\":{\"x\":1},\"AdditionalProperties\":{\"x\":1},\"conversation_id\":\"c2\"}");

            Assert.Null(options.AdditionalProperties);
            Assert.Null(options.RawRepresentationFactory);
            Assert.Null(options.ConversationId);
            Assert.Null(options.ContinuationToken);
            Assert.Null(options.AllowBackgroundResponses);
            Assert.DoesNotContain("evil", Chat.LastRequest.SystemText, StringComparison.Ordinal);
            Assert.DoesNotContain(Chat.LastRequest.Messages, m => m.Text.Contains("evil"));
        }

        [Fact]
        public async Task HandlerOwnedSettingsWin()
        {
            var options = await OptionsFor(
                "{\"model\":\"other-model\",\"model_id\":\"other-model\",\"tools\":[],\"system\":\"evil\",\"instructions\":\"evil\",\"response_format\":\"text\"}",
                Config("The handler's instructions.") with { Tools = ConfigTools("a") },
                new Dictionary<string, ToolHandler> { ["a"] = Tool() });

            Assert.Equal("test-model", options.ModelId);
            Assert.Equal(new[] { "a" }, Chat.LastRequest.ToolNames);
            Assert.Equal("The handler's instructions.", Chat.LastRequest.SystemText);
            Assert.Null(options.ResponseFormat);
        }

        [Fact]
        public async Task ModelCustomIsNeverRead()
        {
            var config = Config() with
            {
                Model = new ModelConfig("test-model")
                {
                    Custom = new Dictionary<string, JsonElement> { ["temperature"] = Fixtures.Json("0.9") },
                },
            };
            await RunAsync(config);
            Assert.Null(Chat.LastRequest.Options!.Temperature);
        }

        [Fact]
        public async Task SameParametersOnEveryToolLoopTurn()
        {
            Chat.ThenToolCall("a").ThenText("done");
            await OptionsFor("{\"temperature\":0.5}", Config() with { Tools = ConfigTools("a") }, new Dictionary<string, ToolHandler> { ["a"] = Tool() });
            Assert.All(Chat.Requests, r => Assert.Equal(0.5f, r.Options!.Temperature));
        }

        /// <summary>
        /// Every public ChatOptions property is classified. A property added in a later
        /// Microsoft.Extensions.AI release fails this test until someone decides where it goes.
        /// </summary>
        [Fact]
        public void EveryChatOptionsPropertyIsClassified()
        {
            var forwarded = new[] { "Temperature", "MaxOutputTokens", "TopP", "TopK", "FrequencyPenalty", "PresencePenalty", "Seed", "StopSequences" };
            var owned = new[] { "ModelId", "Instructions", "Tools", "ToolMode", "ResponseFormat" };
            var excluded = new[]
            {
                "AdditionalProperties", "RawRepresentationFactory", "ConversationId", "ContinuationToken",
                "AllowBackgroundResponses", "Reasoning", "AllowMultipleToolCalls",
            };

            var properties = typeof(ChatOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
            var classified = forwarded.Concat(owned).Concat(excluded).ToList();

            Assert.Empty(properties.Except(classified));
            Assert.Empty(classified.Except(properties));
        }

        [Fact]
        public async Task OnlyForwardedPropertiesAreSetFromParameters()
        {
            var options = await OptionsFor("{\"temperature\":0.5}");
            Assert.Null(options.Reasoning);
            Assert.Null(options.AllowMultipleToolCalls);
            Assert.Null(options.AdditionalProperties);
            Assert.Null(options.MaxOutputTokens);
        }
    }
}
