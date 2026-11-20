using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Best-effort parsing of a handler's output when the config sets an output format.
    /// </summary>
    internal static class StructuredOutput
    {
        private static readonly Regex _leadingJsonFence = new Regex(@"^```json\r?\n", RegexOptions.CultureInvariant);
        private static readonly Regex _leadingFence = new Regex(@"^```\r?\n", RegexOptions.CultureInvariant);
        private static readonly Regex _trailingFence = new Regex(@"\r?\n```$", RegexOptions.CultureInvariant);
        private static readonly Regex _fencedBlock = new Regex(@"```(?:json)?\r?\n([\s\S]*?)\r?\n```", RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns the response for <paramref name="output"/>. Without an output format the
        /// output is returned as it is, or an empty string for none. With one, a string output is
        /// parsed as JSON (a <see cref="JsonElement"/>), Markdown fences included, and returned
        /// raw when it does not parse; any other output is returned as it is.
        /// </summary>
        /// <param name="output">The handler's output.</param>
        /// <param name="config">The evaluated config.</param>
        /// <returns>The response.</returns>
        internal static object? Parse(object? output, AiConfig config)
        {
            if (config.OutputFormat is null)
            {
                return output ?? "";
            }

            if (output is string text)
            {
                return ParseJsonWithPossibleFences(text) ?? (object)text;
            }

            return output;
        }

        /// <summary>
        /// Parses JSON that may be wrapped in Markdown code fences. Never throws or logs.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The parsed value, or <see langword="null"/> when the text is not JSON.</returns>
        internal static JsonElement? ParseJsonWithPossibleFences(string text)
        {
            var trimmed = text.Trim();
            if (TryParse(trimmed, out var value))
            {
                return value;
            }

            var stripped = _leadingJsonFence.Replace(trimmed, "", 1);
            stripped = _leadingFence.Replace(stripped, "", 1);
            stripped = _trailingFence.Replace(stripped, "", 1).Trim();
            if (TryParse(stripped, out value))
            {
                return value;
            }

            // A fenced block after some preamble.
            var match = _fencedBlock.Match(trimmed);
            if (match.Success && TryParse(match.Groups[1].Value.Trim(), out value))
            {
                return value;
            }

            return null;
        }

        private static bool TryParse(string text, out JsonElement value)
        {
            value = default;
            if (text.Length == 0)
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                value = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
