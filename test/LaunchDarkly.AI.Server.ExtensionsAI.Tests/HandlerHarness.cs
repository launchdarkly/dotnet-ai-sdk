using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// A test base for the IChatClient handler: a reset SDK, a <see cref="FakeChatClient"/> and
    /// helpers that call the handler directly.
    /// </summary>
    public abstract class HandlerHarness : LdAiTestBase
    {
        protected FakeChatClient Chat { get; } = new FakeChatClient();

        protected ProviderHandler Handler(ChatClientHandlerOptions? options = null) =>
            options == null ? ChatClientHandler.Create(Chat) : ChatClientHandler.Create(Chat, options);

        protected static AiConfig Config(string? instructions = "You are helpful.", params ConfigMessage[] messages) =>
            Fixtures.Config() with
            {
                Instructions = instructions,
                Messages = messages.Length == 0 ? null : messages,
            };

        protected static ConfigMessage Msg(string role, string content) => new ConfigMessage(role, content);

        protected static HistoryMessage Hist(string role, string content) => new HistoryMessage(role, content);

        protected static IReadOnlyDictionary<string, object?> Vars(params (string Key, object? Value)[] entries) =>
            entries.ToDictionary(e => e.Key, e => e.Value);

        protected static FunctionTool Tool(List<string>? ran = null, object? result = null, string name = "tool") =>
            new FunctionTool((_, _) =>
            {
                ran?.Add(name);
                return Task.FromResult(result);
            });

        protected static IReadOnlyDictionary<string, ToolDefinition> ConfigTools(params string[] names) =>
            names.ToDictionary(n => n, n => Fixtures.Tool(n, "does " + n));

        protected Task<HandlerResult> RunAsync(
            AiConfig config,
            string? input = "hi",
            IReadOnlyDictionary<string, object?>? variables = null,
            IReadOnlyDictionary<string, ToolHandler>? tools = null,
            IReadOnlyList<HistoryMessage>? history = null,
            ProviderHandler? handler = null) =>
            (handler ?? Handler()).InvokeAsync(
                new HandlerRequest(config)
                {
                    UserInput = input,
                    Variables = variables ?? new Dictionary<string, object?>(),
                    ToolHandlers = tools ?? new Dictionary<string, ToolHandler>(),
                    History = history,
                },
                CancellationToken.None);
    }
}
