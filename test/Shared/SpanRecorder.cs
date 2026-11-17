using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LaunchDarkly.AI.TestHelpers
{
    /// <summary>
    /// A real in-memory listener on the SDK's ActivitySource. It records every span that starts
    /// and every span that stops, so a test can check names, parentage, attributes, events,
    /// status and that nothing was left open.
    /// </summary>
    public sealed class SpanRecorder : IDisposable
    {
        public const string SourceName = "LaunchDarkly.AI.Server";

        private readonly ActivityListener _listener;
        private readonly List<Activity> _started = new List<Activity>();
        private readonly List<Activity> _stopped = new List<Activity>();

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = a =>
                {
                    lock (_started)
                    {
                        _started.Add(a);
                    }
                },
                ActivityStopped = a =>
                {
                    lock (_stopped)
                    {
                        _stopped.Add(a);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        /// <summary>Spans that ended, in the order they ended.</summary>
        public IReadOnlyList<Activity> Finished
        {
            get
            {
                lock (_stopped)
                {
                    return _stopped.ToList();
                }
            }
        }

        /// <summary>Spans that started and never ended.</summary>
        public IReadOnlyList<Activity> Open
        {
            get
            {
                lock (_started)
                {
                    lock (_stopped)
                    {
                        return _started.Where(a => !_stopped.Contains(a)).ToList();
                    }
                }
            }
        }

        public Activity Single(string name) => Assert(Finished.Where(a => a.DisplayName == name).ToList(), name);

        public IReadOnlyList<Activity> Named(string prefix) =>
            Finished.Where(a => a.DisplayName.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        public Activity Root => Single("invoke_agent");

        private static Activity Assert(List<Activity> spans, string name)
        {
            if (spans.Count != 1)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected exactly one finished span named '{name}', found {spans.Count}.");
            }

            return spans[0];
        }

        public void Dispose() => _listener.Dispose();
    }

    public static class ActivityExtensions
    {
        public static object? Attr(this Activity activity, string key) => activity.GetTagItem(key);

        public static string? AttrString(this Activity activity, string key) => activity.GetTagItem(key)?.ToString();

        public static ActivityEvent? Event(this Activity activity, string name)
        {
            foreach (var e in activity.Events)
            {
                if (e.Name == name)
                {
                    return e;
                }
            }

            return null;
        }

        public static object? EventAttr(this ActivityEvent activityEvent, string key)
        {
            foreach (var tag in activityEvent.Tags)
            {
                if (tag.Key == key)
                {
                    return tag.Value;
                }
            }

            return null;
        }

        public static bool HasEventAttr(this ActivityEvent activityEvent, string key) =>
            activityEvent.Tags.Any(t => t.Key == key);
    }
}
