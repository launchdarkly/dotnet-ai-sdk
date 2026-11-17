using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The basic invoke flow: evaluation, the handler call, the response and the disabled and
    /// missing cases.
    /// </summary>
    public class InvokeTests : InvokeHarness
    {
        [Fact]
        public async Task EvaluatesTheConfigKeyForTheContext()
        {
            SetVariation();
            await InvokeAsync(Returning("ok"));

            var evaluation = Assert.Single(Fake.Evaluations);
            Assert.Equal(Fixtures.ConfigKey, evaluation.Key);
            Assert.Equal(Fixtures.UserContext, evaluation.Context);
        }

        [Fact]
        public async Task CallsTheHandlerWithConfigInputVariablesAndHistory()
        {
            SetVariation(j => j["instructions"] = "Hello {{name}}");
            var history = new[] { new HistoryMessage(MessageRoles.User, "earlier") };
            await InvokeAsync(Returning("ok"), "question", new Dictionary<string, object?> { ["name"] = "Ann" }, history: history);

            var request = Assert.Single(Calls);
            Assert.Equal("question", request.UserInput);
            Assert.Equal("Hello {{name}}", request.Config.Instructions);
            Assert.Equal("test-model", request.Config.Model.Name);
            Assert.Equal("Ann", request.Variables["name"]);
            Assert.Equal(history, request.History);
        }

        [Fact]
        public async Task NullInputReachesTheHandlerAsNull()
        {
            SetVariation();
            await InvokeAsync(Returning("ok"), input: null);
            Assert.Null(Assert.Single(Calls).UserInput);
        }

        [Fact]
        public async Task AbsentHistoryReachesTheHandlerAsNull()
        {
            SetVariation();
            await InvokeAsync(Returning("ok"));
            Assert.Null(Assert.Single(Calls).History);
        }

        [Fact]
        public async Task ReturnsResponseUsageAndTrackData()
        {
            SetVariation();
            var response = await InvokeAsync(Returning("the answer", Fixtures.Usage(("input_tokens", 3), ("output_tokens", 4))));

            Assert.Equal("the answer", response.Response);
            Assert.Equal(7, response.Usage.Total);
            Assert.Equal(Fixtures.ConfigKey, response.TrackData.ConfigKey);
        }

        [Fact]
        public async Task HandlerReceivesTheTrackingEntry()
        {
            SetVariation();
            var response = await InvokeAsync(Returning("ok"));

            var ld = Assert.IsType<TrackData>(Assert.Single(Calls).Variables["__ld"]);
            Assert.Equal(response.TrackData.RunId, ld.RunId);
            Assert.Equal(Fixtures.ConfigKey, ld.ConfigKey);
            Assert.Equal("v1", ld.VariationKey);
        }

        [Fact]
        public async Task HandlerReceivesTheEvaluatedVariationOnlyWhenCaptureIsOn()
        {
            SetVariation();
            await InvokeAsync(Returning("ok"));
            Assert.True(Assert.Single(Calls).Variables.ContainsKey("__ldValue"));

            Calls.Clear();
            var noCapture = LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), Returning("ok"),
                new CreateHandlerOptions { CaptureContent = false });
            await InvokeAsync(noCapture);
            Assert.False(Assert.Single(Calls).Variables.ContainsKey("__ldValue"));
        }

        [Fact]
        public async Task EachInvocationGetsANewRunId()
        {
            SetVariation();
            var a = await InvokeAsync(Returning("ok"));
            var b = await InvokeAsync(Returning("ok"));
            Assert.NotEqual(a.TrackData.RunId, b.TrackData.RunId);
        }

        [Fact]
        public async Task MissingVariationThrowsNotFound()
        {
            var ex = await Assert.ThrowsAsync<AiConfigNotFoundException>(() => InvokeAsync(Returning("ok")));
            Assert.Equal(Fixtures.ConfigKey, ex.ConfigKey);
            Assert.Empty(Calls);
        }

        [Fact]
        public async Task DisabledVariationThrowsDisabled()
        {
            SetVariation(j => ((JsonObject)j["_ldMeta"]!)["enabled"] = false);
            var ex = await Assert.ThrowsAsync<AiConfigDisabledException>(() => InvokeAsync(Returning("ok")));
            Assert.Equal(Fixtures.ConfigKey, ex.ConfigKey);
            Assert.Empty(Calls);
        }

        [Fact]
        public async Task MetaWithoutEnabledIsDisabled()
        {
            SetVariation(j => ((JsonObject)j["_ldMeta"]!).Remove("enabled"));
            await Assert.ThrowsAsync<AiConfigDisabledException>(() => InvokeAsync(Returning("ok")));
        }

        [Fact]
        public async Task VariationWithoutMetaIsNotEnabled()
        {
            SetVariation(j => j.Remove("_ldMeta"));
            await Assert.ThrowsAnyAsync<LdAiException>(() => InvokeAsync(Returning("ok")));
            Assert.Empty(Calls);
        }

        [Fact]
        public async Task InvalidVariationThrows()
        {
            SetVariation(j => j.Remove("provider"));
            await Assert.ThrowsAnyAsync<LdAiException>(() => InvokeAsync(Returning("ok")));
            Assert.Empty(Calls);
        }

        [Fact]
        public async Task HandlerExceptionPropagates()
        {
            SetVariation();
            var error = new InvalidOperationException("provider down");
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(Throwing(error)));
            Assert.Same(error, thrown);
        }

        [Fact]
        public async Task CancellationReachesTheHandler()
        {
            SetVariation();
            using var cts = new CancellationTokenSource();
            CancellationToken seen = default;
            var handler = LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, ct) =>
            {
                seen = ct;
                return Task.FromResult(new HandlerResult { Output = "ok" });
            });

            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler })
                .InvokeAsync("hi", Fixtures.UserContext, cancellationToken: cts.Token);

            Assert.Equal(cts.Token, seen);
        }

        [Fact]
        public async Task JudgeConfigurationLogsOneWarningAndStillRuns()
        {
            SetVariation(j => j["judgeConfiguration"] = new JsonObject
            {
                ["judges"] = new JsonArray(new JsonObject { ["key"] = "judge-a", ["samplingRate"] = 1 }),
            });

            var first = await InvokeAsync(Returning("ok"));
            await InvokeAsync(Returning("ok"));

            Assert.Equal("ok", first.Response);
            Assert.Single(Warnings, w => w.IndexOf("judge", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task ConfigThroughTheClientInterfaceBehavesTheSame()
        {
            SetVariation();
            var response = await LdAi.GetClient().Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Returning("ok") })
                .InvokeAsync("hi", Fixtures.UserContext);
            Assert.Equal("ok", response.Response);
        }
    }
}
