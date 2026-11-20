using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// The example tools, keyed exactly as the LaunchDarkly flag variations name them. A key that
    /// differs from the variation's, even in case, is never offered to the model. Aliases cover
    /// both spellings a flag may use.
    /// </summary>
    internal static class Tools
    {
        /// <summary>The user-preferences tool key the offline flag attaches.</summary>
        internal const string GetUserPreferences = "get-user-preferences";

        private const string LlmsTxtUrl = "https://launchdarkly.com/docs/llms.txt";

        private static readonly HttpClient _http = CreateHttpClient();
        private static readonly Regex _htmlTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Lazy<JsonElement> _preferences = new Lazy<JsonElement>(LoadPreferences);

        /// <summary>Every example tool, by flag tool key.</summary>
        internal static IReadOnlyDictionary<string, ToolHandler> All { get; } = new Dictionary<string, ToolHandler>(StringComparer.Ordinal)
        {
            // A provider-executed tool: the SDK never calls it. Claude's agent handlers run it as
            // the built-in WebSearch tool; the IChatClient handler skips it with a warning.
            ["web-search"] = new NativeTool("WebSearch"),
            [GetUserPreferences] = new FunctionTool(GetPreferencesAsync),
            ["user-preferences-lookup"] = new FunctionTool(GetPreferencesAsync),
            ["search-ld-documentation"] = new FunctionTool(SearchDocumentationAsync),
            ["fetch-ld-documentation"] = new FunctionTool(FetchDocumentationAsync),
            ["fetch-launchdarkly-documentation"] = new FunctionTool(FetchDocumentationAsync),
        };

        /// <summary>Returns the stored preferences for <c>user_id</c>, from <c>user_preferences.json</c>.</summary>
        private static Task<object?> GetPreferencesAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var id = StringArgument(arguments, "user_id");
            object? result = id != null && _preferences.Value.TryGetProperty(id, out var preferences)
                ? JsonSerializer.Serialize(preferences)
                : $"No preferences found for user \"{id}\".";
            return Task.FromResult<object?>(result);
        }

        /// <summary>
        /// Returns up to ten LaunchDarkly documentation links whose index line contains every word of
        /// <c>query</c>. Calls the network.
        /// </summary>
        private static async Task<object?> SearchDocumentationAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var query = StringArgument(arguments, "query") ?? "";
            string index;
            try
            {
                index = await _http.GetStringAsync(new Uri(LlmsTxtUrl), cancellationToken);
            }
            catch (HttpRequestException e)
            {
                return "Search error: " + e.Message;
            }

            var terms = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var hits = index.Split('\n')
                .Where(line => line.StartsWith("- [", StringComparison.Ordinal))
                .Where(line => terms.All(term => line.ToLowerInvariant().Contains(term, StringComparison.Ordinal)))
                .Take(10)
                .ToList();
            return hits.Count == 0 ? $"No documentation results found for \"{query}\"." : string.Join("\n", hits);
        }

        /// <summary>
        /// Fetches a documentation page, preferring its Markdown form, and returns its text truncated.
        /// Calls the network.
        /// </summary>
        private static async Task<object?> FetchDocumentationAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var url = (StringArgument(arguments, "url") ?? "").TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var page))
            {
                return $"Not a URL: \"{url}\".";
            }

            try
            {
                var markdown = url.EndsWith(".md", StringComparison.Ordinal) ? page : new Uri(url + ".md");
                using var response = await _http.GetAsync(markdown, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return Truncate(await response.Content.ReadAsStringAsync(cancellationToken), 10000);
                }

                var html = await _http.GetStringAsync(page, cancellationToken);
                var text = string.Join(" ", _htmlTag.Replace(html, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return Truncate(text, 8000);
            }
            catch (HttpRequestException e)
            {
                return $"Failed to fetch \"{url}\": {e.Message}";
            }
        }

        private static string? StringArgument(JsonElement arguments, string name) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string Truncate(string text, int length) =>
            text.Length <= length ? text : text.Substring(0, length) + "\n\n[content truncated]";

        private static JsonElement LoadPreferences()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "user_preferences.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LaunchDarkly-AI-Examples/1.0");
            return client;
        }
    }
}
