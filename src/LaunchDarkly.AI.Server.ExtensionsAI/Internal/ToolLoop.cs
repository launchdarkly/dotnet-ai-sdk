using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// Sends one model turn: one <c>chat</c> span, one call to the chat client, with the turn's usage
    /// added to the run's total. The handler builds it; a tool loop only calls it.
    /// </summary>
    /// <param name="messages">The conversation so far.</param>
    /// <param name="options">The request options. The same options go on every turn.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The model's response.</returns>
    internal delegate Task<ChatResponse> ModelTurn(IList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one tool call the model requested: one <c>execute_tool</c> span, resolved only against
    /// the offered tools (anything else fails closed), with the result as text. The handler builds
    /// it; a tool loop only calls it.
    /// </summary>
    /// <param name="call">The model's tool call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result to send back to the model.</returns>
    internal delegate Task<FunctionResultContent> ToolCall(FunctionCallContent call, CancellationToken cancellationToken);

    /// <summary>
    /// What a tool loop is given for one request.
    /// </summary>
    internal sealed class ToolLoopRequest
    {
        internal ToolLoopRequest(List<ChatMessage> messages, ChatOptions options, ModelTurn turn, ToolCall callTool)
        {
            Messages = messages;
            Options = options;
            Turn = turn;
            CallTool = callTool;
        }

        /// <summary>The prompt: system message, config messages, history and input.</summary>
        internal List<ChatMessage> Messages { get; }

        /// <summary>The options for every turn, offered tools and response format included.</summary>
        internal ChatOptions Options { get; }

        /// <summary>Sends one model turn.</summary>
        internal ModelTurn Turn { get; }

        /// <summary>Runs one tool call.</summary>
        internal ToolCall CallTool { get; }
    }

    /// <summary>
    /// The seam the tool loop plugs in behind. The handler owns everything around it: the prompt, the
    /// options, the root span, each turn's <c>chat</c> span and usage (<see cref="ModelTurn"/>), and
    /// each call's <c>execute_tool</c> span, fail-closed resolution and result text
    /// (<see cref="ToolCall"/>). A loop decides only when to call the model again and what goes in
    /// the next turn's messages.
    /// </summary>
    internal interface IToolLoop
    {
        /// <summary>
        /// Runs turns until the model answers without a tool call.
        /// </summary>
        /// <param name="request">The prompt, options and the handler's turn and tool-call functions.</param>
        /// <param name="cancellationToken">Cancels the run.</param>
        /// <returns>The model's final response.</returns>
        Task<ChatResponse> RunAsync(ToolLoopRequest request, CancellationToken cancellationToken);
    }
}
