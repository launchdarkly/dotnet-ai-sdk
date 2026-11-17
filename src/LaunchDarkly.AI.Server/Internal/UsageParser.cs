using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Normalizes a handler's raw token counts into a <see cref="TokenUsage"/>.
    /// </summary>
    internal static class UsageParser
    {
        // Tried in order; the first row whose input and output keys are both present wins. Each
        // cache column lists every accepted spelling, because an unlisted one would silently read
        // as zero. The fold always adds cache on top of input, so a handler returns either raw
        // usage with its cache fields, or an input that already includes cache without them.
        private static readonly (string Input, string Output, string[]? CacheRead, string[]? CacheCreation)[] _rows =
        {
            ("input_tokens", "output_tokens", new[] { "cache_read_input_tokens" }, new[] { "cache_creation_input_tokens" }),
            ("inputTokens", "outputTokens", new[] { "cacheReadInputTokens" }, new[] { "cacheCreationInputTokens", "cacheWriteInputTokens" }),
            ("input", "output", null, null),
        };

        /// <summary>
        /// Reads the first complete input and output pair, folds every cache spelling into input,
        /// and computes the total. Unknown or missing usage gives zeros.
        /// </summary>
        /// <param name="raw">The handler's usage, or <see langword="null"/>.</param>
        /// <returns>The normalized counts.</returns>
        internal static TokenUsage Parse(IReadOnlyDictionary<string, object?>? raw)
        {
            if (raw is null)
            {
                return new TokenUsage();
            }

            foreach (var (inputKey, outputKey, cacheReadKeys, cacheCreationKeys) in _rows)
            {
                if (!raw.ContainsKey(inputKey) || !raw.ContainsKey(outputKey))
                {
                    continue;
                }

                var uncached = NumberOrZero(raw[inputKey]);
                var cacheRead = Sum(raw, cacheReadKeys);
                var cacheCreation = Sum(raw, cacheCreationKeys);
                var input = uncached + cacheRead + cacheCreation;
                var output = NumberOrZero(raw[outputKey]);
                var hasDetails = Any(raw, cacheReadKeys) || Any(raw, cacheCreationKeys);
                return new TokenUsage
                {
                    Input = input,
                    Output = output,
                    Total = input + output,
                    InputDetails = hasDetails
                        ? new InputTokenDetails { Uncached = uncached, CacheRead = cacheRead, CacheCreation = cacheCreation }
                        : null,
                };
            }

            return new TokenUsage();
        }

        private static long Sum(IReadOnlyDictionary<string, object?> raw, string[]? keys)
        {
            long sum = 0;
            if (keys != null)
            {
                foreach (var key in keys)
                {
                    if (raw.TryGetValue(key, out var value))
                    {
                        sum += NumberOrZero(value);
                    }
                }
            }

            return sum;
        }

        private static bool Any(IReadOnlyDictionary<string, object?> raw, string[]? keys)
        {
            if (keys != null)
            {
                foreach (var key in keys)
                {
                    if (raw.ContainsKey(key))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a count from any numeric type, a numeric JSON value or a numeric string.
        /// Anything else, and a value that is not finite, is zero.
        /// </summary>
        /// <param name="value">The raw count.</param>
        /// <returns>The count, truncated to a whole number.</returns>
        internal static long NumberOrZero(object? value)
        {
            double number;
            switch (value)
            {
                case null:
                    return 0;
                case long l:
                    return l;
                case int i:
                    return i;
                case JsonElement element when element.ValueKind == JsonValueKind.Number:
                    if (element.TryGetInt64(out var whole))
                    {
                        return whole;
                    }

                    number = element.GetDouble();
                    break;
                case string text:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        return 0;
                    }

                    break;
                case bool:
                    return 0;
                case IConvertible convertible:
                    try
                    {
                        number = convertible.ToDouble(CultureInfo.InvariantCulture);
                    }
                    catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
                    {
                        return 0;
                    }

                    break;
                default:
                    return 0;
            }

            if (double.IsNaN(number) || double.IsInfinity(number) || number < long.MinValue || number > long.MaxValue)
            {
                return 0;
            }

            return (long)number;
        }
    }
}
