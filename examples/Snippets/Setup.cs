using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.AI.Server.ExtensionsAI;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using Microsoft.Extensions.AI;

namespace LaunchDarkly.AI.Examples.Snippets
{
    /// <summary>The README samples for initialization, registries, handlers and telemetry.</summary>
    internal static class Setup
    {
        internal static async Task InitOptionsAsync(string sdkKey)
        {
            // snippet: init-options
            // Explicit initialization, for example to point the SDK at a Relay Proxy.
            await LdAi.InitClientAsync(new InitOptions
            {
                SdkKey = sdkKey, // when unset, LD_SDK_KEY
                BaseUri = new Uri("http://localhost:8030"),
                StreamUri = new Uri("http://localhost:8030"),
                EventsUri = new Uri("http://localhost:8030"),
                StartWaitTime = TimeSpan.FromSeconds(5),
            });
            // end-snippet
        }

        internal static async Task<ILdAiClient> BringYourOwnClientAsync(string sdkKey)
        {
            // snippet: byoc
            // Bring your own client: the application builds the LaunchDarkly client and owns its
            // configuration and readiness.
            var ldClient = new LdClient(Configuration.Builder(sdkKey).StartWaitTime(TimeSpan.FromSeconds(5)).Build());
            var aiClient = await LdAi.InitClientAsync(ldClient);
            // end-snippet
            return aiClient;
        }

        internal static Config Registry(IChatClient chatClient)
        {
            // snippet: registry
            // The application owns the registry and passes it to each config: there is no
            // process-wide registry. Tool keys match the variation's tool keys exactly.
            var registry = new Registry(
                new[] { ChatClientHandler.Create(chatClient) },
                new Dictionary<string, ToolHandler>
                {
                    ["get-user-preferences"] = new FunctionTool((arguments, cancellationToken) =>
                        Task.FromResult<object?>($"Preferences of {arguments.GetProperty("user_id").GetString()}: beach, hiking")),
                    // A provider's built-in tool. The SDK never calls it.
                    ["web-search"] = new NativeTool("WebSearch"),
                });

            var config = LdAi.Config(new ConfigArgs { Key = "my-ai-config", Registry = registry });
            // end-snippet
            return config;
        }

        internal static ProviderHandler CustomHandler()
        {
            // snippet: custom-handler
            // A handler is a function from the evaluated request to the model's output and raw usage.
            var handler = LdAi.CreateHandler(
                new ProvidesFor("Echo", HandlerModes.Messages),
                (request, cancellationToken) => Task.FromResult(new HandlerResult
                {
                    Output = $"{request.Config.Model.Name} heard: {request.UserInput}",
                    Usage = new Dictionary<string, object?> { ["input_tokens"] = 3, ["output_tokens"] = 4 },
                }));
            // end-snippet
            return handler;
        }

        internal static async Task CoreInvokeAsync(ProviderHandler handler)
        {
            // snippet: core-invoke
            var config = LdAi.Config(new ConfigArgs { Key = "my-ai-config", Handler = handler });
            var response = await config.InvokeAsync(
                "Hello",
                Context.New("user-123"),
                variables: new Dictionary<string, object?> { ["company"] = "Example Inc." });
            Console.WriteLine(response.Response);
            // end-snippet
        }

        internal static async Task InspectAsync()
        {
            // snippet: inspect-config
            // Read a variation without invoking a model. Failures are in the result, not thrown.
            var result = await LdAi.InspectConfigAsync("my-ai-config", Context.New("user-123"));
            if (result.Error != null)
            {
                Console.Error.WriteLine(result.Error.Message);
            }
            else
            {
                Console.WriteLine($"{result.Meta?.Mode} {result.Config?.Provider.Name} {result.Config?.Model.Name}");
            }
            // end-snippet
        }

        internal static ActivityListener Telemetry()
        {
            // snippet: telemetry
            // Do not record prompts, completions and tool arguments on spans (the default is to record them).
            LdAi.Configure(new SdkOptions { CaptureContent = false });

            // Without OpenTelemetry: listen to the SDK's activity source directly.
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "LaunchDarkly.AI.Server",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Console.Error.WriteLine($"{activity.DisplayName}: {activity.Duration.TotalMilliseconds:F0} ms"),
            };
            ActivitySource.AddActivityListener(listener);
            // end-snippet
            return listener;
        }
    }
}
