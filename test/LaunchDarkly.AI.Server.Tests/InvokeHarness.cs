using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// A test base that runs one invocation of the test config against a scripted handler.
    /// </summary>
    public abstract class InvokeHarness : LdAiTestBase
    {
        protected FakeLdClient Fake { get; private set; } = new FakeLdClient();

        protected List<HandlerRequest> Calls { get; } = new List<HandlerRequest>();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Fake = UseFakeClient();
            Fake.ClearTracks();
        }

        /// <summary>Sets the test config's variation.</summary>
        protected void SetVariation(Action<JsonObject>? edit = null, string provider = "OpenAI", string? mode = "messages") =>
            Fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig(provider, mode, edit);

        /// <summary>A handler for the test config's provider that returns <paramref name="result"/>.</summary>
        protected ProviderHandler Handler(Func<HandlerRequest, HandlerResult> result, string provider = "OpenAI", string mode = HandlerModes.Messages) =>
            LdAi.CreateHandler(new ProvidesFor(provider, mode), (request, _) =>
            {
                Calls.Add(request);
                return Task.FromResult(result(request));
            });

        protected ProviderHandler Returning(object? output, IReadOnlyDictionary<string, object?>? usage = null) =>
            Handler(_ => new HandlerResult { Output = output, Usage = usage });

        protected ProviderHandler Throwing(Exception error) =>
            LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (request, _) =>
            {
                Calls.Add(request);
                return Task.FromException<HandlerResult>(error);
            });

        protected Task<ProviderResponse> InvokeAsync(
            ProviderHandler handler,
            string? input = "hi",
            IReadOnlyDictionary<string, object?>? variables = null,
            IReadOnlyDictionary<string, ToolHandler>? tools = null,
            IReadOnlyList<HistoryMessage>? history = null) =>
            LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler, ToolHandlers = tools })
                .InvokeAsync(input, Fixtures.UserContext, variables, history, CancellationToken.None);
    }
}
