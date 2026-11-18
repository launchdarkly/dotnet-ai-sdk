using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The events an invocation tracks automatically: duration, success or error, and tokens.
    /// </summary>
    public class InvokeTrackingTests : InvokeHarness
    {
        private const string Duration = "$ld:ai:duration:total";
        private const string Success = "$ld:ai:generation:success";
        private const string Error = "$ld:ai:generation:error";
        private const string TokensTotal = "$ld:ai:tokens:total";
        private const string TokensInput = "$ld:ai:tokens:input";
        private const string TokensOutput = "$ld:ai:tokens:output";

        private static void AssertTrackData(TrackedEvent e, ProviderResponse? response = null)
        {
            Assert.Equal(Fixtures.UserContext, e.Context);
            Assert.Equal(Fixtures.ConfigKey, e.DataString("configKey"));
            Assert.Equal("v1", e.DataString("variationKey"));
            Assert.Equal(2, e.DataJson.GetProperty("version").GetInt32());
            Assert.Equal("test-model", e.DataString("modelName"));
            Assert.Equal("OpenAI", e.DataString("providerName"));
            if (response != null)
            {
                Assert.Equal(response.TrackData.RunId, e.DataString("runId"));
            }
        }

        [Fact]
        public async Task SuccessTracksDurationAndSuccess()
        {
            SetVariation();
            var response = await InvokeAsync(Returning("ok"));

            var duration = Assert.Single(Fake.Events(Duration));
            Assert.True(duration.Metric >= 0);
            AssertTrackData(duration, response);

            var success = Assert.Single(Fake.Events(Success));
            Assert.Equal(1.0, success.Metric);
            AssertTrackData(success, response);
            Assert.Empty(Fake.Events(Error));
        }

        [Fact]
        public async Task FailureTracksDurationAndErrorThenRethrows()
        {
            SetVariation();
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(Throwing(new InvalidOperationException("boom"))));

            Assert.Single(Fake.Events(Duration));
            var error = Assert.Single(Fake.Events(Error));
            Assert.Equal(1.0, error.Metric);
            AssertTrackData(error);
            Assert.Empty(Fake.Events(Success));
        }

        [Fact]
        public async Task TokenCountsAreTrackedWhenAboveZero()
        {
            SetVariation();
            var response = await InvokeAsync(Returning("ok", Fixtures.Usage(("input_tokens", 10), ("output_tokens", 5))));

            Assert.Equal(15.0, Assert.Single(Fake.Events(TokensTotal)).Metric);
            Assert.Equal(10.0, Assert.Single(Fake.Events(TokensInput)).Metric);
            Assert.Equal(5.0, Assert.Single(Fake.Events(TokensOutput)).Metric);
            AssertTrackData(Fake.Events(TokensTotal)[0], response);
        }

        [Fact]
        public async Task ZeroTokenCountsAreNotTracked()
        {
            SetVariation();
            await InvokeAsync(Returning("ok", Fixtures.Usage(("input_tokens", 0), ("output_tokens", 0))));
            Assert.Empty(Fake.EventsStartingWith("$ld:ai:tokens:"));
        }

        [Fact]
        public async Task DurationIsTrackedBeforeTheErrorReachesTheCaller()
        {
            SetVariation();
            try
            {
                await InvokeAsync(Throwing(new InvalidOperationException("boom")));
            }
            catch (InvalidOperationException)
            {
                Assert.Single(Fake.Events(Duration));
                return;
            }

            Assert.Fail("The handler's exception did not propagate.");
        }

        [Fact]
        public async Task NoToolEventsWithoutToolCalls()
        {
            SetVariation();
            await InvokeAsync(Returning("ok"));
            Assert.Empty(Fake.Events("$ld:ai:tool_call"));
        }

        [Fact]
        public async Task ThrowingTrackDoesNotFailTheInvocation()
        {
            SetVariation();
            Fake.TrackThrows = true;
            var response = await InvokeAsync(Returning("ok", Fixtures.Usage(("input_tokens", 1), ("output_tokens", 1))));
            Assert.Equal("ok", response.Response);
        }

        private ProviderHandler CallingTool(string key, string args, Action<object?> received) =>
            LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), async (request, ct) =>
            {
                Calls.Add(request);
                received(await ((FunctionTool)request.ToolHandlers[key]).InvokeAsync(Fixtures.Json(args), ct));
                return new HandlerResult { Output = "ok" };
            });

        private void SetVariationWithTool(string key) =>
            SetVariation(j => j["tools"] = new JsonObject { [key] = Fixtures.ToolJson(key) });

        [Fact]
        public async Task ThrowingTrackNeverReplacesTheHandlersException()
        {
            SetVariation();
            Fake.TrackThrows = true;
            var error = new InvalidOperationException("handler failed");

            var thrown = await Record.ExceptionAsync(() => InvokeAsync(Throwing(error)));

            Assert.Same(error, thrown);
        }

        [Fact]
        public async Task ThrowingTrackDoesNotFailAToolCall()
        {
            SetVariationWithTool("lookup");
            Fake.TrackThrows = true;
            object? received = null;
            var tool = new FunctionTool((_, _) => Task.FromResult<object?>("tool value"));

            var response = await InvokeAsync(
                CallingTool("lookup", "{}", r => received = r),
                tools: new Dictionary<string, ToolHandler> { ["lookup"] = tool });

            Assert.Equal("tool value", received);
            Assert.Equal("ok", response.Response);
        }

        [Fact]
        public async Task EventsCarryNoHistoryOrToolContent()
        {
            SetVariationWithTool("lookup");
            var tool = new FunctionTool((_, _) => Task.FromResult<object?>("secret-r"));

            await InvokeAsync(
                CallingTool("lookup", "{\"q\":\"secret-a\"}", _ => { }),
                tools: new Dictionary<string, ToolHandler> { ["lookup"] = tool },
                history: new[] { new HistoryMessage("user", "secret-h") });

            Assert.Single(Fake.Events("$ld:ai:tool_call"));
            Assert.All(Fake.EventsStartingWith("$ld:ai:"), e =>
            {
                Assert.DoesNotContain("secret", e.Data.ToJsonString(), StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task EventsCarryNoUserContent()
        {
            SetVariation();
            await InvokeAsync(Returning("secret answer"), "secret question");
            Assert.All(Fake.EventsStartingWith("$ld:ai:"), e =>
            {
                Assert.DoesNotContain("secret", e.Data.ToJsonString(), StringComparison.Ordinal);
            });
            Assert.True(Fake.Tracks.Count(t => t.Name.StartsWith("$ld:ai:", StringComparison.Ordinal)) >= 2);
        }
    }
}
