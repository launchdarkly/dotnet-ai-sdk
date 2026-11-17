using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The tool loop: the model's tool calls run the matching tool, the result goes back as text,
    /// and the loop ends when the model answers. Only the tools offered for this request can run.
    /// </summary>
    public class ToolLoopTests : HandlerHarness
    {
        [Fact]
        public async Task SingleToolCallThenAnswer()
        {
            JsonElement? args = null;
            var tool = new FunctionTool((a, _) =>
            {
                args = a;
                return Task.FromResult<object?>("sunny");
            });
            Chat.ThenToolCall("weather", new Dictionary<string, object?> { ["q"] = "Paris" }).ThenText("It is sunny.", FakeChatClient.Usage(10, 5));

            var result = await RunAsync(Config() with { Tools = ConfigTools("weather") }, tools: new Dictionary<string, ToolHandler> { ["weather"] = tool });

            Assert.Equal("Paris", args!.Value.GetProperty("q").GetString());
            Assert.Equal(2, Chat.Requests.Count);
            Assert.Equal("sunny", Assert.Single(Chat.Requests[1].ToolResults).Result?.ToString());
            Assert.Equal("It is sunny.", result.Output);
        }

        [Fact]
        public async Task ConsecutiveToolCallsRunOnceEachInOrder()
        {
            var ran = new List<string>();
            Chat.ThenToolCall("a").ThenToolCall("b").ThenText("final");

            var result = await RunAsync(Config() with { Tools = ConfigTools("a", "b") },
                tools: new Dictionary<string, ToolHandler> { ["a"] = Tool(ran, name: "a"), ["b"] = Tool(ran, name: "b") });

            Assert.Equal(new[] { "a", "b" }, ran);
            Assert.Equal("final", result.Output);
            Assert.Equal(3, Chat.Requests.Count);
        }

        [Fact]
        public async Task ParallelToolCallsInOneTurnRunInOrder()
        {
            var ran = new List<string>();
            Chat.ThenToolCalls(("c1", "a", new Dictionary<string, object?>()), ("c2", "b", new Dictionary<string, object?>())).ThenText("final");

            await RunAsync(Config() with { Tools = ConfigTools("a", "b") },
                tools: new Dictionary<string, ToolHandler> { ["a"] = Tool(ran, name: "a"), ["b"] = Tool(ran, name: "b") });

            Assert.Equal(new[] { "a", "b" }, ran);
            Assert.Equal(new[] { "c1", "c2" }, Chat.Requests[1].ToolResults.Select(r => r.CallId));
        }

        [Fact]
        public async Task RegisteredButUnattachedToolFailsClosed()
        {
            var ran = new List<string>();
            Chat.ThenToolCall("privileged").ThenText("never");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(Config() with { Tools = ConfigTools("safe") },
                tools: new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, name: "safe"), ["privileged"] = Tool(ran, name: "privileged") }));

            Assert.Contains("privileged", ex.Message, StringComparison.Ordinal);
            Assert.Empty(ran);
        }

        [Theory]
        [InlineData("nowhere")]
        [InlineData("ToString")]
        [InlineData("constructor")]
        public async Task UnknownToolNameFailsClosed(string name)
        {
            var ran = new List<string>();
            Chat.ThenToolCall(name).ThenText("never");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(Config() with { Tools = ConfigTools("safe") },
                tools: new Dictionary<string, ToolHandler> { ["safe"] = Tool(ran, name: "safe") }));

            Assert.Contains(name, ex.Message, StringComparison.Ordinal);
            Assert.Empty(ran);
        }

        [Fact]
        public async Task ToolExceptionPropagates()
        {
            var error = new InvalidOperationException("tool broke");
            Chat.ThenToolCall("a").ThenText("never");

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Config() with { Tools = ConfigTools("a") },
                tools: new Dictionary<string, ToolHandler> { ["a"] = new FunctionTool((_, _) => Task.FromException<object?>(error)) }));

            Assert.Same(error, thrown);
        }

        [Fact]
        public async Task NativeToolValueIsNeverCalled()
        {
            Chat.ThenToolCall("web").ThenText("never");
            await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(Config() with { Tools = ConfigTools("web") },
                tools: new Dictionary<string, ToolHandler> { ["web"] = new NativeTool("WebSearch") }));
            Assert.Empty(Chat.Requests[0].Tools);
        }

        [Fact]
        public async Task ObjectResultIsSentAsJsonText()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            var result = new Dictionary<string, object?> { ["temp"] = 72, ["city"] = "Zürich" };

            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: result) });

            var sent = Assert.IsType<string>(Assert.Single(Chat.Requests[1].ToolResults).Result);
            Assert.Equal("{\"temp\":72,\"city\":\"Zürich\"}", sent);
        }

        [Fact]
        public async Task NullResultIsSentAsEmptyText()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: null) });
            Assert.Equal("", Assert.Single(Chat.Requests[1].ToolResults).Result);
        }

        [Fact]
        public async Task StringResultIsSentUnchanged()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: "{not json") });
            Assert.Equal("{not json", Assert.Single(Chat.Requests[1].ToolResults).Result);
        }

        [Fact]
        public async Task NoConfigToolsMeansOneCallAndNoToolRuns()
        {
            var ran = new List<string>();
            await RunAsync(Config(), tools: new Dictionary<string, ToolHandler> { ["a"] = Tool(ran, name: "a") });

            Assert.Single(Chat.Requests);
            Assert.Empty(Chat.LastRequest.Tools);
            Assert.Empty(ran);
        }

        [Fact]
        public async Task OnlyTheConfigsToolsAreOfferedOnEveryTurn()
        {
            Chat.ThenToolCall("safe").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("safe") },
                tools: new Dictionary<string, ToolHandler> { ["safe"] = Tool(), ["privileged"] = Tool() });

            Assert.All(Chat.Requests, r => Assert.Equal(new[] { "safe" }, r.ToolNames));
        }

        [Fact]
        public async Task UsageAddsUpAcrossTurns()
        {
            Chat.ThenToolCall("a").ThenText("done", FakeChatClient.Usage(10, 5));
            var fake = await InitWithFakeAsync();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j => j["tools"] = new JsonObject { ["a"] = Fixtures.ToolJson("a") });

            var response = await LdAi.Config(new ConfigArgs
            {
                Key = Fixtures.ConfigKey,
                Handler = Handler(),
                ToolHandlers = new Dictionary<string, ToolHandler> { ["a"] = Tool() },
            }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal(11, response.Usage.Input);
            Assert.Equal(6, response.Usage.Output);
        }

        [Fact]
        public async Task ToolCallsThroughConfigAreTracked()
        {
            Chat.ThenToolCall("a").ThenToolCall("a").ThenText("done");
            var fake = await InitWithFakeAsync();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(edit: j => j["tools"] = new JsonObject { ["a"] = Fixtures.ToolJson("a") });

            await LdAi.Config(new ConfigArgs
            {
                Key = Fixtures.ConfigKey,
                Handler = Handler(),
                ToolHandlers = new Dictionary<string, ToolHandler> { ["a"] = Tool() },
            }).InvokeAsync("hi", Fixtures.UserContext);

            var calls = fake.Events("$ld:ai:tool_call");
            Assert.Equal(2, calls.Count);
            Assert.All(calls, c => Assert.Equal("a", c.DataString("toolKey")));
        }
    }
}
