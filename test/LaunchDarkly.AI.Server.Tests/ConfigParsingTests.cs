using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Validation of an AI Config variation. Observed through <see cref="LdAi.InspectConfigAsync"/>:
    /// a valid config comes back parsed, an invalid one comes back with an error.
    /// </summary>
    public class ConfigParsingTests : LdAiTestBase
    {
        private async Task<InspectResult> ParseAsync(Action<JsonObject> edit)
        {
            var fake = UseFakeClient();
            var json = Fixtures.EnabledConfigJson();
            edit(json);
            fake.Variations[Fixtures.ConfigKey] = Fixtures.ToLdValue(json);
            return await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);
        }

        private static void AssertValid(InspectResult result)
        {
            Assert.Null(result.Error);
            Assert.True(result.Enabled);
            Assert.NotNull(result.Config);
        }

        private static void AssertInvalid(InspectResult result)
        {
            Assert.Null(result.Config);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task ValidWithInstructions()
        {
            var result = await ParseAsync(_ => { });
            AssertValid(result);
            Assert.Equal("Be helpful.", result.Config!.Instructions);
            Assert.Equal("test-model", result.Config.Model.Name);
            Assert.Equal("OpenAI", result.Config.Provider.Name);
            Assert.Equal(Fixtures.ConfigKey, result.Config.Key);
        }

        [Fact]
        public async Task ValidWithMessages()
        {
            var result = await ParseAsync(j =>
            {
                j.Remove("instructions");
                j["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = "sys" },
                    new JsonObject { ["role"] = "user", ["content"] = "hello" });
            });
            AssertValid(result);
            Assert.Equal(new[] { "system", "user" }, result.Config!.Messages!.Select(m => m.Role));
            Assert.Equal("hello", result.Config.Messages![1].Content);
        }

        [Fact]
        public async Task FailsWithNeitherInstructionsNorMessages()
        {
            AssertInvalid(await ParseAsync(j => j.Remove("instructions")));
        }

        [Fact]
        public async Task FailsWithEmptyMessagesAndNoInstructions()
        {
            AssertInvalid(await ParseAsync(j =>
            {
                j.Remove("instructions");
                j["messages"] = new JsonArray();
            }));
        }

        [Fact]
        public async Task NonEmptyClassifiersAreEnough()
        {
            AssertValid(await ParseAsync(j =>
            {
                j.Remove("instructions");
                j["messages"] = new JsonArray();
                j["classifiers"] = new JsonArray(new JsonObject { ["key"] = "toxicity" });
            }));
        }

        [Fact]
        public async Task EmptyClassifiersAreNotEnough()
        {
            AssertInvalid(await ParseAsync(j =>
            {
                j.Remove("instructions");
                j["messages"] = new JsonArray();
                j["classifiers"] = new JsonArray();
            }));
        }

        [Fact]
        public async Task MessageWithAnUnknownRoleFails()
        {
            AssertInvalid(await ParseAsync(j =>
            {
                j.Remove("instructions");
                j["messages"] = new JsonArray(new JsonObject { ["role"] = "admin", ["content"] = "x" });
            }));
        }

        [Fact]
        public async Task MissingModelNameFails()
        {
            AssertInvalid(await ParseAsync(j => j["model"] = new JsonObject()));
        }

        [Fact]
        public async Task MissingProviderFails()
        {
            AssertInvalid(await ParseAsync(j => j.Remove("provider")));
        }

        [Fact]
        public async Task OptionalFieldsAreAccepted()
        {
            var result = await ParseAsync(j =>
            {
                j["tools"] = new JsonObject { ["web-search"] = Fixtures.ToolJson("web-search", "searches the web") };
                j["judgeConfiguration"] = new JsonObject { ["judges"] = new JsonArray() };
                j["evaluationMetricKey"] = "quality";
            });
            AssertValid(result);
            var tool = result.Config!.Tools!["web-search"];
            Assert.Equal("web-search", tool.Name);
            Assert.Equal("function", tool.Type);
            Assert.Equal("searches the web", tool.Description);
            Assert.Equal("object", tool.Parameters.GetProperty("type").GetString());
            Assert.Equal("quality", result.Config.EvaluationMetricKey);
            Assert.NotNull(result.Config.JudgeConfiguration);
        }

        [Fact]
        public async Task ToolWithTheWrongTypeFails()
        {
            AssertInvalid(await ParseAsync(j =>
            {
                var tool = Fixtures.ToolJson("web-search");
                tool["type"] = "class";
                j["tools"] = new JsonObject { ["web-search"] = tool };
            }));
        }

        [Fact]
        public async Task OutputFormatIsAccepted()
        {
            var result = await ParseAsync(j => j["outputFormat"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });
            AssertValid(result);
            Assert.Equal("object", result.Config!.OutputFormat!.Value.GetProperty("type").GetString());
        }

        [Fact]
        public async Task ModelParametersAreKeptAsJson()
        {
            var result = await ParseAsync(j => ((JsonObject)j["model"]!)["parameters"] = new JsonObject { ["temperature"] = 0.5 });
            AssertValid(result);
            Assert.Equal(0.5, result.Config!.Model.Parameters!["temperature"].GetDouble());
        }
    }
}
