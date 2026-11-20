using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Integrations;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// The AI Config the examples evaluate when no <c>LD_SDK_KEY</c> is set, served by the
    /// LaunchDarkly client's test data source. Any other key has no variation, which is the
    /// failure case.
    /// </summary>
    internal static class OfflineFlags
    {
        /// <summary>The key of the offline AI Config.</summary>
        internal const string DocumentationSummarizer = "launch-darkly-documentation-summarizer";

        // A messages-mode variation: a system message with a context placeholder, model parameters
        // and the user-preferences tool, keyed as Tools.All keys it.
        private const string DocumentationSummarizerVariation = """
            {
              "_ldMeta": { "enabled": true, "variationKey": "offline", "version": 1, "mode": "messages" },
              "model": { "name": "scripted-model", "parameters": { "temperature": 0.2, "max_tokens": 512 } },
              "provider": { "name": "OpenAI" },
              "messages": [
                {
                  "role": "system",
                  "content": "You answer questions about LaunchDarkly for {{ldContext.key}}. Use get-user-preferences to personalize the answer."
                }
              ],
              "tools": {
                "get-user-preferences": {
                  "name": "get-user-preferences",
                  "type": "function",
                  "description": "Looks up a user's stored travel, leisure and food preferences.",
                  "parameters": {
                    "type": "object",
                    "properties": { "user_id": { "type": "string" } },
                    "required": ["user_id"]
                  }
                }
              }
            }
            """;

        /// <summary>Adds the offline AI Configs to <paramref name="testData"/>.</summary>
        internal static void AddTo(TestData testData) =>
            testData.Update(testData.Flag(DocumentationSummarizer).ValueForAll(LdValue.Parse(DocumentationSummarizerVariation)));
    }

    /// <summary>
    /// An in-memory <see cref="IChatClient"/> with a fixed script, so the examples run with no
    /// network or API key. When the request offers the user-preferences tool and no tool has run, it
    /// calls that tool. Otherwise it answers with text built from the request: the latest user
    /// turn, the first earlier user turn when there is history, and the tool result.
    /// </summary>
    internal sealed class ScriptedChatClient : IChatClient
    {
        private const string PreferencesUser = "user-123";

        private readonly ChatClientMetadata _metadata = new ChatClientMetadata("scripted", null, "scripted-model");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var conversation = messages.ToList();
            var toolResult = conversation.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
            var offersPreferences = options?.Tools?.Any(t => t.Name == Tools.GetUserPreferences) == true;

            ChatMessage reply;
            if (toolResult == null && offersPreferences)
            {
                reply = new ChatMessage(ChatRole.Assistant, new List<AIContent>
                {
                    new FunctionCallContent("call-1", Tools.GetUserPreferences, new Dictionary<string, object?> { ["user_id"] = PreferencesUser }),
                });
            }
            else
            {
                reply = new ChatMessage(ChatRole.Assistant, Answer(conversation, toolResult));
            }

            var input = conversation.Sum(m => WordCount(m.Text));
            var output = Math.Max(1, WordCount(reply.Text));
            return Task.FromResult(new ChatResponse(reply)
            {
                ModelId = options?.ModelId ?? _metadata.DefaultModelId,
                Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey != null)
            {
                return null;
            }

            if (serviceType == typeof(ChatClientMetadata))
            {
                return _metadata;
            }

            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }

        private static string Answer(IReadOnlyList<ChatMessage> conversation, FunctionResultContent? toolResult)
        {
            var userTurns = conversation.Where(m => m.Role == ChatRole.User).Select(m => m.Text).ToList();
            var answer = $"(scripted model) You asked: \"{userTurns.LastOrDefault()}\".";
            if (userTurns.Count > 1)
            {
                answer += $" Earlier you said: \"{userTurns[0]}\".";
            }

            if (toolResult != null)
            {
                answer += $" Stored preferences for {PreferencesUser}: {toolResult.Result}";
            }

            return answer;
        }

        private static int WordCount(string? text) =>
            string.IsNullOrEmpty(text) ? 0 : text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
