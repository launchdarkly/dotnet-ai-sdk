using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The tools a handler receives: local tools over registry tools, narrowed to the tools the
    /// active variation attaches.
    /// </summary>
    public class ToolScopingTests : InvokeHarness
    {
        private static FunctionTool Tool(List<string> ran, string name) =>
            new FunctionTool((_, _) =>
            {
                ran.Add(name);
                return Task.FromResult<object?>(name);
            });

        private void AttachTools(params string[] names) => SetVariation(j =>
        {
            var tools = new JsonObject();
            foreach (var n in names)
            {
                tools[n] = Fixtures.ToolJson(n);
            }

            j["tools"] = tools;
        });

        private async Task<HandlerRequest> ReceivedAsync(IReadOnlyDictionary<string, ToolHandler>? local, Registry? registry = null)
        {
            var handler = Returning("ok");
            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler, ToolHandlers = local, Registry = registry })
                .InvokeAsync("hi", Fixtures.UserContext);
            return Assert.Single(Calls);
        }

        [Fact]
        public async Task LocalToolsAreUsedWithoutARegistry()
        {
            AttachTools("safe");
            var ran = new List<string>();
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, "local") });

            await ((FunctionTool)request.ToolHandlers["safe"]).InvokeAsync(Fixtures.Json("{}"));
            Assert.Equal(new[] { "local" }, ran);
        }

        [Fact]
        public async Task RegistryToolsAreUsedWithoutLocalOnes()
        {
            AttachTools("safe");
            var ran = new List<string>();
            var registry = new Registry(null, new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, "registry") });
            var request = await ReceivedAsync(null, registry);

            await ((FunctionTool)request.ToolHandlers["safe"]).InvokeAsync(Fixtures.Json("{}"));
            Assert.Equal(new[] { "registry" }, ran);
        }

        [Fact]
        public async Task LocalToolsWinOverRegistryTools()
        {
            AttachTools("safe", "other");
            var ran = new List<string>();
            var registry = new Registry(null, new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, "registry"), ["other"] = Tool(ran, "other") });
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, "local") }, registry);

            await ((FunctionTool)request.ToolHandlers["safe"]).InvokeAsync(Fixtures.Json("{}"));
            await ((FunctionTool)request.ToolHandlers["other"]).InvokeAsync(Fixtures.Json("{}"));
            Assert.Equal(new[] { "local", "other" }, ran);
        }

        [Fact]
        public async Task NoToolsAnywhereGivesAnEmptyMap()
        {
            AttachTools("safe");
            var request = await ReceivedAsync(null, new Registry());
            Assert.Empty(request.ToolHandlers);
        }

        [Fact]
        public async Task UnattachedToolsNeverReachTheHandler()
        {
            AttachTools("safe");
            var ran = new List<string>();
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, "safe"), ["privileged"] = Tool(ran, "privileged") });

            Assert.Equal(new[] { "safe" }, request.ToolHandlers.Keys);
        }

        [Fact]
        public async Task NoConfigToolsMeansNoTools()
        {
            SetVariation();
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(new List<string>(), "safe") });
            Assert.Empty(request.ToolHandlers);
        }

        [Fact]
        public async Task EmptyConfigToolsMeansNoTools()
        {
            AttachTools();
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(new List<string>(), "safe") });
            Assert.Empty(request.ToolHandlers);
        }

        [Fact]
        public async Task AConfigToolWithNoRegisteredHandlerIsAbsent()
        {
            AttachTools("ToString", "safe");
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["safe"] = Tool(new List<string>(), "safe") });
            Assert.Equal(new[] { "safe" }, request.ToolHandlers.Keys);
        }

        [Fact]
        public async Task UnattachedNativeToolsAreDropped()
        {
            AttachTools("safe");
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler>
            {
                ["safe"] = Tool(new List<string>(), "safe"),
                ["web"] = new NativeTool("WebSearch"),
            });
            Assert.Equal(new[] { "safe" }, request.ToolHandlers.Keys.ToArray());
        }
    }
}
