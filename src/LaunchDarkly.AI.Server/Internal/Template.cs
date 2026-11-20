using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.Server.Internal
{
    /// <summary>
    /// Renders <c>{{name}}</c> and <c>{{a.b}}</c> placeholders from a variables dictionary.
    /// </summary>
    internal static class Template
    {
        private static readonly Regex _placeholder = new Regex(@"\{\{([A-Za-z0-9_.]+)\}\}", RegexOptions.CultureInvariant);

        /// <summary>
        /// Replaces each placeholder whose path resolves to a non-null value. A placeholder that
        /// does not resolve is left as it is.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="variables">The values, or <see langword="null"/>.</param>
        /// <returns>The rendered text.</returns>
        internal static string Render(string template, IReadOnlyDictionary<string, object?>? variables)
        {
            if (variables is null || variables.Count == 0 || template.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                return template;
            }

            return _placeholder.Replace(template, match =>
            {
                object? current = variables;
                foreach (var segment in match.Groups[1].Value.Split('.'))
                {
                    if (!TryGetMember(current, segment, out current))
                    {
                        return match.Value;
                    }
                }

                return ToText(current) ?? match.Value;
            });
        }

        private static bool TryGetMember(object? value, string name, out object? member)
        {
            member = null;
            switch (value)
            {
                case IReadOnlyDictionary<string, object?> readOnly:
                    return readOnly.TryGetValue(name, out member);
                case IDictionary<string, object?> dictionary:
                    return dictionary.TryGetValue(name, out member);
                case IDictionary legacy:
                    if (legacy.Contains(name))
                    {
                        member = legacy[name];
                        return true;
                    }

                    return false;
                case JsonElement element:
                    if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property))
                    {
                        member = property;
                        return true;
                    }

                    return false;
                case JsonObject node:
                    if (node.TryGetPropertyValue(name, out var child))
                    {
                        member = child;
                        return true;
                    }

                    return false;
                case LdValue ldValue:
                    if (ldValue.Type == LdValueType.Object && ldValue.Dictionary.TryGetValue(name, out var item))
                    {
                        member = item;
                        return true;
                    }

                    return false;
                case Context context:
                    return TryGetContextMember(context, name, out member);
                default:
                    return false;
            }
        }

        private static bool TryGetContextMember(Context context, string name, out object? member)
        {
            member = null;

            // A multi-kind context exposes each of its contexts under its kind name.
            if (context.Multiple && context.TryGetContextByKind(ContextKind.Of(name), out var single))
            {
                member = single;
                return true;
            }

            var value = context.GetValue(name);
            if (value.IsNull)
            {
                return false;
            }

            member = value;
            return true;
        }

        private static string? ToText(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string text:
                    return text;
                case bool flag:
                    return flag ? "true" : "false";
                case JsonElement element:
                    return element.ValueKind switch
                    {
                        JsonValueKind.Null or JsonValueKind.Undefined => null,
                        JsonValueKind.String => element.GetString(),
                        _ => element.GetRawText(),
                    };
                case JsonNode node:
                    return node is JsonValue scalar && scalar.TryGetValue<string>(out var s) ? s : node.ToJsonString();
                case LdValue ldValue:
                    return ldValue.Type switch
                    {
                        LdValueType.Null => null,
                        LdValueType.String => ldValue.AsString,
                        _ => ldValue.ToJsonString(),
                    };
                case Context context:
                    return context.Key;
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }
    }
}
