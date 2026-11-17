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
    /// A provider or tool failure is recorded on the spans, ends them, and reaches the caller
    /// unchanged.
    /// </summary>
    public sealed class HandlerErrorTests : HandlerHarness, IDisposable
    {
        private readonly SpanRecorder _spans = new SpanRecorder();

        public void Dispose() => _spans.Dispose();

        [Fact]
        public async Task ProviderErrorIsRecordedEndedAndRethrown()
        {
            var error = new InvalidOperationException("provider down");
            Chat.ThenThrow(error);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Config()));

            Assert.Same(error, thrown);
            Assert.Empty(_spans.Open);
            foreach (var span in new[] { _spans.Root, _spans.Named("chat ").Single() })
            {
                Assert.Equal(ActivityStatusCode.Error, span.Status);
                Assert.False(string.IsNullOrEmpty(span.StatusDescription));
                var exception = span.Event("exception");
                Assert.NotNull(exception);
                Assert.Equal(typeof(InvalidOperationException).FullName, exception!.Value.EventAttr("exception.type")?.ToString());
                Assert.Equal("provider down", exception.Value.EventAttr("exception.message")?.ToString());
            }
        }

        [Fact]
        public async Task ToolErrorIsRecordedOnTheToolSpanAndRethrown()
        {
            var error = new InvalidOperationException("tool broke");
            Chat.ThenToolCall("lookup").ThenText("never");
            var tools = new Dictionary<string, ToolHandler>
            {
                ["lookup"] = new FunctionTool((_, _) => Task.FromException<object?>(error)),
            };

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunAsync(Config() with { Tools = ConfigTools("lookup") }, tools: tools));

            Assert.Same(error, thrown);
            Assert.Empty(_spans.Open);
            Assert.Equal(ActivityStatusCode.Error, _spans.Single("execute_tool lookup").Status);
            Assert.Equal(ActivityStatusCode.Error, _spans.Root.Status);
        }

        [Fact]
        public async Task ErrorThroughConfigTracksGenerationErrorAndRethrows()
        {
            var fake = await InitWithFakeAsync();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            Chat.ThenThrow(new TimeoutException("slow"));

            await Assert.ThrowsAsync<TimeoutException>(() =>
                LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext));

            Assert.Single(fake.Events("$ld:ai:generation:error"));
            Assert.Empty(_spans.Open);
        }
    }
}
