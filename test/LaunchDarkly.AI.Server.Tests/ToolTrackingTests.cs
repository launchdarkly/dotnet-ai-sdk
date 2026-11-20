using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The tools a handler receives emit <c>$ld:ai:tool_call</c> when called.
    /// </summary>
    public class ToolTrackingTests : InvokeHarness
    {
        private const string ToolCall = "$ld:ai:tool_call";

        private async Task<HandlerRequest> ReceivedAsync(IReadOnlyDictionary<string, ToolHandler> tools)
        {
            SetVariation(j =>
            {
                var attached = new JsonObject();
                foreach (var key in tools.Keys)
                {
                    attached[key] = Fixtures.ToolJson(key);
                }

                j["tools"] = attached;
            });
            await InvokeAsync(Returning("ok"), tools: tools);
            Fake.ClearTracks();
            return Assert.Single(Calls);
        }

        [Fact]
        public async Task CallingAToolTracksAndCallsTheOriginal()
        {
            JsonElement? seen = null;
            var tool = new FunctionTool((args, _) =>
            {
                seen = args;
                return Task.FromResult<object?>("result");
            });
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["web-search"] = tool });

            var result = await ((FunctionTool)request.ToolHandlers["web-search"]).InvokeAsync(Fixtures.Json("{\"q\":\"x\"}"));

            Assert.Equal("result", result);
            Assert.Equal("x", seen!.Value.GetProperty("q").GetString());
            var e = Assert.Single(Fake.Events(ToolCall));
            Assert.Equal("web-search", e.DataString("toolKey"));
            Assert.Equal(Fixtures.ConfigKey, e.DataString("configKey"));
            Assert.Equal(Fixtures.UserContext, e.Context);
            Assert.Equal(1.0, e.Metric);
        }

        [Fact]
        public async Task ToolHandlersReceivedAreWrapped()
        {
            var tool = new FunctionTool((_, _) => Task.FromResult<object?>(null));
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["t"] = tool });
            Assert.NotSame(tool, request.ToolHandlers["t"]);
        }

        [Fact]
        public async Task NativeToolBecomesATrackingStubThatKeepsTheInstance()
        {
            var native = new NativeTool("WebSearch");
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["web-search"] = native });

            var stub = Assert.IsType<FunctionTool>(request.ToolHandlers["web-search"]);
            Assert.Same(native, stub.WrappedNativeTool);

            await stub.InvokeAsync(Fixtures.Json("{}"));
            var e = Assert.Single(Fake.Events(ToolCall));
            Assert.Equal("web-search", e.DataString("toolKey"));
        }

        [Fact]
        public async Task NativeToolStubTracksWithMetricOne()
        {
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["web-search"] = new NativeTool("WebSearch") });

            await ((FunctionTool)request.ToolHandlers["web-search"]).InvokeAsync(Fixtures.Json("{}"));

            Assert.Equal(1.0, Assert.Single(Fake.Events(ToolCall)).Metric);
        }

        [Fact]
        public async Task OrdinaryToolHasNoWrappedNativeTool()
        {
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["t"] = new FunctionTool((_, _) => Task.FromResult<object?>(null)) });
            Assert.Null(((FunctionTool)request.ToolHandlers["t"]).WrappedNativeTool);
        }

        [Fact]
        public async Task HandoffToolsAreNotTracked()
        {
            var request = await ReceivedAsync(new Dictionary<string, ToolHandler> { ["__handoff_next"] = new FunctionTool((_, _) => Task.FromResult<object?>("ok")) });
            await ((FunctionTool)request.ToolHandlers["__handoff_next"]).InvokeAsync(Fixtures.Json("{}"));
            Assert.Empty(Fake.Events(ToolCall));
        }

        [Fact]
        public void NativeToolKeepsTheProvidersName()
        {
            Assert.Equal("WebSearch", new NativeTool("WebSearch").ToolName);
        }
    }
}
