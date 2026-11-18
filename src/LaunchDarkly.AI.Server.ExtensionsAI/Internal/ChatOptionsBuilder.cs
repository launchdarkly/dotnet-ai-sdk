using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Internal
{
    /// <summary>
    /// Builds the <see cref="ChatOptions"/> for a request.
    /// </summary>
    /// <remarks>
    /// Every public <see cref="ChatOptions"/> property is in exactly one group:
    /// <list type="bullet">
    /// <item><b>Forwarded</b> from <c>model.parameters</c>, by snake_case key only:
    /// <c>temperature</c>, <c>max_tokens</c> (<see cref="ChatOptions.MaxOutputTokens"/>), <c>top_p</c>,
    /// <c>top_k</c>, <c>frequency_penalty</c>, <c>presence_penalty</c>, <c>seed</c>, and
    /// <c>stop_sequences</c> or <c>stop</c> (<see cref="ChatOptions.StopSequences"/>; <c>stop_sequences</c>
    /// wins). A value of the wrong JSON type, or a number out of range, is dropped.</item>
    /// <item><b>Owned</b> by the handler, never read from parameters: <see cref="ChatOptions.ModelId"/>,
    /// <see cref="ChatOptions.Instructions"/>, <see cref="ChatOptions.Tools"/>,
    /// <see cref="ChatOptions.ToolMode"/> and <see cref="ChatOptions.ResponseFormat"/>.</item>
    /// <item><b>Excluded</b>, never set: <see cref="ChatOptions.AdditionalProperties"/>,
    /// <see cref="ChatOptions.RawRepresentationFactory"/>, <see cref="ChatOptions.ConversationId"/>,
    /// <see cref="ChatOptions.ContinuationToken"/>, <see cref="ChatOptions.AllowBackgroundResponses"/>,
    /// <see cref="ChatOptions.Reasoning"/> and <see cref="ChatOptions.AllowMultipleToolCalls"/>.</item>
    /// </list>
    /// Every other parameter key is dropped, and <c>model.custom</c> is never read.
    /// </remarks>
    internal static class ChatOptionsBuilder
    {
        /// <summary>The schema name sent with the JSON response format.</summary>
        internal const string OutputSchemaName = "output";

        /// <summary>
        /// Builds the options.
        /// </summary>
        /// <param name="config">The evaluated config.</param>
        /// <param name="modelId">The model id to send.</param>
        /// <param name="tools">The tools offered, or none.</param>
        /// <returns>The options.</returns>
        internal static ChatOptions Build(AiConfig config, string modelId, IReadOnlyList<AITool> tools)
        {
            var options = new ChatOptions { ModelId = modelId };
            var parameters = config.Model.Parameters;
            if (parameters != null)
            {
                options.Temperature = Float(parameters, "temperature");
                options.MaxOutputTokens = Int(parameters, "max_tokens");
                options.TopP = Float(parameters, "top_p");
                options.TopK = Int(parameters, "top_k");
                options.FrequencyPenalty = Float(parameters, "frequency_penalty");
                options.PresencePenalty = Float(parameters, "presence_penalty");
                options.Seed = Long(parameters, "seed");
                options.StopSequences = Stops(parameters, "stop_sequences") ?? Stops(parameters, "stop");
            }

            if (tools.Count > 0)
            {
                options.Tools = tools.ToList();
            }

            if (config.OutputFormat is JsonElement schema)
            {
                options.ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, OutputSchemaName);
            }

            return options;
        }

        // A number that is not finite as a float (1e400, or 1e100 once narrowed) is dropped, like any
        // other unusable value. TryGetDouble never throws, where GetDouble can on .NET Framework.
        private static float? Float(IReadOnlyDictionary<string, JsonElement> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            {
                return null;
            }

            var narrowed = (float)number;
            return float.IsInfinity(narrowed) || float.IsNaN(narrowed) ? null : narrowed;
        }

        private static int? Int(IReadOnlyDictionary<string, JsonElement> parameters, string key) =>
            parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : null;

        private static long? Long(IReadOnlyDictionary<string, JsonElement> parameters, string key) =>
            parameters.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? number
                : null;

        // A string, or an array of strings. Anything else is dropped.
        private static IList<string>? Stops(IReadOnlyDictionary<string, JsonElement> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return new List<string> { value.GetString()! };
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var stops = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                stops.Add(item.GetString()!);
            }

            return stops;
        }
    }
}
