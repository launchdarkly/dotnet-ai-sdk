using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.TestHelpers
{
    /// <summary>
    /// Builders for AI Config variations, contexts and handlers.
    /// </summary>
    public static class Fixtures
    {
        public const string ConfigKey = "test-config";

        public static readonly Context UserContext =
            Context.Builder("user-123").Kind("user").Set("email", "user@example.com").Build();

        /// <summary>
        /// An enabled variation with instructions, as JSON that the test can edit before use.
        /// </summary>
        public static JsonObject EnabledConfigJson(string provider = "OpenAI", string? mode = "messages", string model = "test-model")
        {
            var meta = new JsonObject
            {
                ["enabled"] = true,
                ["variationKey"] = "v1",
                ["version"] = 2,
            };
            if (mode != null)
            {
                meta["mode"] = mode;
            }

            return new JsonObject
            {
                ["_ldMeta"] = meta,
                ["model"] = new JsonObject { ["name"] = model },
                ["provider"] = new JsonObject { ["name"] = provider },
                ["instructions"] = "Be helpful.",
            };
        }

        /// <summary>
        /// An enabled variation, optionally edited, as the LdValue the client returns.
        /// </summary>
        public static LdValue EnabledConfig(string provider = "OpenAI", string? mode = "messages", Action<JsonObject>? edit = null)
        {
            var json = EnabledConfigJson(provider, mode);
            edit?.Invoke(json);
            return ToLdValue(json);
        }

        public static LdValue ToLdValue(JsonNode node) => LdValue.Parse(node.ToJsonString());

        public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        /// <summary>
        /// A tool definition entry for a variation's <c>tools</c> object.
        /// </summary>
        public static JsonObject ToolJson(string name, string description = "a tool") => new JsonObject
        {
            ["name"] = name,
            ["type"] = "function",
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["q"] = new JsonObject { ["type"] = "string" } },
            },
        };

        /// <summary>
        /// A parsed config, for calling a handler directly.
        /// </summary>
        public static AiConfig Config(string provider = "OpenAI", string model = "test-model") =>
            new AiConfig(new ModelConfig(model), new ProviderConfig(provider)) { Key = ConfigKey };

        public static ToolDefinition Tool(string name, string description = "a tool") =>
            new ToolDefinition(name, Json("{\"type\":\"object\",\"properties\":{\"q\":{\"type\":\"string\"}}}"))
            {
                Description = description,
            };

        /// <summary>
        /// A handler built with <see cref="LdAi.CreateHandler(ProvidesFor, Func{HandlerRequest, CancellationToken, Task{HandlerResult}})"/>
        /// that records each request and returns <paramref name="output"/>.
        /// </summary>
        public static ProviderHandler RecordingHandler(
            string provider,
            string mode,
            List<HandlerRequest> calls,
            string? output = "ok",
            IReadOnlyDictionary<string, object?>? usage = null,
            IReadOnlyList<string>? providers = null)
        {
            Func<HandlerRequest, CancellationToken, Task<HandlerResult>> fn = (request, _) =>
            {
                calls.Add(request);
                return Task.FromResult(new HandlerResult { Output = output, Usage = usage });
            };
            return providers == null
                ? LdAi.CreateHandler(new ProvidesFor(provider, mode), fn)
                : LdAi.CreateHandler(new ProvidesFor(provider, mode), fn, new CreateHandlerOptions { Providers = providers });
        }

        public static ProviderHandler RecordingHandler(string provider, string mode, string? output = "ok") =>
            RecordingHandler(provider, mode, new List<HandlerRequest>(), output);

        public static IReadOnlyDictionary<string, object?> Usage(params (string Key, object? Value)[] entries)
        {
            var d = new Dictionary<string, object?>();
            foreach (var (k, v) in entries)
            {
                d[k] = v;
            }

            return d;
        }

        /// <summary>The <c>__ld</c> tracking fixture handler span tests supply in variables.</summary>
        public static TrackData LdFixture(string variationKey = "variation-a", string? graphKey = null) => new TrackData
        {
            ConfigKey = "test-config",
            VariationKey = variationKey,
            RunId = "run-123",
            Version = 1,
            ModelName = "test-model",
            ProviderName = "TestProvider",
            ModelKey = "my-model",
            ModelVersion = 2,
            GraphKey = graphKey,
        };
    }
}
