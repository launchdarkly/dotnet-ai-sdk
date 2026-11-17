using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.AI.Server.Internal;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// Builds the messages a request sends: the system prompt from the config, then the config's
    /// conversation messages, then the history, then the user's input when it is appended.
    /// </summary>
    internal static class PromptBuilder
    {
        /// <summary>
        /// Builds the prompt.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>The config's messages win over its instructions; the instructions are the fallback when
        /// there are no messages. System-role messages are joined into one system message.</item>
        /// <item>Placeholders in the instructions and messages are resolved from the variables; unknown
        /// ones are kept.</item>
        /// <item>System-role history is dropped.</item>
        /// <item>Without history, the input is appended unless the last conversation message is a user
        /// turn, which already carries it. With history, a non-empty input is always appended, and an
        /// empty one only when the last turn is not a user turn.</item>
        /// <item>An appended input that is absent is sent as an empty string, never as no content.</item>
        /// </list>
        /// </remarks>
        /// <param name="config">The evaluated config.</param>
        /// <param name="userInput">The user's input.</param>
        /// <param name="variables">Template variables.</param>
        /// <param name="history">Prior turns.</param>
        /// <returns>The messages, the system message first when there is one.</returns>
        internal static List<ChatMessage> Build(
            AiConfig config,
            string? userInput,
            IReadOnlyDictionary<string, object?> variables,
            IReadOnlyList<HistoryMessage>? history)
        {
            var messages = new List<ChatMessage>();
            var turns = new List<(string Role, string Text)>();

            if (config.Messages != null && config.Messages.Count > 0)
            {
                var system = config.Messages.Where(m => m.Role == MessageRoles.System).Select(m => m.Content).ToList();
                if (system.Count > 0)
                {
                    messages.Add(Text(ChatRole.System, Template.Render(string.Join("\n", system), variables)));
                }

                foreach (var message in config.Messages)
                {
                    if (message.Role == MessageRoles.User || message.Role == MessageRoles.Assistant)
                    {
                        turns.Add((message.Role, Template.Render(message.Content, variables)));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(config.Instructions))
            {
                messages.Add(Text(ChatRole.System, Template.Render(config.Instructions!, variables)));
            }

            var input = userInput ?? "";
            bool append;
            if (history != null && history.Count > 0)
            {
                foreach (var turn in history)
                {
                    if (turn.Role == MessageRoles.User || turn.Role == MessageRoles.Assistant)
                    {
                        turns.Add((turn.Role, turn.Content ?? ""));
                    }
                }

                append = input.Length > 0 || !EndsWithUser(turns);
            }
            else
            {
                append = !EndsWithUser(turns);
            }

            if (append)
            {
                turns.Add((MessageRoles.User, input));
            }

            foreach (var (role, text) in turns)
            {
                messages.Add(Text(role == MessageRoles.User ? ChatRole.User : ChatRole.Assistant, text));
            }

            return messages;
        }

        /// <summary>The messages as (role, text) for the span's prompt attributes.</summary>
        /// <param name="messages">The messages sent.</param>
        /// <returns>One entry per message.</returns>
        internal static IReadOnlyList<(string Role, string Text)> ForSpan(IEnumerable<ChatMessage> messages) =>
            messages.Select(m => (m.Role.Value, m.Text)).ToList();

        private static bool EndsWithUser(List<(string Role, string Text)> turns) =>
            turns.Count > 0 && turns[turns.Count - 1].Role == MessageRoles.User;

        // Always one text part, so an empty input is sent as "" and never as a message with no content.
        private static ChatMessage Text(ChatRole role, string text) =>
            new ChatMessage(role, new List<AIContent> { new TextContent(text) });
    }
}
