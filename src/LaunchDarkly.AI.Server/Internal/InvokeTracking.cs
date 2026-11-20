using System;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The events an invocation tracks: duration, success or error, and token counts, each
    /// carrying the run's <see cref="TrackData"/>. Tracking never fails the invocation.
    /// </summary>
    internal static class InvokeTracking
    {
        /// <summary>The total duration event, in milliseconds.</summary>
        internal const string DurationTotal = "$ld:ai:duration:total";

        /// <summary>The successful generation event.</summary>
        internal const string GenerationSuccess = "$ld:ai:generation:success";

        /// <summary>The failed generation event.</summary>
        internal const string GenerationError = "$ld:ai:generation:error";

        /// <summary>The total token count event.</summary>
        internal const string TokensTotal = "$ld:ai:tokens:total";

        /// <summary>The input token count event.</summary>
        internal const string TokensInput = "$ld:ai:tokens:input";

        /// <summary>The output token count event.</summary>
        internal const string TokensOutput = "$ld:ai:tokens:output";

        /// <summary>
        /// Builds the tracking data for one run of <paramref name="config"/>, with a new run ID and
        /// the pinned model config's key and version when the variation has them.
        /// </summary>
        /// <param name="config">The evaluated config.</param>
        /// <param name="meta">The variation's metadata.</param>
        /// <param name="graphKey">The graph key, for a run inside a graph.</param>
        /// <returns>The tracking data.</returns>
        internal static TrackData CreateTrackData(AiConfig config, VariationMeta meta, string? graphKey = null) => new TrackData
        {
            RunId = Guid.NewGuid().ToString(),
            ConfigKey = config.Key,
            VariationKey = meta.VariationKey ?? "",
            Version = meta.Version ?? 1,
            ModelName = config.Model.Name,
            ProviderName = config.Provider.Name,
            GraphKey = graphKey,
            ModelKey = string.IsNullOrEmpty(meta.ModelKey) ? null : meta.ModelKey,
            ModelVersion = meta.ModelVersion,
        };

        /// <summary>
        /// The event data for <paramref name="data"/>: camelCase keys, with each unset optional
        /// field left out rather than sent as null.
        /// </summary>
        /// <param name="data">The tracking data.</param>
        /// <returns>The event data.</returns>
        internal static LdValue ToEventData(TrackData data)
        {
            var builder = LdValue.BuildObject()
                .Add("runId", data.RunId)
                .Add("configKey", data.ConfigKey)
                .Add("variationKey", data.VariationKey)
                .Add("version", data.Version)
                .Add("modelName", data.ModelName)
                .Add("providerName", data.ProviderName);
            if (data.GraphKey != null)
            {
                builder.Add("graphKey", data.GraphKey);
            }

            if (data.ToolKey != null)
            {
                builder.Add("toolKey", data.ToolKey);
            }

            if (!string.IsNullOrEmpty(data.ModelKey))
            {
                builder.Add("modelKey", data.ModelKey);
            }

            if (data.ModelVersion is long modelVersion)
            {
                builder.Add("modelVersion", modelVersion);
            }

            return builder.Build();
        }

        /// <summary>
        /// Tracks a successful run: its duration, the success, and each token count above zero.
        /// </summary>
        /// <param name="client">The LaunchDarkly client.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="data">The run's tracking data.</param>
        /// <param name="duration">How long the handler took.</param>
        /// <param name="usage">The normalized token counts.</param>
        internal static void TrackSuccess(ILdClient client, Context context, TrackData data, TimeSpan duration, TokenUsage usage)
        {
            var eventData = SafeEventData(data);
            if (eventData is not LdValue payload)
            {
                return;
            }

            Track(client, DurationTotal, context, payload, duration.TotalMilliseconds);
            Track(client, GenerationSuccess, context, payload, 1);
            if (usage.Total > 0)
            {
                Track(client, TokensTotal, context, payload, usage.Total);
            }

            if (usage.Input > 0)
            {
                Track(client, TokensInput, context, payload, usage.Input);
            }

            if (usage.Output > 0)
            {
                Track(client, TokensOutput, context, payload, usage.Output);
            }
        }

        /// <summary>
        /// Tracks a failed run: its duration and the error.
        /// </summary>
        /// <param name="client">The LaunchDarkly client.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="data">The run's tracking data.</param>
        /// <param name="duration">How long the handler ran before it failed.</param>
        internal static void TrackError(ILdClient client, Context context, TrackData data, TimeSpan duration)
        {
            if (SafeEventData(data) is not LdValue payload)
            {
                return;
            }

            Track(client, DurationTotal, context, payload, duration.TotalMilliseconds);
            Track(client, GenerationError, context, payload, 1);
        }

        /// <summary>
        /// Sends one event. A failure is logged and dropped: tracking never fails the run or
        /// replaces the handler's own exception.
        /// </summary>
        /// <param name="client">The LaunchDarkly client.</param>
        /// <param name="eventName">The event key.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="data">The event data.</param>
        /// <param name="metric">The metric value.</param>
        internal static void Track(ILdClient client, string eventName, Context context, LdValue data, double metric)
        {
            try
            {
                client.Track(eventName, context, data, metric);
            }
            catch (Exception e)
            {
                SdkLog.Logger.Warn("Could not track {0}: {1}", eventName, e.Message);
            }
        }

        private static LdValue? SafeEventData(TrackData data)
        {
            try
            {
                return ToEventData(data);
            }
            catch (Exception e)
            {
                SdkLog.Logger.Warn("Could not build the AI tracking data: {0}", e.Message);
                return null;
            }
        }
    }
}
