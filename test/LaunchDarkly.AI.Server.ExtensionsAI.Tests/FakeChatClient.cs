using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// One request the handler sent: a copy of the messages and options at the time of the call.
    /// </summary>
    public sealed class ChatRequest
    {
        public ChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        {
            Messages = messages.ToList();
            Options = options?.Clone();
            CancellationToken = cancellationToken;
        }

        /// <summary>The token the handler passed with this request.</summary>
        public CancellationToken CancellationToken { get; }

        public IReadOnlyList<ChatMessage> Messages { get; }

        public ChatOptions? Options { get; }

        /// <summary>The system prompt, wherever the handler put it: system messages or Instructions.</summary>
        public string SystemText =>
            string.Join("\n", Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text)
                .Concat(Options?.Instructions == null ? Array.Empty<string>() : new[] { Options.Instructions }));

        /// <summary>The non-system turns, as (role, text).</summary>
        public IReadOnlyList<(string Role, string Text)> Turns =>
            Messages.Where(m => m.Role != ChatRole.System).Select(m => (m.Role.Value, m.Text)).ToList();

        public IReadOnlyList<AITool> Tools => Options?.Tools?.ToList() ?? new List<AITool>();

        public IReadOnlyList<string> ToolNames => Tools.Select(t => t.Name).ToList();

        public IReadOnlyList<FunctionResultContent> ToolResults =>
            Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
    }

    /// <summary>
    /// A hand-written in-memory <see cref="IChatClient"/>. It records every request and answers
    /// with scripted responses, in order; with none left it answers "done".
    /// </summary>
    public sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<Func<ChatRequest, ChatResponse>> _script = new Queue<Func<ChatRequest, ChatResponse>>();
        private readonly List<ChatRequest> _requests = new List<ChatRequest>();

        public FakeChatClient(string providerName = "fake-provider", string modelId = "fake-model")
        {
            Metadata = new ChatClientMetadata(providerName, null, modelId);
        }

        public ChatClientMetadata Metadata { get; }

        public IReadOnlyList<ChatRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        public ChatRequest LastRequest => Requests[Requests.Count - 1];

        public int Disposed { get; private set; }

        public static UsageDetails Usage(long input, long output) =>
            new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };

        /// <summary>Answers the next request with text.</summary>
        public FakeChatClient ThenText(string text, UsageDetails? usage = null, string? modelId = null) =>
            Then(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = usage, ModelId = modelId });

        /// <summary>Answers the next request with these content parts.</summary>
        public FakeChatClient ThenContents(params AIContent[] contents) =>
            Then(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, contents.ToList())));

        /// <summary>Answers the next request with one or more tool calls.</summary>
        public FakeChatClient ThenToolCalls(params (string CallId, string Name, IDictionary<string, object?> Args)[] calls) =>
            Then(_ => new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                calls.Select(c => (AIContent)new FunctionCallContent(c.CallId, c.Name, c.Args)).ToList()))
            {
                Usage = Usage(1, 1),
            });

        public FakeChatClient ThenToolCall(string name, IDictionary<string, object?>? args = null, string? callId = null) =>
            ThenToolCalls((callId ?? "call-" + name, name, args ?? new Dictionary<string, object?>()));

        /// <summary>Throws on the next request.</summary>
        public FakeChatClient ThenThrow(Exception error) => Then(_ => throw error);

        public FakeChatClient Then(Func<ChatRequest, ChatResponse> response)
        {
            _script.Enqueue(response);
            return this;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = new ChatRequest(messages, options, cancellationToken);
            lock (_requests)
            {
                _requests.Add(request);
            }

            var next = _script.Count > 0 ? _script.Dequeue() : (_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
            try
            {
                return Task.FromResult(next(request));
            }
            catch (Exception error)
            {
                // A real client reports a failure through the returned task, not by throwing.
                return Task.FromException<ChatResponse>(error);
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey == null && serviceType.IsInstanceOfType(Metadata) ? Metadata
            : serviceKey == null && serviceType.IsInstanceOfType(this) ? this
            : null;

        public void Dispose() => Disposed++;
    }
}
