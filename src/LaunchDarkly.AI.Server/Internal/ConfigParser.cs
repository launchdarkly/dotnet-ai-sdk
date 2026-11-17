using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Validates an AI Config flag variation and reads it into an <see cref="AiConfig"/> and its
    /// <see cref="VariationMeta"/>.
    /// </summary>
    internal static class ConfigParser
    {
        private static readonly HashSet<string> _roles = new HashSet<string>(StringComparer.Ordinal)
        {
            MessageRoles.System,
            MessageRoles.User,
            MessageRoles.Assistant,
        };

        /// <summary>
        /// Reads an enabled, valid variation for an invocation.
        /// </summary>
        /// <param name="key">The AI Config key.</param>
        /// <param name="variation">The variation the LaunchDarkly client returned; <see cref="LdValue.Null"/> when it returned the default.</param>
        /// <returns>The parsed config and its metadata.</returns>
        /// <exception cref="AiConfigNotFoundException">The variation is not a JSON object (the key does not exist, or evaluation fell back to the default).</exception>
        /// <exception cref="AiConfigDisabledException">The variation has no <c>_ldMeta</c>, or <c>_ldMeta.enabled</c> is not <see langword="true"/>.</exception>
        /// <exception cref="LdAiException">The variation is enabled but is not a valid AI Config.</exception>
        internal static (AiConfig Config, VariationMeta Meta) Parse(string key, LdValue variation)
        {
            var root = ToObject(key, variation);
            if (!TryGetObject(root, "_ldMeta", out var metaElement))
            {
                throw Disabled(key);
            }

            var meta = ParseMeta(metaElement);
            if (!meta.Enabled)
            {
                throw Disabled(key);
            }

            return (ParseConfig(key, root), meta);
        }

        /// <summary>
        /// Reads a variation without invoking anything, for <c>InspectConfigAsync</c>. Never
        /// throws for a missing, disabled or invalid variation: the reason is the result's error.
        /// </summary>
        /// <param name="key">The AI Config key.</param>
        /// <param name="variation">The variation the LaunchDarkly client returned.</param>
        /// <returns>The inspection result.</returns>
        internal static InspectResult Inspect(string key, LdValue variation)
        {
            try
            {
                var root = ToObject(key, variation);
                if (!TryGetObject(root, "_ldMeta", out var metaElement))
                {
                    throw NotFound(key);
                }

                var meta = ParseMeta(metaElement);
                if (!meta.Enabled)
                {
                    return new InspectResult { Enabled = false, Meta = meta };
                }

                return new InspectResult { Enabled = true, Config = ParseConfig(key, root), Meta = meta };
            }
            catch (LdAiException e)
            {
                return new InspectResult { Error = e };
            }
        }

        /// <summary>
        /// Reads the pinned model config version: an integer, an integral number such as
        /// <c>3.0</c>, or a string holding one. Anything else is absent.
        /// </summary>
        /// <param name="value">The raw <c>_ldMeta.modelVersion</c>.</param>
        /// <returns>The version, or <see langword="null"/>.</returns>
        internal static long? CoerceModelVersion(JsonElement value)
        {
            double number;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out var integer))
                    {
                        return integer;
                    }

                    number = value.GetDouble();
                    break;
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (string.IsNullOrWhiteSpace(text) ||
                        !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        return null;
                    }

                    break;
                default:
                    return null;
            }

            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Floor(number) != number ||
                number < long.MinValue || number > long.MaxValue)
            {
                return null;
            }

            return (long)number;
        }

        private static JsonElement ToObject(string key, LdValue variation)
        {
            if (variation.Type != LdValueType.Object)
            {
                throw NotFound(key);
            }

            using var document = JsonDocument.Parse(variation.ToJsonString());
            return document.RootElement.Clone();
        }

        private static VariationMeta ParseMeta(JsonElement meta)
        {
            string? modelKey = null;
            if (meta.TryGetProperty("modelKey", out var rawKey) && rawKey.ValueKind == JsonValueKind.String)
            {
                var text = rawKey.GetString();
                modelKey = string.IsNullOrEmpty(text) ? null : text;
            }

            int? version = null;
            if (meta.TryGetProperty("version", out var rawVersion) && rawVersion.ValueKind == JsonValueKind.Number &&
                rawVersion.TryGetInt32(out var v))
            {
                version = v;
            }

            return new VariationMeta
            {
                Enabled = meta.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
                VariationKey = GetString(meta, "variationKey"),
                Version = version,
                Mode = GetString(meta, "mode"),
                ModelKey = modelKey,
                ModelVersion = meta.TryGetProperty("modelVersion", out var rawModelVersion)
                    ? CoerceModelVersion(rawModelVersion)
                    : null,
            };
        }

        private static AiConfig ParseConfig(string key, JsonElement root)
        {
            if (!TryGetObject(root, "model", out var model) || GetString(model, "name") is not string modelName)
            {
                throw Invalid(key, "model.name is required and must be a string");
            }

            if (!TryGetObject(root, "provider", out var provider) || GetString(provider, "name") is not string providerName)
            {
                throw Invalid(key, "provider.name is required and must be a string");
            }

            var instructions = GetString(root, "instructions");
            var messages = ParseMessages(key, root);
            var hasClassifiers = root.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.ValueKind == JsonValueKind.Array && classifiers.GetArrayLength() > 0;
            if (instructions is null && (messages is null || messages.Count == 0) && !hasClassifiers)
            {
                throw Invalid(key, "the config must have instructions or a non-empty messages array");
            }

            JsonElement? outputFormat = null;
            if (root.TryGetProperty("outputFormat", out var rawOutputFormat))
            {
                if (rawOutputFormat.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(key, "outputFormat must be an object (JSON Schema)");
                }

                outputFormat = rawOutputFormat;
            }

            return new AiConfig(
                new ModelConfig(modelName)
                {
                    Parameters = GetDictionary(model, "parameters"),
                    Custom = GetDictionary(model, "custom"),
                },
                new ProviderConfig(providerName))
            {
                Key = key,
                Instructions = instructions,
                Messages = messages,
                Tools = ParseTools(key, root),
                JudgeConfiguration = root.TryGetProperty("judgeConfiguration", out var judges) &&
                    judges.ValueKind != JsonValueKind.Null
                    ? judges
                    : null,
                EvaluationMetricKey = GetString(root, "evaluationMetricKey"),
                OutputFormat = outputFormat,
            };
        }

        private static List<ConfigMessage>? ParseMessages(string key, JsonElement root)
        {
            if (!root.TryGetProperty("messages", out var raw) || raw.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (raw.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(key, "messages must be an array");
            }

            var messages = new List<ConfigMessage>();
            var index = 0;
            foreach (var message in raw.EnumerateArray())
            {
                var role = message.ValueKind == JsonValueKind.Object ? GetString(message, "role") : null;
                if (role is null || !_roles.Contains(role))
                {
                    throw Invalid(key, $"messages[{index}] has an invalid role");
                }

                if (GetString(message, "content") is not string content)
                {
                    throw Invalid(key, $"messages[{index}].content must be a string");
                }

                messages.Add(new ConfigMessage(role, content));
                index++;
            }

            return messages;
        }

        private static Dictionary<string, ToolDefinition>? ParseTools(string key, JsonElement root)
        {
            if (!root.TryGetProperty("tools", out var raw) || raw.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (raw.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(key, "tools must be an object");
            }

            var tools = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
            foreach (var entry in raw.EnumerateObject())
            {
                var tool = entry.Value;
                if (tool.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(key, $"tools.{entry.Name} must be an object");
                }

                if (GetString(tool, "name") is not string name)
                {
                    throw Invalid(key, $"tools.{entry.Name}.name must be a string");
                }

                if (GetString(tool, "type") != "function")
                {
                    throw Invalid(key, $"tools.{entry.Name}.type must be \"function\"");
                }

                if (!TryGetObject(tool, "parameters", out var parameters))
                {
                    throw Invalid(key, $"tools.{entry.Name}.parameters must be an object");
                }

                tools[entry.Name] = new ToolDefinition(name, parameters)
                {
                    Description = GetString(tool, "description"),
                    CustomParameters = tool.TryGetProperty("customParameters", out var custom) &&
                        custom.ValueKind != JsonValueKind.Null
                        ? custom
                        : null,
                };
            }

            return tools;
        }

        private static Dictionary<string, JsonElement>? GetDictionary(JsonElement parent, string name)
        {
            if (!TryGetObject(parent, name, out var element))
            {
                return null;
            }

            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value;
            }

            return result;
        }

        private static bool TryGetObject(JsonElement parent, string name, out JsonElement value) =>
            parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

        private static string? GetString(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static AiConfigNotFoundException NotFound(string key) =>
            new AiConfigNotFoundException(key, $"No AI Config variation was found for \"{key}\".");

        private static AiConfigDisabledException Disabled(string key) =>
            new AiConfigDisabledException(key, $"The AI Config \"{key}\" is not enabled for this context.");

        private static LdAiException Invalid(string key, string reason) =>
            new LdAiException($"Invalid AI Config \"{key}\": {reason}.");
    }
}
