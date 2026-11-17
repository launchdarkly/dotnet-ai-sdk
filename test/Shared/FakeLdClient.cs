using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Migrations;

namespace LaunchDarkly.AI.TestHelpers
{
    /// <summary>
    /// One recorded <c>Track</c> call.
    /// </summary>
    public sealed record TrackedEvent(string Name, Context Context, LdValue Data, double? Metric)
    {
        /// <summary>The event data as JSON, for assertions.</summary>
        public JsonElement DataJson => JsonDocument.Parse(Data.ToJsonString()).RootElement.Clone();

        /// <summary>A string field of the data, or null when absent.</summary>
        public string? DataString(string key) =>
            DataJson.ValueKind == JsonValueKind.Object && DataJson.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        /// <summary>Whether the data has the key at all.</summary>
        public bool HasData(string key) =>
            DataJson.ValueKind == JsonValueKind.Object && DataJson.TryGetProperty(key, out _);
    }

    /// <summary>
    /// A hand-written in-memory double of the LaunchDarkly server-side client. It serves JSON
    /// variations from a dictionary and records every evaluation and track call.
    /// </summary>
    public sealed class FakeLdClient : ILdClient
    {
        private readonly object _lock = new object();
        private readonly List<TrackedEvent> _tracks = new List<TrackedEvent>();
        private readonly List<(string Key, Context Context)> _evaluations = new List<(string, Context)>();

        /// <summary>Variations by flag key. A missing key returns the caller's default.</summary>
        public Dictionary<string, LdValue> Variations { get; } = new Dictionary<string, LdValue>();

        /// <summary>When set, every variation call throws it.</summary>
        public Exception? VariationException { get; set; }

        /// <summary>When true, every track call throws.</summary>
        public bool TrackThrows { get; set; }

        /// <summary>When true, Flush throws.</summary>
        public bool FlushThrows { get; set; }

        /// <summary>What Initialized reports.</summary>
        public bool IsInitialized { get; set; } = true;

        /// <summary>The SDK key this client was built with, when the test factory built it.</summary>
        public string? SdkKey { get; set; }

        /// <summary>The options this client was built with, when the test factory built it.</summary>
        public LaunchDarkly.AI.Server.InitOptions? BuiltWith { get; set; }

        /// <summary>Number of Flush calls.</summary>
        public int Flushes { get; private set; }

        /// <summary>Number of Dispose (close) calls.</summary>
        public int Closes { get; private set; }

        /// <summary>Every track call, in order.</summary>
        public IReadOnlyList<TrackedEvent> Tracks
        {
            get
            {
                lock (_lock)
                {
                    return _tracks.ToList();
                }
            }
        }

        /// <summary>Every JSON evaluation, in order.</summary>
        public IReadOnlyList<(string Key, Context Context)> Evaluations
        {
            get
            {
                lock (_lock)
                {
                    return _evaluations.ToList();
                }
            }
        }

        /// <summary>The track calls with this event name.</summary>
        public IReadOnlyList<TrackedEvent> Events(string name) => Tracks.Where(t => t.Name == name).ToList();

        /// <summary>The track calls whose name starts with this prefix.</summary>
        public IReadOnlyList<TrackedEvent> EventsStartingWith(string prefix) =>
            Tracks.Where(t => t.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        /// <summary>Forgets every recorded track call.</summary>
        public void ClearTracks()
        {
            lock (_lock)
            {
                _tracks.Clear();
            }
        }

        public bool Initialized => IsInitialized;

        public IDataSourceStatusProvider DataSourceStatusProvider => throw new NotSupportedException();

        public IDataStoreStatusProvider DataStoreStatusProvider => throw new NotSupportedException();

        public IBigSegmentStoreStatusProvider BigSegmentStoreStatusProvider => throw new NotSupportedException();

        public IFlagTracker FlagTracker => throw new NotSupportedException();

        public LdValue JsonVariation(string key, Context context, LdValue defaultValue)
        {
            lock (_lock)
            {
                _evaluations.Add((key, context));
            }

            if (VariationException != null)
            {
                throw VariationException;
            }

            return Variations.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public EvaluationDetail<LdValue> JsonVariationDetail(string key, Context context, LdValue defaultValue) =>
            new EvaluationDetail<LdValue>(JsonVariation(key, context, defaultValue), null, EvaluationReason.FallthroughReason);

        public void Track(string name, Context context) => Record(name, context, LdValue.Null, null);

        public void Track(string name, Context context, LdValue data) => Record(name, context, data, null);

        public void Track(string name, Context context, LdValue data, double metricValue) => Record(name, context, data, metricValue);

        private void Record(string name, Context context, LdValue data, double? metric)
        {
            if (TrackThrows)
            {
                throw new InvalidOperationException("track exploded");
            }

            lock (_lock)
            {
                _tracks.Add(new TrackedEvent(name, context, data, metric));
            }
        }

        public void Flush()
        {
            Flushes++;
            if (FlushThrows)
            {
                throw new InvalidOperationException("flush exploded");
            }
        }

        public bool FlushAndWait(TimeSpan timeout)
        {
            Flush();
            return true;
        }

        public void Dispose() => Closes++;

        public bool IsOffline() => false;

        public Logger GetLogger() => Logs.None.Logger("");

        public bool BoolVariation(string key, Context context, bool defaultValue = false) => defaultValue;

        public EvaluationDetail<bool> BoolVariationDetail(string key, Context context, bool defaultValue) =>
            new EvaluationDetail<bool>(defaultValue, null, EvaluationReason.FallthroughReason);

        public int IntVariation(string key, Context context, int defaultValue) => defaultValue;

        public EvaluationDetail<int> IntVariationDetail(string key, Context context, int defaultValue) =>
            new EvaluationDetail<int>(defaultValue, null, EvaluationReason.FallthroughReason);

        public float FloatVariation(string key, Context context, float defaultValue) => defaultValue;

        public EvaluationDetail<float> FloatVariationDetail(string key, Context context, float defaultValue) =>
            new EvaluationDetail<float>(defaultValue, null, EvaluationReason.FallthroughReason);

        public double DoubleVariation(string key, Context context, double defaultValue) => defaultValue;

        public EvaluationDetail<double> DoubleVariationDetail(string key, Context context, double defaultValue) =>
            new EvaluationDetail<double>(defaultValue, null, EvaluationReason.FallthroughReason);

        public string StringVariation(string key, Context context, string defaultValue) => defaultValue;

        public EvaluationDetail<string> StringVariationDetail(string key, Context context, string defaultValue) =>
            new EvaluationDetail<string>(defaultValue, null, EvaluationReason.FallthroughReason);

        public MigrationVariation MigrationVariation(string key, Context context, MigrationStage defaultStage) =>
            throw new NotSupportedException();

        public void TrackMigration(MigrationOpTracker tracker) => throw new NotSupportedException();

        public void Identify(Context context)
        {
        }

        public FeatureFlagsState AllFlagsState(Context context, params FlagsStateOption[] options) =>
            throw new NotSupportedException();

        public string SecureModeHash(Context context) => "";
    }
}
