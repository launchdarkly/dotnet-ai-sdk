using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The SDK's spans: one <see cref="ActivitySource"/> named <c>LaunchDarkly.AI.Server</c>, and the
    /// helpers every handler uses to write the <c>invoke_agent</c> / <c>chat</c> / <c>execute_tool</c>
    /// tree, so each handler writes the same names, attributes and events. Nothing here throws into
    /// a run: a failure to write an attribute is dropped.
    /// </summary>
    internal static class Telemetry
    {
        /// <summary>The name of the SDK's activity source, which a listener subscribes to.</summary>
        internal const string SourceName = "LaunchDarkly.AI.Server";

        /// <summary>The root span of one handler run.</summary>
        internal const string InvokeAgent = "invoke_agent";

        /// <summary>The operation name of a model turn, and the prefix of its span name.</summary>
        internal const string Chat = "chat";

        /// <summary>The operation name of a tool call, and the prefix of its span name.</summary>
        internal const string ExecuteTool = "execute_tool";

        /// <summary>The provider name written for any Bedrock route.</summary>
        internal const string AwsBedrock = "aws.bedrock";

        /// <summary>The provider name written when the config names none.</summary>
        internal const string DefaultProvider = "openai";

        // Compact JSON with raw UTF-8 and no HTML escaping, as JSON.stringify writes it.
        private static readonly JsonSerializerOptions _compactJson = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        /// <summary>The SDK's activity source.</summary>
        internal static ActivitySource Source { get; } = new ActivitySource(SourceName, SdkInfo.Version);

        /// <summary>
        /// Starts the root span of a run in the caller's current context, so it joins the caller's
        /// trace. <see langword="null"/> when nothing listens.
        /// </summary>
        /// <param name="system">The <c>gen_ai.system</c> value of the handler.</param>
        /// <param name="provider">The <c>gen_ai.provider.name</c> value.</param>
        /// <param name="requestModel">The model id the handler sends.</param>
        /// <returns>The started span, or <see langword="null"/>.</returns>
        internal static Activity? StartInvokeAgent(string system, string provider, string requestModel)
        {
            var activity = Source.StartActivity(InvokeAgent, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag("gen_ai.operation.name", InvokeAgent);
                SetModelIdentity(activity, system, provider, requestModel);
            }

            return activity;
        }

        /// <summary>
        /// Starts a <c>chat {model}</c> span for one model turn, as a child of <paramref name="root"/>.
        /// </summary>
        /// <param name="root">The run's root span, or <see langword="null"/> when nothing listens.</param>
        /// <param name="system">The <c>gen_ai.system</c> value of the handler.</param>
        /// <param name="provider">The <c>gen_ai.provider.name</c> value.</param>
        /// <param name="requestModel">The model id the handler sends.</param>
        /// <returns>The started span, or <see langword="null"/>.</returns>
        internal static Activity? StartChat(Activity? root, string system, string provider, string requestModel)
        {
            var activity = StartChild(root, Chat + " " + requestModel, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag("gen_ai.operation.name", Chat);
                SetModelIdentity(activity, system, provider, requestModel);
            }

            return activity;
        }

        /// <summary>
        /// Starts an <c>execute_tool {name}</c> span for one tool call, as a child of
        /// <paramref name="root"/> and so a sibling of the <c>chat</c> spans.
        /// </summary>
        /// <param name="root">The run's root span, or <see langword="null"/> when nothing listens.</param>
        /// <param name="toolName">The tool name.</param>
        /// <param name="toolCallId">The provider's id for the call, when it has one.</param>
        /// <returns>The started span, or <see langword="null"/>.</returns>
        internal static Activity? StartExecuteTool(Activity? root, string toolName, string? toolCallId)
        {
            var activity = StartChild(root, ExecuteTool + " " + toolName, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag("gen_ai.operation.name", ExecuteTool);
                activity.SetTag("gen_ai.tool.name", toolName);
                if (!string.IsNullOrEmpty(toolCallId))
                {
                    activity.SetTag("gen_ai.tool.call.id", toolCallId);
                }
            }

            return activity;
        }

        /// <summary>
        /// The <c>gen_ai.provider.name</c> for a config's provider: lower-cased, <c>openai</c> when
        /// empty, and <c>aws.bedrock</c> for the Bedrock route.
        /// </summary>
        /// <param name="configProvider">The config's provider name.</param>
        /// <returns>The provider name to write.</returns>
        internal static string ProviderName(string? configProvider)
        {
            if (string.IsNullOrEmpty(configProvider))
            {
                return DefaultProvider;
            }

            var lower = configProvider!.ToLowerInvariant();
            return lower == "bedrock" ? AwsBedrock : lower;
        }

        /// <summary>
        /// Writes the attributes and the <c>feature_flag</c> event that tie a root span to its AI Config,
        /// from the SDK's <c>__ld</c> tracking entry. Without the entry only
        /// <c>launchdarkly.operation.type</c> is written.
        /// </summary>
        /// <param name="activity">The root span.</param>
        /// <param name="variables">The handler's variables.</param>
        /// <param name="captureContent">
        /// The handler's effective capture setting. When it is on and the variables carry the evaluated
        /// variation (<c>__ldValue</c>), that variation is the flag value; otherwise the variation key is.
        /// </param>
        internal static void SetConfigAttributes(Activity? activity, IReadOnlyDictionary<string, object?>? variables, bool captureContent)
        {
            if (activity == null)
            {
                return;
            }

            try
            {
                activity.SetTag("launchdarkly.operation.type", "gen_ai");
                if (variables == null || !variables.TryGetValue(Variables.LdTrackData, out var entry) || entry is not TrackData ld)
                {
                    return;
                }

                activity.SetTag("launchdarkly.config.key", ld.ConfigKey);
                activity.SetTag("launchdarkly.variation.key", ld.VariationKey);
                activity.SetTag("launchdarkly.run.id", ld.RunId);
                if (!string.IsNullOrEmpty(ld.GraphKey))
                {
                    activity.SetTag("launchdarkly.graph.key", ld.GraphKey);
                }

                var tags = new ActivityTagsCollection
                {
                    { "feature_flag.key", ld.ConfigKey },
                    { "feature_flag.provider.name", "LaunchDarkly" },
                };
                var hasVariationKey = !string.IsNullOrEmpty(ld.VariationKey);
                if (hasVariationKey)
                {
                    tags.Add("feature_flag.result.variant", ld.VariationKey);
                }

                var value = captureContent && variables.TryGetValue("__ldValue", out var raw) ? SerializeFlagValue(raw) : null;
                if (value != null)
                {
                    tags.Add("feature_flag.result.value", value);
                }
                else if (hasVariationKey)
                {
                    tags.Add("feature_flag.result.value", JsonSerializer.Serialize(ld.VariationKey, _compactJson));
                }

                activity.AddEvent(new ActivityEvent("feature_flag", tags: tags));
            }
            catch (Exception e)
            {
                SdkLog.Logger.Debug("Could not write the AI Config span attributes: {0}", e.Message);
            }
        }

        /// <summary>
        /// Writes the token counts and their OpenLLMetry aliases. Absent counts are written as zero, so
        /// the span always carries the full set.
        /// </summary>
        /// <param name="activity">The span.</param>
        /// <param name="usage">The normalized token counts.</param>
        internal static void SetUsage(Activity? activity, TokenUsage usage)
        {
            if (activity == null)
            {
                return;
            }

            activity.SetTag("gen_ai.usage.input_tokens", usage.Input);
            activity.SetTag("gen_ai.usage.output_tokens", usage.Output);
            activity.SetTag("gen_ai.usage.total_tokens", usage.Input + usage.Output);
            activity.SetTag("gen_ai.usage.cache_read.input_tokens", usage.InputDetails?.CacheRead ?? 0);
            activity.SetTag("gen_ai.usage.cache_creation.input_tokens", usage.InputDetails?.CacheCreation ?? 0);
            activity.SetTag("gen_ai.usage.prompt_tokens", usage.Input);
            activity.SetTag("gen_ai.usage.completion_tokens", usage.Output);
        }

        /// <summary>
        /// Writes what the model was given, when capture is on: one <c>gen_ai.prompt.N.role</c> /
        /// <c>.content</c> pair per message and a <c>gen_ai.content.prompt</c> event.
        /// </summary>
        /// <param name="activity">The chat span.</param>
        /// <param name="captureContent">The effective capture setting.</param>
        /// <param name="messages">The prompt as (role, text), the system prompt first when there is one.</param>
        internal static void SetPromptContent(Activity? activity, bool captureContent, IReadOnlyList<(string Role, string Text)> messages)
        {
            if (activity == null || !captureContent)
            {
                return;
            }

            var lines = new List<string>(messages.Count);
            for (var i = 0; i < messages.Count; i++)
            {
                var index = i.ToString(CultureInfo.InvariantCulture);
                activity.SetTag("gen_ai.prompt." + index + ".role", messages[i].Role);
                activity.SetTag("gen_ai.prompt." + index + ".content", messages[i].Text);
                lines.Add(messages[i].Role + ": " + messages[i].Text);
            }

            activity.AddEvent(new ActivityEvent(
                "gen_ai.content.prompt",
                tags: new ActivityTagsCollection { { "gen_ai.prompt", string.Join("\n", lines) } }));
        }

        /// <summary>
        /// Writes what the model produced, when capture is on: <c>gen_ai.completion.0.role</c> /
        /// <c>.content</c> and a <c>gen_ai.content.completion</c> event.
        /// </summary>
        /// <param name="activity">The chat span.</param>
        /// <param name="captureContent">The effective capture setting.</param>
        /// <param name="text">The model's visible output text.</param>
        internal static void SetCompletionContent(Activity? activity, bool captureContent, string text)
        {
            if (activity == null || !captureContent)
            {
                return;
            }

            activity.SetTag("gen_ai.completion.0.role", MessageRoles.Assistant);
            activity.SetTag("gen_ai.completion.0.content", text);
            activity.AddEvent(new ActivityEvent(
                "gen_ai.content.completion",
                tags: new ActivityTagsCollection { { "gen_ai.completion", text } }));
        }

        /// <summary>
        /// Writes a tool call's arguments and result, when capture is on, in the one encoding every
        /// SDK uses (see <see cref="ToolCallAttribute(object?)"/>). An absent or null value writes no
        /// attribute.
        /// </summary>
        /// <param name="activity">The tool span.</param>
        /// <param name="captureContent">The effective capture setting.</param>
        /// <param name="arguments">The arguments the model sent.</param>
        /// <param name="result">The tool's result.</param>
        internal static void SetToolCallContent(Activity? activity, bool captureContent, object? arguments, object? result)
        {
            if (activity == null || !captureContent)
            {
                return;
            }

            if (ToolCallAttribute(arguments) is string args)
            {
                activity.SetTag("gen_ai.tool.call.arguments", args);
            }

            if (ToolCallAttribute(result) is string text)
            {
                activity.SetTag("gen_ai.tool.call.result", text);
            }
        }

        /// <summary>Marks a span OK.</summary>
        /// <param name="activity">The span.</param>
        internal static void SetOk(Activity? activity) => activity?.SetStatus(ActivityStatusCode.Ok);

        /// <summary>
        /// Records <paramref name="error"/> on a span as the OpenTelemetry <c>exception</c> event and
        /// sets the status to error with its message.
        /// </summary>
        /// <param name="activity">The span.</param>
        /// <param name="error">The exception.</param>
        internal static void RecordError(Activity? activity, Exception error)
        {
            if (activity == null)
            {
                return;
            }

            try
            {
                var message = string.IsNullOrEmpty(error.Message) ? error.GetType().Name : error.Message;
                activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", error.GetType().FullName },
                    { "exception.message", error.Message },
                    { "exception.stacktrace", error.ToString() },
                }));
                activity.SetStatus(ActivityStatusCode.Error, message);
            }
            catch (Exception e)
            {
                SdkLog.Logger.Debug("Could not record an error on a span: {0}", e.Message);
            }
        }

        /// <summary>
        /// The text a tool handler's return value is sent to the model as: a string unchanged,
        /// <see langword="null"/> as an empty string, and anything else as compact JSON with non-ASCII
        /// characters not escaped. A value JSON cannot encode falls back to its <c>ToString()</c>.
        /// </summary>
        /// <param name="value">The tool's result.</param>
        /// <returns>The text.</returns>
        internal static string ToolResultString(object? value) => value switch
        {
            null => "",
            string text => text,
            _ => CompactJson(value) ?? value.ToString() ?? "",
        };

        /// <summary>
        /// The span attribute form of a tool argument or result: <see langword="null"/> (write nothing)
        /// for an absent or JSON null value, a string unchanged, and anything else as compact JSON, or
        /// its <c>ToString()</c> when JSON cannot encode it.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The attribute text, or <see langword="null"/>.</returns>
        internal static string? ToolCallAttribute(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string text:
                    return text;
                case JsonElement element when element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined:
                    return null;
                default:
                    return CompactJson(value) ?? value.ToString();
            }
        }

        private static string? CompactJson(object value)
        {
            try
            {
                return JsonSerializer.Serialize(value, value.GetType(), _compactJson);
            }
            catch (Exception e) when (e is JsonException || e is NotSupportedException || e is InvalidOperationException || e is ArgumentException)
            {
                return null;
            }
        }

        private static string? SerializeFlagValue(object? raw) => raw switch
        {
            null => null,
            JsonElement element when element.ValueKind == JsonValueKind.Undefined => null,
            JsonElement element => element.GetRawText(),
            _ => CompactJson(raw),
        };

        private static void SetModelIdentity(Activity activity, string system, string provider, string requestModel)
        {
            activity.SetTag("gen_ai.system", system);
            activity.SetTag("gen_ai.provider.name", provider);
            activity.SetTag("gen_ai.request.model", requestModel);
        }

        private static Activity? StartChild(Activity? root, string name, ActivityKind kind) =>
            root == null
                ? Source.StartActivity(name, kind)
                : Source.StartActivity(name, kind, root.Context);
    }
}
