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

        /// <summary>The variable that holds the evaluated variation, when content capture is on.</summary>
        internal const string LdVariation = "__ldValue";

        /// <summary>
        /// Returns a copy of <paramref name="variables"/> with <c>ldContext</c> set to
        /// <paramref name="context"/>, <c>__ld</c> set to <paramref name="trackData"/> when given,
        /// and <c>__ldValue</c> set to <paramref name="variation"/> when given. All three are
        /// SDK-owned: a caller's value under any of these keys is replaced, or removed when the SDK
        /// supplies none. The caller's dictionary is not changed.
        /// </summary>
        /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="trackData">The run's tracking data, or <see langword="null"/>.</param>
        /// <param name="variation">The evaluated variation, or <see langword="null"/> when content
        /// capture is off.</param>
        /// <returns>The handler's variables.</returns>
        internal static IReadOnlyDictionary<string, object?> WithContext(
            IReadOnlyDictionary<string, object?>? variables,
            Context context,
            TrackData? trackData = null,
            object? variation = null)
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

            if (variation != null)
            {
                merged[LdVariation] = variation;
            }
            else
            {
                merged.Remove(LdVariation);
            }

            return merged;
        }
    }
}
