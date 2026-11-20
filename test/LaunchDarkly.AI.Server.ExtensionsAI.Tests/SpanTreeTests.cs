using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// The span tree the handler emits on the SDK's ActivitySource, checked with a real listener:
    /// names, parentage, gen_ai attributes and events, status and that every span ends.
    /// </summary>
    public sealed class SpanTreeTests : HandlerHarness, IDisposable
    {
        private readonly SpanRecorder _spans = new SpanRecorder();

        public void Dispose() => _spans.Dispose();

        private async Task RunWithOneToolCallAsync(bool? capture = null)
        {
            Chat.ThenToolCall("web-search", new Dictionary<string, object?> { ["q"] = "weather" })
                .ThenText("It is sunny.", FakeChatClient.Usage(10, 5));
            var config = Config() with { Tools = ConfigTools("web-search") };
            var handler = capture == null ? Handler() : Handler(new ChatClientHandlerOptions { CaptureContent = capture });
            await RunAsync(config, "What is the weather?", tools: new Dictionary<string, ToolHandler> { ["web-search"] = Tool(result: "sunny") }, handler: handler);
        }

        [Fact]
        public async Task SpanNamesAreInvokeAgentChatAndExecuteTool()
        {
            await RunWithOneToolCallAsync();

            Assert.NotNull(_spans.Root);
            Assert.Equal(2, _spans.Finished.Count(s => s.DisplayName == "chat test-model"));
            Assert.Single(_spans.Finished, s => s.DisplayName == "execute_tool web-search");
        }

        [Fact]
        public async Task ChatAndToolSpansAreSiblingsUnderTheRoot()
        {
            await RunWithOneToolCallAsync();

            var root = _spans.Root;
            Assert.All(_spans.Named("chat "), s => Assert.Equal(root.SpanId, s.ParentSpanId));
            var tool = _spans.Single("execute_tool web-search");
            Assert.Equal(root.SpanId, tool.ParentSpanId);
            Assert.DoesNotContain(_spans.Named("chat "), s => s.SpanId == tool.ParentSpanId);
        }

        [Fact]
        public async Task RootJoinsTheCallersTrace()
        {
            using var source = new ActivitySource(SpanRecorder.SourceName);
            using var parent = source.StartActivity("caller");
            await RunAsync(Config());
            Assert.Equal(parent!.SpanId, _spans.Root.ParentSpanId);
        }

        [Fact]
        public async Task OperationNameIsSetOnEachSpan()
        {
            await RunWithOneToolCallAsync();

            Assert.Equal("invoke_agent", _spans.Root.AttrString("gen_ai.operation.name"));
            Assert.All(_spans.Named("chat "), s => Assert.Equal("chat", s.AttrString("gen_ai.operation.name")));
            Assert.Equal("execute_tool", _spans.Single("execute_tool web-search").AttrString("gen_ai.operation.name"));
        }

        [Fact]
        public async Task ProviderIsTheConfigsProviderLowerCased()
        {
            await RunAsync(Config());

            var chat = _spans.Named("chat ").Single();
            Assert.Equal("openai", chat.AttrString("gen_ai.provider.name"));
            Assert.Equal("microsoft.extensions.ai", chat.AttrString("gen_ai.system"));
        }

        [Fact]
        public async Task BedrockProviderIsWrittenAsAwsBedrock()
        {
            await RunAsync(Fixtures.Config(provider: "Bedrock") with { Instructions = "x" });
            Assert.Equal("aws.bedrock", _spans.Named("chat ").Single().AttrString("gen_ai.provider.name"));
        }

        [Fact]
        public async Task RequestAndResponseModelAreSet()
        {
            Chat.ThenText("ok", modelId: "test-model-2026");
            await RunAsync(Config());

            var chat = _spans.Named("chat ").Single();
            Assert.Equal("test-model", chat.AttrString("gen_ai.request.model"));
            Assert.Equal("test-model-2026", chat.AttrString("gen_ai.response.model"));
        }

        [Fact]
        public async Task ResponseModelFallsBackToTheConfigModel()
        {
            Chat.ThenText("ok");
            await RunAsync(Config());
            Assert.Equal("test-model", _spans.Named("chat ").Single().AttrString("gen_ai.response.model"));
        }

        [Fact]
        public async Task TokenAttributesAndAliasesAreSet()
        {
            Chat.ThenText("ok", FakeChatClient.Usage(10, 5));
            await RunAsync(Config());

            var chat = _spans.Named("chat ").Single();
            Assert.Equal(10L, Convert.ToInt64(chat.Attr("gen_ai.usage.input_tokens")));
            Assert.Equal(5L, Convert.ToInt64(chat.Attr("gen_ai.usage.output_tokens")));
            Assert.Equal(15L, Convert.ToInt64(chat.Attr("gen_ai.usage.total_tokens")));
            Assert.Equal(10L, Convert.ToInt64(chat.Attr("gen_ai.usage.prompt_tokens")));
            Assert.Equal(5L, Convert.ToInt64(chat.Attr("gen_ai.usage.completion_tokens")));
        }

        [Fact]
        public async Task IndexedPromptAndCompletionAttributesAreSetWhenCaptureIsOn()
        {
            Chat.ThenText("Hello back");
            await RunAsync(Config("You are helpful."), "Hello");

            var chat = _spans.Named("chat ").Single();
            Assert.Equal("system", chat.AttrString("gen_ai.prompt.0.role"));
            Assert.Equal("You are helpful.", chat.AttrString("gen_ai.prompt.0.content"));
            Assert.Equal("user", chat.AttrString("gen_ai.prompt.1.role"));
            Assert.Equal("Hello", chat.AttrString("gen_ai.prompt.1.content"));
            Assert.Equal("assistant", chat.AttrString("gen_ai.completion.0.role"));
            Assert.Equal("Hello back", chat.AttrString("gen_ai.completion.0.content"));
        }

        [Fact]
        public async Task ContentEventsAreAddedWhenCaptureIsOn()
        {
            Chat.ThenText("Hello back");
            await RunAsync(Config("You are helpful."), "Hello");

            var chat = _spans.Named("chat ").Single();
            var prompt = chat.Event("gen_ai.content.prompt");
            var completion = chat.Event("gen_ai.content.completion");
            Assert.NotNull(prompt);
            Assert.NotNull(completion);
            Assert.Contains(prompt!.Value.Tags, t => (t.Value?.ToString() ?? "").Contains("Hello"));
            Assert.Contains(completion!.Value.Tags, t => (t.Value?.ToString() ?? "").Contains("Hello back"));
        }

        [Fact]
        public async Task NoContentIsRecordedWhenCaptureIsOff()
        {
            await RunWithOneToolCallAsync(capture: false);

            Assert.All(_spans.Finished, s =>
            {
                Assert.DoesNotContain(s.TagObjects, t => t.Key.StartsWith("gen_ai.prompt.", StringComparison.Ordinal));
                Assert.DoesNotContain(s.TagObjects, t => t.Key.StartsWith("gen_ai.completion.", StringComparison.Ordinal));
                Assert.Null(s.Event("gen_ai.content.prompt"));
                Assert.Null(s.Event("gen_ai.content.completion"));
                Assert.Null(s.Attr("gen_ai.tool.call.arguments"));
                Assert.Null(s.Attr("gen_ai.tool.call.result"));
            });
            Assert.NotNull(_spans.Named("chat ").Last().Attr("gen_ai.usage.input_tokens"));
        }

        [Fact]
        public async Task ConfiguredDefaultTurnsCaptureOff()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            Chat.ThenText("secret");
            await RunAsync(Config());
            Assert.Null(_spans.Named("chat ").Single().Attr("gen_ai.completion.0.content"));
        }

        [Fact]
        public async Task ToolSpanCarriesArgumentsAndResultAsCompactJson()
        {
            Chat.ThenToolCall("lookup", new Dictionary<string, object?> { ["city"] = "Zürich", ["q"] = "a<b" }).ThenText("done");
            var result = new Dictionary<string, object?> { ["temp"] = 72, ["city"] = "Zürich" };
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: result) });

            var tool = _spans.Single("execute_tool lookup");
            Assert.Equal("{\"city\":\"Zürich\",\"q\":\"a<b\"}", tool.AttrString("gen_ai.tool.call.arguments"));
            Assert.Equal("{\"temp\":72,\"city\":\"Zürich\"}", tool.AttrString("gen_ai.tool.call.result"));
        }

        [Fact]
        public async Task NullToolResultWritesNoResultAttribute()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: null) });
            Assert.Null(_spans.Single("execute_tool lookup").Attr("gen_ai.tool.call.result"));
        }

        [Fact]
        public async Task JsonNullToolResultWritesNoResultAttribute()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            var jsonNull = System.Text.Json.JsonDocument.Parse("null").RootElement.Clone();
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: jsonNull) });
            Assert.Null(_spans.Single("execute_tool lookup").Attr("gen_ai.tool.call.result"));
        }

        [Fact]
        public async Task RootStartsANewTraceWithoutACurrentSpan()
        {
            Activity.Current = null;
            await RunAsync(Config());
            Assert.Equal(default, _spans.Root.ParentSpanId);
        }

        [Fact]
        public async Task EmptyProviderIsWrittenAsOpenai()
        {
            await RunAsync(Fixtures.Config(provider: "") with { Instructions = "x" });

            var chat = _spans.Named("chat ").Single();
            Assert.Equal("openai", chat.AttrString("gen_ai.provider.name"));
            Assert.Equal("microsoft.extensions.ai", chat.AttrString("gen_ai.system"));
        }

        [Fact]
        public async Task StringToolResultIsWrittenUnchanged()
        {
            Chat.ThenToolCall("lookup").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: "plain \"text\"") });
            Assert.Equal("plain \"text\"", _spans.Single("execute_tool lookup").AttrString("gen_ai.tool.call.result"));
        }

        [Fact]
        public async Task UnserializableToolResultFallsBackToItsStringForm()
        {
            var cyclic = new Cyclic();
            cyclic.Self = cyclic;
            Chat.ThenToolCall("lookup").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: new Dictionary<string, ToolHandler> { ["lookup"] = Tool(result: cyclic) });
            Assert.Equal(cyclic.ToString(), _spans.Single("execute_tool lookup").AttrString("gen_ai.tool.call.result"));
        }

        [Fact]
        public async Task SpansEndWithStatusOk()
        {
            await RunWithOneToolCallAsync();

            Assert.All(_spans.Finished, s => Assert.Equal(ActivityStatusCode.Ok, s.Status));
            Assert.Empty(_spans.Open);
        }

        [Fact]
        public async Task OneChatSpanPerModelTurn()
        {
            Chat.ThenToolCall("a").ThenToolCall("b").ThenText("done");
            await RunAsync(Config() with { Tools = ConfigTools("a", "b") },
                tools: new Dictionary<string, ToolHandler> { ["a"] = Tool(), ["b"] = Tool() });

            Assert.Equal(3, _spans.Named("chat ").Count);
            Assert.Equal(2, _spans.Named("execute_tool ").Count);
            Assert.Single(_spans.Finished, s => s.DisplayName == "invoke_agent");
        }

        [Fact]
        public async Task RootCarriesTheRunsCacheTokens()
        {
            Chat.Then(_ => new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "ok"))
            {
                Usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15, CachedInputTokenCount = 4 },
            });
            await RunAsync(Config());

            foreach (var span in new[] { _spans.Root, _spans.Named("chat ").Single() })
            {
                Assert.Equal(10L, Convert.ToInt64(span.Attr("gen_ai.usage.input_tokens")));
                Assert.Equal(4L, Convert.ToInt64(span.Attr("gen_ai.usage.cache_read.input_tokens")));
            }
        }

        private sealed class Cyclic
        {
            public Cyclic? Self { get; set; }

            public override string ToString() => "cyclic-value";
        }
    }
}
