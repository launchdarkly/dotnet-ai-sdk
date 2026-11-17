using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Builds the variables a handler receives from the caller's.
    /// </summary>
    internal static class Variables
    {
        /// <summary>The variable that holds the evaluation context.</summary>
        internal const string LdContext = "ldContext";

        /// <summary>The variable that holds the run's tracking data.</summary>
        internal const string LdTrackData = "__ld";

        /// <summary>
        /// Returns a copy of <paramref name="variables"/> with <c>ldContext</c> set to
        /// <paramref name="context"/> and, when given, <c>__ld</c> set to
        /// <paramref name="trackData"/>. Both are SDK-owned, so a caller's value under either key
        /// is replaced. The caller's dictionary is not changed.
        /// </summary>
        /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="trackData">The run's tracking data, or <see langword="null"/>.</param>
        /// <returns>The handler's variables.</returns>
        internal static IReadOnlyDictionary<string, object?> WithContext(
            IReadOnlyDictionary<string, object?>? variables,
            Context context,
            TrackData? trackData = null)
        {
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (variables != null)
            {
                foreach (var entry in variables)
                {
                    merged[entry.Key] = entry.Value;
                }
            }

            merged[LdContext] = context;
            if (trackData != null)
            {
                merged[LdTrackData] = trackData;
            }
            else
            {
                merged.Remove(LdTrackData);
            }

            return merged;
        }
    }
}
