using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LaunchDarkly.AI.Server
{
    /// <summary>
    /// The handler mode values a variation can select.
    /// </summary>
    public static class HandlerModes
    {
        /// <summary>
        /// A messages (chat completion) handler. The variation modes <c>completion</c> and
        /// <c>judge</c>, an absent mode and any unknown mode all select it.
        /// </summary>
        public const string Messages = "messages";

        /// <summary>
        /// An agent handler.
        /// </summary>
        public const string Agent = "agent";
    }

    /// <summary>
    /// Message role values.
    /// </summary>
    public static class MessageRoles
    {
        /// <summary>The system role.</summary>
        public const string System = "system";

        /// <summary>The user role.</summary>
        public const string User = "user";

        /// <summary>The assistant role.</summary>
        public const string Assistant = "assistant";
    }

    /// <summary>
    /// A message configured on an AI Config variation.
    /// </summary>
    public sealed record ConfigMessage
    {
        /// <summary>
        /// Creates a message.
        /// </summary>
        /// <param name="role">One of <see cref="MessageRoles"/>.</param>
        /// <param name="content">The message text, which may hold <c>{{variable}}</c> placeholders.</param>
        public ConfigMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>One of <see cref="MessageRoles"/>.</summary>
        public string Role { get; init; }

        /// <summary>The message text.</summary>
        public string Content { get; init; }
    }

    /// <summary>
    /// A prior conversation turn passed to an invocation.
    /// </summary>
    public sealed record HistoryMessage
    {
        /// <summary>
        /// Creates a history message.
        /// </summary>
        /// <param name="role">One of <see cref="MessageRoles"/>. System-role history is ignored.</param>
        /// <param name="content">The message text.</param>
        public HistoryMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>One of <see cref="MessageRoles"/>.</summary>
        public string Role { get; init; }

        /// <summary>The message text.</summary>
        public string Content { get; init; }
    }

    /// <summary>
    /// The model section of an AI Config.
    /// </summary>
    public sealed record ModelConfig
    {
        /// <summary>
        /// Creates a model section.
        /// </summary>
        /// <param name="name">The model name.</param>
        public ModelConfig(string name)
        {
            Name = name;
        }

        /// <summary>The model name.</summary>
        public string Name { get; init; }

        /// <summary>Model parameters, forwarded to a provider only through a handler's allowlist.</summary>
        public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

        /// <summary>Custom model settings. Never forwarded automatically.</summary>
        public IReadOnlyDictionary<string, JsonElement>? Custom { get; init; }
    }

    /// <summary>
    /// The provider section of an AI Config.
    /// </summary>
    public sealed record ProviderConfig
    {
        /// <summary>
        /// Creates a provider section.
        /// </summary>
        /// <param name="name">The provider name, compared exactly when routing.</param>
        public ProviderConfig(string name)
        {
            Name = name;
        }

        /// <summary>The provider name.</summary>
        public string Name { get; init; }
    }

    /// <summary>
    /// A tool attached to an AI Config variation.
    /// </summary>
    public sealed record ToolDefinition
    {
        /// <summary>
        /// Creates a tool definition.
        /// </summary>
        /// <param name="name">The tool name.</param>
        /// <param name="parameters">The JSON Schema of the tool's arguments.</param>
        public ToolDefinition(string name, JsonElement parameters)
        {
            Name = name;
            Parameters = parameters;
        }

        /// <summary>The tool name.</summary>
        public string Name { get; init; }

        /// <summary>The tool type. Always <c>function</c>.</summary>
        public string Type { get; init; } = "function";

        /// <summary>The tool description.</summary>
        public string? Description { get; init; }

        /// <summary>The JSON Schema of the tool's arguments.</summary>
        public JsonElement Parameters { get; init; }

        /// <summary>Provider-specific tool settings.</summary>
        public JsonElement? CustomParameters { get; init; }
    }

    /// <summary>
    /// A parsed AI Config variation.
    /// </summary>
    public sealed record AiConfig
    {
        /// <summary>
        /// Creates a config.
        /// </summary>
        /// <param name="model">The model section.</param>
        /// <param name="provider">The provider section.</param>
        public AiConfig(ModelConfig model, ProviderConfig provider)
        {
            Model = model;
            Provider = provider;
        }

        /// <summary>The AI Config key this variation came from.</summary>
        public string Key { get; init; } = "";

        /// <summary>The model section.</summary>
        public ModelConfig Model { get; init; }

        /// <summary>The provider section.</summary>
        public ProviderConfig Provider { get; init; }

        /// <summary>Instructions (a system prompt), which may hold <c>{{variable}}</c> placeholders.</summary>
        public string? Instructions { get; init; }

        /// <summary>Configured messages, which may hold <c>{{variable}}</c> placeholders.</summary>
        public IReadOnlyList<ConfigMessage>? Messages { get; init; }

        /// <summary>Attached tools, by tool key.</summary>
        public IReadOnlyDictionary<string, ToolDefinition>? Tools { get; init; }

        /// <summary>The raw judge configuration, when the variation has one.</summary>
        public JsonElement? JudgeConfiguration { get; init; }

        /// <summary>The evaluation metric key, when the variation has one.</summary>
        public string? EvaluationMetricKey { get; init; }

        /// <summary>The JSON Schema the response should match, when the variation sets one.</summary>
        public JsonElement? OutputFormat { get; init; }
    }

    /// <summary>
    /// The <c>_ldMeta</c> section of a variation.
    /// </summary>
    public sealed record VariationMeta
    {
        /// <summary>Whether the variation is enabled.</summary>
        public bool Enabled { get; init; }

        /// <summary>The variation key.</summary>
        public string? VariationKey { get; init; }

        /// <summary>The config version.</summary>
        public int? Version { get; init; }

        /// <summary>The raw variation mode.</summary>
        public string? Mode { get; init; }

        /// <summary>The pinned model config key, when present.</summary>
        public string? ModelKey { get; init; }

        /// <summary>The pinned model config version, when present and an integer.</summary>
        public int? ModelVersion { get; init; }
    }

    /// <summary>
    /// The split of input tokens, reported when the provider reports cache usage.
    /// </summary>
    public sealed record InputTokenDetails
    {
        /// <summary>Input tokens not served from a cache.</summary>
        public long Uncached { get; init; }

        /// <summary>Input tokens read from a cache.</summary>
        public long CacheRead { get; init; }

        /// <summary>Input tokens written to a cache.</summary>
        public long CacheCreation { get; init; }
    }

    /// <summary>
    /// Normalized token counts.
    /// </summary>
    public sealed record TokenUsage
    {
        /// <summary>Input tokens, cache tokens included.</summary>
        public long Input { get; init; }

        /// <summary>Output tokens.</summary>
        public long Output { get; init; }

        /// <summary>Input plus output.</summary>
        public long Total { get; init; }

        /// <summary>The cache split, or <see langword="null"/> when the provider reported none.</summary>
        public InputTokenDetails? InputDetails { get; init; }
    }

    /// <summary>
    /// The data sent with every tracked AI event for one invocation.
    /// </summary>
    public sealed record TrackData
    {
        /// <summary>A unique ID for this invocation.</summary>
        public string RunId { get; init; } = "";

        /// <summary>The AI Config key.</summary>
        public string ConfigKey { get; init; } = "";

        /// <summary>The variation key.</summary>
        public string VariationKey { get; init; } = "";

        /// <summary>The config version.</summary>
        public int Version { get; init; }

        /// <summary>The model name.</summary>
        public string ModelName { get; init; } = "";

        /// <summary>The provider name.</summary>
        public string ProviderName { get; init; } = "";

        /// <summary>The graph key, for an invocation inside a graph.</summary>
        public string? GraphKey { get; init; }

        /// <summary>The tool key, on a tool-call event.</summary>
        public string? ToolKey { get; init; }

        /// <summary>The pinned model config key, when present.</summary>
        public string? ModelKey { get; init; }

        /// <summary>The pinned model config version, when present.</summary>
        public int? ModelVersion { get; init; }
    }

    /// <summary>
    /// The result of an invocation.
    /// </summary>
    public sealed record ProviderResponse
    {
        /// <summary>
        /// The handler's output. A string, or, when the config sets an output format, the parsed
        /// JSON (a <see cref="JsonElement"/>) or the raw string when it does not parse.
        /// </summary>
        public object? Response { get; init; }

        /// <summary>Normalized token counts.</summary>
        public TokenUsage Usage { get; init; } = new TokenUsage();

        /// <summary>The data sent with the invocation's tracked events.</summary>
        public TrackData TrackData { get; init; } = new TrackData();
    }

    /// <summary>
    /// The result of <see cref="LdAi.InspectConfigAsync(string, LaunchDarkly.Sdk.Context, System.Threading.CancellationToken)"/>.
    /// </summary>
    public sealed record InspectResult
    {
        /// <summary>Whether the variation is enabled.</summary>
        public bool Enabled { get; init; }

        /// <summary>The parsed config, when the variation is enabled and valid.</summary>
        public AiConfig? Config { get; init; }

        /// <summary>The variation's metadata, when it has any.</summary>
        public VariationMeta? Meta { get; init; }

        /// <summary>
        /// Why the config could not be read: not found, invalid, or a client failure.
        /// <see langword="null"/> when it was read, enabled or not.
        /// </summary>
        public Exception? Error { get; init; }
    }
}
