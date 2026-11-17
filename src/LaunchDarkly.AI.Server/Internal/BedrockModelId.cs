using System;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// The Bedrock cross-region inference-profile rule: the model id a handler sends to Bedrock,
    /// given the config's model name and <c>model.region</c> prefix.
    /// </summary>
    internal static class BedrockModelId
    {
        // The known inference-profile prefixes. An id that already starts with one is sent unchanged.
        private static readonly string[] _knownPrefixes =
        {
            "global.", "us.", "us-gov.", "eu.", "apac.", "au.", "jp.", "ca.", "in.",
        };

        /// <summary>
        /// Whether <paramref name="providerName"/> selects Bedrock: <c>Bedrock</c>, in any case.
        /// </summary>
        /// <param name="providerName">The config's provider name.</param>
        /// <returns><see langword="true"/> for a Bedrock provider.</returns>
        internal static bool IsBedrock(string? providerName) =>
            string.Equals(providerName, "bedrock", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Applies the prefix. The region is trimmed and loses trailing dots; when it is then empty, or
        /// the id is an ARN or already carries a known prefix (or the region's own), the id is unchanged.
        /// Otherwise the result is <c>&lt;region&gt;.&lt;id&gt;</c>.
        /// </summary>
        /// <param name="modelId">The Bedrock model id, from the config's model name.</param>
        /// <param name="region">The config's <c>model.region</c>, or <see langword="null"/>.</param>
        /// <returns>The id to send.</returns>
        internal static string Apply(string modelId, string? region)
        {
            var prefix = (region ?? "").Trim().TrimEnd('.');
            if (prefix.Length == 0 || modelId.StartsWith("arn:", StringComparison.Ordinal))
            {
                return modelId;
            }

            if (modelId.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return modelId;
            }

            foreach (var known in _knownPrefixes)
            {
                if (modelId.StartsWith(known, StringComparison.Ordinal))
                {
                    return modelId;
                }
            }

            return prefix + "." + modelId;
        }
    }
}
