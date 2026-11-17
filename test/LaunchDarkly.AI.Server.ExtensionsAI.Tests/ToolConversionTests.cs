using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Which tools are offered to the model, and in what shape.
    /// </summary>
    public class ToolConversionTests : HandlerHarness
    {
        private Task OfferAsync(IReadOnlyDictionary<string, ToolDefinition>? configTools, IReadOnlyDictionary<string, ToolHandler> handlers) =>
            RunAsync(Config() with { Tools = configTools }, tools: handlers);

        [Fact]
        public async Task NameDescriptionAndSchemaAreForwarded()
        {
            await OfferAsync(ConfigTools("lookup"), new Dictionary<string, ToolHandler> { ["lookup"] = Tool() });

            var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(Chat.LastRequest.Tools));
            Assert.Equal("lookup", tool.Name);
            Assert.Equal("does lookup", tool.Description);
            Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
            Assert.Equal("string", tool.JsonSchema.GetProperty("properties").GetProperty("q").GetProperty("type").GetString());
        }

        [Fact]
        public async Task ThreeToolsAreAllForwarded()
        {
            await OfferAsync(ConfigTools("a", "b", "c"), new Dictionary<string, ToolHandler> { ["a"] = Tool(), ["b"] = Tool(), ["c"] = Tool() });
            Assert.Equal(new[] { "a", "b", "c" }, Chat.LastRequest.ToolNames.OrderBy(n => n, System.StringComparer.Ordinal));
        }

        [Fact]
        public async Task OnlyToolsWithAHandlerAreOffered()
        {
            await OfferAsync(ConfigTools("a", "b", "c"), new Dictionary<string, ToolHandler> { ["a"] = Tool(), ["b"] = Tool() });
            Assert.Equal(new[] { "a", "b" }, Chat.LastRequest.ToolNames.OrderBy(n => n, System.StringComparer.Ordinal));
        }

        [Fact]
        public async Task ToolWithoutAHandlerIsSilentlyExcluded()
        {
            await OfferAsync(ConfigTools("a"), new Dictionary<string, ToolHandler>());
            Assert.Empty(Chat.LastRequest.Tools);
        }

        [Fact]
        public async Task HandlersTheConfigDoesNotAttachAreNeverOffered()
        {
            await OfferAsync(ConfigTools("safe"), new Dictionary<string, ToolHandler> { ["safe"] = Tool(), ["privileged"] = Tool() });
            Assert.Equal(new[] { "safe" }, Chat.LastRequest.ToolNames);
        }

        [Fact]
        public async Task NativeToolIsNeverOfferedAsAFunctionAndWarnsOnce()
        {
            var tools = new Dictionary<string, ToolHandler> { ["search"] = Tool(), ["web"] = new NativeTool("WebSearch") };
            await OfferAsync(ConfigTools("search", "web"), tools);
            await OfferAsync(ConfigTools("search", "web"), tools);

            Assert.All(Chat.Requests, r => Assert.Equal(new[] { "search" }, r.ToolNames));
            Assert.Single(Warnings, w => w.Contains("web"));
        }

        [Fact]
        public async Task NativeToolTrackingStubIsNeverOfferedAsAFunction()
        {
            var fake = await InitWithFakeAsync();
            fake.Variations[TestHelpers.Fixtures.ConfigKey] = TestHelpers.Fixtures.EnabledConfig(edit: j =>
                j["tools"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["search"] = TestHelpers.Fixtures.ToolJson("search"),
                    ["web"] = TestHelpers.Fixtures.ToolJson("web"),
                });

            await LdAi.Config(new ConfigArgs
            {
                Key = TestHelpers.Fixtures.ConfigKey,
                Handler = Handler(),
                ToolHandlers = new Dictionary<string, ToolHandler> { ["search"] = Tool(), ["web"] = new NativeTool("WebSearch") },
            }).InvokeAsync("hi", TestHelpers.Fixtures.UserContext);

            Assert.Equal(new[] { "search" }, Chat.LastRequest.ToolNames);
        }

        [Fact]
        public async Task NoConfigToolsSendsNoTools()
        {
            await OfferAsync(null, new Dictionary<string, ToolHandler> { ["a"] = Tool() });
            Assert.Empty(Chat.LastRequest.Tools);
        }

        [Fact]
        public async Task EmptyConfigToolsSendsNoTools()
        {
            await OfferAsync(new Dictionary<string, ToolDefinition>(), new Dictionary<string, ToolHandler> { ["a"] = Tool() });
            Assert.Empty(Chat.LastRequest.Tools);
        }
    }
}
