using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using LaunchDarkly.Sdk;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The evaluation context reaches the handler's variables as <c>ldContext</c>.
    /// </summary>
    public class ContextInterpolationTests : LdAiTestBase
    {
        private async Task<HandlerRequest> InvokeAsync(IReadOnlyDictionary<string, object?>? variables, bool manyHandlers = false)
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig("OpenAI");
            var calls = new List<HandlerRequest>();
            var handler = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages, calls);
            var args = manyHandlers
                ? new ConfigArgs
                {
                    Key = Fixtures.ConfigKey,
                    Handlers = new[] { Fixtures.RecordingHandler("Anthropic", HandlerModes.Messages), handler },
                }
                : new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler };
            await LdAi.Config(args).InvokeAsync("hi", Fixtures.UserContext, variables);
            return Assert.Single(calls);
        }

        [Fact]
        public async Task ContextIsExposedAsLdContext()
        {
            var request = await InvokeAsync(null);

            var context = Assert.IsType<Context>(request.Variables["ldContext"]);
            Assert.Equal("user-123", context.Key);
            Assert.Equal("user@example.com", LdAi.ParseTemplate("{{ldContext.email}}", request.Variables));
        }

        [Fact]
        public async Task KindIsIncluded()
        {
            var request = await InvokeAsync(null);
            Assert.Equal("user", LdAi.ParseTemplate("{{ldContext.kind}}", request.Variables));
        }

        [Fact]
        public async Task CallerVariablesAreKeptBesideLdContext()
        {
            var request = await InvokeAsync(new Dictionary<string, object?> { ["someVariable"] = 5 });

            Assert.Equal(5, request.Variables["someVariable"]);
            Assert.Equal(Fixtures.UserContext, request.Variables["ldContext"]);
        }

        [Fact]
        public async Task CallerSuppliedLdContextIsDiscarded()
        {
            var request = await InvokeAsync(new Dictionary<string, object?> { ["ldContext"] = "custom" });

            Assert.Equal(Fixtures.UserContext, request.Variables["ldContext"]);
            Assert.DoesNotContain("custom", request.Variables.Values);
        }

        [Fact]
        public async Task InjectionHappensWithSeveralHandlers()
        {
            var request = await InvokeAsync(null, manyHandlers: true);
            Assert.Equal(Fixtures.UserContext, request.Variables["ldContext"]);
        }

        [Fact]
        public async Task CallerVariablesAreNotMutated()
        {
            var variables = new Dictionary<string, object?> { ["someVariable"] = 5 };
            await InvokeAsync(variables);
            Assert.Equal(new[] { "someVariable" }, variables.Keys);
        }
    }
}
