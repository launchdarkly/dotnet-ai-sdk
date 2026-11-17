using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server.Internal;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// Runs one request on a chat client: resolves the client, builds the prompt, the tools and the
    /// options, sends the turns through the tool loop under an <c>invoke_agent</c> span, and returns
    /// the visible text and the summed usage.
    /// </summary>
    internal sealed class ChatClientRunner
    {
        /// <summary>The <c>gen_ai.system</c> value of every span this handler writes.</summary>
        internal const string GenAiSystem = "microsoft.extensions.ai";

        private readonly Func<AiConfig, CancellationToken, ValueTask<IChatClient>> _resolveClient;
        private readonly IToolLoop _toolLoop;

        internal ChatClientRunner(Func<AiConfig, CancellationToken, ValueTask<IChatClient>> resolveClient, IToolLoop toolLoop)
        {
            _resolveClient = resolveClient;
            _toolLoop = toolLoop;
        }

        /// <summary>
        /// Runs <paramref name="request"/>.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="captureContent">The handler's effective capture setting for this run.</param>
        /// <param name="cancellationToken">Cancels the run.</param>
        /// <returns>The output text and the raw usage.</returns>
        internal async Task<HandlerResult> RunAsync(HandlerRequest request, bool captureContent, CancellationToken cancellationToken)
        {
            var config = request.Config;
            var modelId = SentModelId(config);
            var provider = Telemetry.ProviderName(config.Provider.Name);
            var variables = request.Variables ?? new Dictionary<string, object?>();

            using var root = Telemetry.StartInvokeAgent(GenAiSystem, provider, modelId);
            Telemetry.SetConfigAttributes(root, variables, captureContent);
            try
            {
                var invocationConfig = modelId == config.Model.Name ? config : config with { Model = config.Model with { Name = modelId } };
                var client = await _resolveClient(invocationConfig, cancellationToken).ConfigureAwait(false)
                    ?? throw new LdAiException("The chat client factory returned no chat client.");

                var tools = OfferedTools.Select(config.Tools, request.ToolHandlers);
                var options = ChatOptionsBuilder.Build(config, modelId, tools.Declarations);
                var messages = PromptBuilder.Build(config, request.UserInput, variables, request.History);

                long input = 0, uncached = 0, cacheRead = 0, output = 0;
                string? responseModel = null;

                async Task<ChatResponse> TurnAsync(IList<ChatMessage> turnMessages, ChatOptions turnOptions, CancellationToken ct)
                {
                    using var chat = Telemetry.StartChat(root, GenAiSystem, provider, modelId);
                    Telemetry.SetPromptContent(chat, captureContent, PromptBuilder.ForSpan(turnMessages));
                    ChatResponse response;
                    try
                    {
                        response = await client.GetResponseAsync(turnMessages, turnOptions.Clone(), ct).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Telemetry.RecordError(chat, e);
                        throw;
                    }

                    var usage = TurnUsage(response.Usage);
                    input += usage.Input;
                    uncached += usage.InputDetails?.Uncached ?? usage.Input;
                    cacheRead += usage.InputDetails?.CacheRead ?? 0;
                    output += usage.Output;
                    responseModel = string.IsNullOrEmpty(response.ModelId) ? modelId : response.ModelId;

                    chat?.SetTag("gen_ai.response.model", responseModel);
                    Telemetry.SetUsage(chat, usage);
                    Telemetry.SetCompletionContent(chat, captureContent, VisibleText(response));
                    Telemetry.SetOk(chat);
                    return response;
                }

                Task<FunctionResultContent> CallToolAsync(FunctionCallContent call, CancellationToken ct) =>
                    tools.ExecuteAsync(call, root, captureContent, ct);

                var final = await _toolLoop.RunAsync(new ToolLoopRequest(messages, options, TurnAsync, CallToolAsync), cancellationToken)
                    .ConfigureAwait(false);

                root?.SetTag("gen_ai.response.model", responseModel ?? modelId);
                Telemetry.SetUsage(root, new TokenUsage { Input = input, Output = output, Total = input + output });
                Telemetry.SetOk(root);

                return new HandlerResult
                {
                    Output = VisibleText(final),
                    Usage = RawUsage(uncached, cacheRead, output),
                };
            }
            catch (Exception e)
            {
                Telemetry.RecordError(root, e);
                throw;
            }
        }

        /// <summary>
        /// The model id the request sends: the config's model name, with the Bedrock inference-profile
        /// prefix under a Bedrock provider.
        /// </summary>
        internal static string SentModelId(AiConfig config) =>
            BedrockModelId.IsBedrock(config.Provider.Name)
                ? BedrockModelId.Apply(config.Model.Name, config.Model.Region)
                : config.Model.Name;

        /// <summary>
        /// The visible text of a response: its text parts, concatenated. Reasoning and every other kind
        /// of content are left out, and a response with no text is empty.
        /// </summary>
        internal static string VisibleText(ChatResponse response)
        {
            var text = new StringBuilder();
            foreach (var message in response.Messages)
            {
                foreach (var part in message.Contents.OfType<TextContent>())
                {
                    text.Append(part.Text);
                }
            }

            return text.ToString();
        }

        // The chat client's input count includes cached tokens, so the cached part is split out of it
        // rather than added on top.
        private static TokenUsage TurnUsage(UsageDetails? usage)
        {
            if (usage == null)
            {
                return new TokenUsage();
            }

            var input = Math.Max(0, usage.InputTokenCount ?? 0);
            var cached = Math.Min(input, Math.Max(0, usage.CachedInputTokenCount ?? 0));
            var output = Math.Max(0, usage.OutputTokenCount ?? 0);
            return new TokenUsage
            {
                Input = input,
                Output = output,
                Total = input + output,
                InputDetails = usage.CachedInputTokenCount == null
                    ? null
                    : new InputTokenDetails { Uncached = input - cached, CacheRead = cached },
            };
        }

        // In the spelling the core's usage parser folds: input excludes the cache, which it adds back.
        private static IReadOnlyDictionary<string, object?> RawUsage(long uncached, long cacheRead, long output)
        {
            var usage = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input_tokens"] = uncached,
                ["output_tokens"] = output,
            };
            if (cacheRead > 0)
            {
                usage["cache_read_input_tokens"] = cacheRead;
            }

            return usage;
        }
    }
}
