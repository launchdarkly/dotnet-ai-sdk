using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// The tool loop, on Microsoft.Extensions.AI's <see cref="FunctionInvokingChatClient"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The function-invoking client runs the loop: it calls the model, runs the tool calls in the
    /// response one after another, sends the results back, and stops when the model answers without
    /// a tool call. Underneath it, <see cref="TurnClient"/> sends each model turn through the handler's
    /// <see cref="ModelTurn"/> (the <c>chat</c> span and usage), and its function invoker runs each
    /// call through the handler's <see cref="ToolCall"/> (the <c>execute_tool</c> span, fail-closed
    /// resolution and the result text).
    /// </para>
    /// <para>It is configured so that:</para>
    /// <list type="bullet">
    /// <item>a tool's exception ends the run and reaches the caller unchanged
    /// (<see cref="FunctionInvokingChatClient.MaximumConsecutiveErrorsPerRequest"/> is 0), instead of
    /// going back to the model as an error result;</item>
    /// <item>calls in one response run in order (<see cref="FunctionInvokingChatClient.AllowConcurrentInvocation"/>
    /// is off);</item>
    /// <item>a call to a tool that was not offered ends the loop before anything runs
    /// (<see cref="FunctionInvokingChatClient.TerminateOnUnknownCalls"/>), and the loop then throws the
    /// fail-closed error naming it, instead of answering the model "not found".</item>
    /// </list>
    /// <para>
    /// It writes no spans of its own. The function-invoking client would add an <c>execute_tool</c> span on
    /// the source of a current <c>invoke_agent</c> span, which is the SDK's own, so the loop runs with no
    /// current activity, and <see cref="TurnClient"/> offers it no activity source. The handler's spans
    /// are parented explicitly, so they are unaffected.
    /// </para>
    /// </remarks>
    internal sealed class FunctionInvokingToolLoop : IToolLoop
    {
        /// <summary>The one instance. The loop keeps no state between runs.</summary>
        internal static readonly FunctionInvokingToolLoop Instance = new FunctionInvokingToolLoop();

        private FunctionInvokingToolLoop()
        {
        }

        /// <inheritdoc/>
        public async Task<ChatResponse> RunAsync(ToolLoopRequest request, CancellationToken cancellationToken)
        {
            // An AsyncLocal change, so it is undone when this method returns to the handler.
            Activity.Current = null;

            var turns = new TurnClient(request.Turn);
            using var client = new FunctionInvokingChatClient(turns)
            {
                AllowConcurrentInvocation = false,
                MaximumConsecutiveErrorsPerRequest = 0,
                TerminateOnUnknownCalls = true,
                IncludeDetailedErrors = false,
                FunctionInvoker = async (context, ct) =>
                    (await request.CallTool(context.CallContent, ct).ConfigureAwait(false)).Result,
            };

            var response = await client.GetResponseAsync(request.Messages, request.Options, cancellationToken).ConfigureAwait(false);

            // The client marks every call it ran as informational, so a call that is left means it stopped
            // on a tool that was not offered, before running any call in that response. The error names
            // that tool, not an offered call listed before it.
            var unhandled = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .Where(c => !c.InformationalOnly)
                .ToList();
            if (unhandled.Count > 0)
            {
                var offered = new HashSet<string>(
                    (request.Options.Tools ?? Enumerable.Empty<AITool>()).Select(t => t.Name),
                    StringComparer.Ordinal);
                var culprit = unhandled.FirstOrDefault(c => !offered.Contains(c.Name)) ?? unhandled[0];
                throw OfferedTools.NotOffered(culprit.Name);
            }

            return turns.LastTurn
                ?? throw new InvalidOperationException("The tool loop finished without a model turn.");
        }

        /// <summary>
        /// The chat client under the function-invoking client: each request is one model turn through the
        /// handler. It exposes no services, so the function-invoking client finds no activity source and
        /// writes no spans.
        /// </summary>
        private sealed class TurnClient : IChatClient
        {
            private readonly ModelTurn _turn;

            internal TurnClient(ModelTurn turn)
            {
                _turn = turn;
            }

            /// <summary>
            /// The last turn on its own, whose text is the run's output. A copy, because the function-invoking
            /// client replaces the last response's messages with the whole run's before it returns it.
            /// </summary>
            internal ChatResponse? LastTurn { get; private set; }

            public async Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var response = await _turn(messages.ToList(), options ?? new ChatOptions(), cancellationToken).ConfigureAwait(false);
                LastTurn = new ChatResponse(response.Messages.ToList())
                {
                    ModelId = response.ModelId,
                    FinishReason = response.FinishReason,
                };
                return response;
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("Streaming is not supported by this handler.");

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}
