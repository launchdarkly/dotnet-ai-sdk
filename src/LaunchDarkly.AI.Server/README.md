# LaunchDarkly.AI.Server

The core of the LaunchDarkly AI SDK for .NET (server-side).

> [!CAUTION]
> **Pre-1.0 working version, not yet published to NuGet; build from source.**
> The API may change at any point before 1.0. The first NuGet release will be 1.0.

This package evaluates [AI Configs](https://docs.launchdarkly.com/home/ai-configs) for a LaunchDarkly context, runs them through a provider handler, and tracks the result (duration, tokens, success or error, tool calls) as LaunchDarkly events and `System.Diagnostics` activities. It calls no model itself: a handler does, such as the `IChatClient` handler in [`LaunchDarkly.AI.Server.ExtensionsAI`](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/src/LaunchDarkly.AI.Server.ExtensionsAI).

## Install

Build the packages from source (see the [repository README](https://github.com/launchdarkly/dotnet-ai-sdk#building-from-source)), then add the package from the local feed:

```bash
dotnet add package LaunchDarkly.AI.Server --source /path/to/dotnet-ai-sdk/nupkgs
```

Most applications add `LaunchDarkly.AI.Server.ExtensionsAI` instead, which brings this package in.

## Handlers and `ProvidesFor`

This package is not a handler. It defines the handler contract: a handler declares the provider and mode it serves as a `ProvidesFor` pair, and `InvokeAsync` picks the handler for the evaluated variation's `provider.name` and `_ldMeta.mode`:

- The mode is `agent` when `_ldMeta.mode` is `agent`, and `messages` for every other value (`completion`, `judge`, none).
- A handler that names the provider exactly wins. Otherwise a wildcard handler (`ProvidesFor.Wildcard`, `"*"`) whose `Providers` list holds the provider wins, the shortest list first, then the earlier handler. Otherwise the first wildcard with no list. Otherwise `NoHandlerException`.

Write your own handler with `LdAi.CreateHandler`:

<!-- snippet: custom-handler -->
```csharp
// A handler is a function from the evaluated request to the model's output and raw usage.
var handler = LdAi.CreateHandler(
    new ProvidesFor("Echo", HandlerModes.Messages),
    (request, cancellationToken) => Task.FromResult(new HandlerResult
    {
        Output = $"{request.Config.Model.Name} heard: {request.UserInput}",
        Usage = new Dictionary<string, object?> { ["input_tokens"] = 3, ["output_tokens"] = 4 },
    }));
```

## Examples

One handler for every variation, with the SDK initialized from `LD_SDK_KEY` on the first call:

<!-- snippet: quick-start -->
```csharp
using System;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.AI.Server.ExtensionsAI;
using LaunchDarkly.Sdk;
using Microsoft.Extensions.AI;

// chatClient is any Microsoft.Extensions.AI IChatClient (OpenAI, Azure OpenAI, Ollama, ...).
var config = LdAi.Config(new ConfigArgs
{
    Key = "my-ai-config",
    Handler = ChatClientHandler.Create(chatClient),
});

// The first invocation initializes the SDK from the LD_SDK_KEY environment variable.
var response = await config.InvokeAsync("What is a feature flag?", Context.New("user-123"));
Console.WriteLine(response.Response);
Console.WriteLine($"{response.Usage.Total} tokens, run {response.TrackData.RunId}");

// On application exit: flush events and close the LaunchDarkly client.
await LdAi.ShutdownAsync();
```

Several handlers, one per provider:

<!-- snippet: multi-provider -->
```csharp
// One handler per provider. The variation's provider.name selects the handler,
// so changing the provider in LaunchDarkly changes the client that runs.
var config = LdAi.Config(new ConfigArgs
{
    Key = "my-ai-config",
    Handlers = new[]
    {
        ChatClientHandler.Create(openAiClient, new ChatClientHandlerOptions { Providers = new[] { "OpenAI" } }),
        ChatClientHandler.Create(anthropicClient, new ChatClientHandlerOptions { Providers = new[] { "Anthropic" } }),
    },
});

var response = await config.InvokeAsync("Summarize our rollout policy.", Context.New("user-123"));
```

A registry of handlers and tools that your application owns:

<!-- snippet: registry -->
```csharp
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
```

Explicit initialization, or your own LaunchDarkly client:

<!-- snippet: init-options -->
```csharp
// Explicit initialization, for example to point the SDK at a Relay Proxy.
await LdAi.InitClientAsync(new InitOptions
{
    SdkKey = sdkKey, // when unset, LD_SDK_KEY
    BaseUri = new Uri("http://localhost:8030"),
    StreamUri = new Uri("http://localhost:8030"),
    EventsUri = new Uri("http://localhost:8030"),
    StartWaitTime = TimeSpan.FromSeconds(5),
});
```

<!-- snippet: byoc -->
```csharp
// Bring your own client: the application builds the LaunchDarkly client and owns its
// configuration and readiness.
var ldClient = new LdClient(Configuration.Builder(sdkKey).StartWaitTime(TimeSpan.FromSeconds(5)).Build());
var aiClient = await LdAi.InitClientAsync(ldClient);
```

Read a variation without calling a model:

<!-- snippet: inspect-config -->
```csharp
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
```

The [examples](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/examples) run these end to end.

## Environment variables

| Variable | Required | Description |
|---|---|---|
| `LD_SDK_KEY` | When the SDK initializes itself and `InitOptions.SdkKey` is unset | The server-side SDK key. Not read when you pass your own `LdClient`. |

The package reads no other environment variables. Base URIs and the start wait time are set in `InitOptions`.

## API reference

All public types are in the `LaunchDarkly.AI.Server` namespace. The XML documentation on each member is the full reference, and `make docs` builds the API docs from it.

**`LdAi`**, the static entry point:

| Member | Description |
|---|---|
| `Task<ILdAiClient> InitClientAsync()`<br>`InitClientAsync(InitOptions options[, CancellationToken])` | Initializes the process-wide client from an SDK key (`InitOptions.SdkKey`, else `LD_SDK_KEY`), or returns the existing one. Concurrent first calls share one attempt; a failed attempt is not cached. Throws `LdAiException` with no key or when the LaunchDarkly client does not initialize. |
| `Task<ILdAiClient> InitClientAsync(ILdClient client[, CancellationToken])` | Uses a LaunchDarkly client the application built, replacing the current one. |
| `ILdAiClient GetClient()` | The process-wide client. Throws `InvalidOperationException` before initialization. |
| `Task ShutdownAsync(CancellationToken = default)` | Flushes and closes the LaunchDarkly client and clears the process-wide client. Safe to call more than once. |
| `void Configure(SdkOptions options)` | Process-wide defaults (`CaptureContent`, default `true`). Throws `ConfigureLockedException` for different options after the first invocation. |
| `Config Config(ConfigArgs args)` | Binds an AI Config key to its handlers and tools. |
| `Task<InspectResult> InspectConfigAsync(string key, Context context, CancellationToken = default)` | Reads a variation without invoking a model. Failures are in `InspectResult.Error`. |
| `ProviderHandler CreateHandler(ProvidesFor providesFor, Func<HandlerRequest, CancellationToken, Task<HandlerResult>> handler[, CreateHandlerOptions options])` | Wraps a function as a handler. Another overload re-wraps an existing `ProviderHandler` under new metadata. |
| `string ParseTemplate(string template, IReadOnlyDictionary<string, object?>? variables)` | Renders `{{name}}` and `{{a.b}}` placeholders; a placeholder with no value is left as it is. |

`ILdAiClient`, which `InitClientAsync` returns, has `Config`, `InspectConfigAsync`, `ShutdownAsync` and the underlying `LdClient`, for dependency injection and test doubles.

**`ConfigArgs`** (init-only properties): `Key` (required), `Handler`, `Handlers`, `ToolHandlers` (by tool key) and `Registry`. Local handlers and tools win over the registry's.

**`Config.InvokeAsync(string? input, Context context, IReadOnlyDictionary<string, object?>? variables = null, IReadOnlyList<HistoryMessage>? history = null, CancellationToken cancellationToken = default)`** returns a `ProviderResponse`:

| Property | Type | Description |
|---|---|---|
| `Response` | `object?` | The model's text, or a `JsonElement` when the variation sets an `outputFormat` and the text parses as JSON. |
| `Usage` | `TokenUsage` | `Input`, `Output`, `Total` and, when the provider reports cache tokens, `InputDetails`. |
| `TrackData` | `TrackData` | The config key, variation key, version, model and provider names and the run ID that the events and spans carry. |

It throws `AiConfigNotFoundException` when the flag has no variation for the context (the key does not exist, or evaluation fell back to the default), `AiConfigDisabledException` when the variation is not enabled, `NoHandlerException` when no handler serves it, and `LdAiException` for an invalid variation. A handler's own exception is tracked as an error and rethrown. Every SDK exception derives from `LdAiException`.

**`InitOptions`**: `SdkKey`, `BaseUri`, `StreamUri`, `EventsUri`, `StartWaitTime` (default 10 seconds).

**`Registry`**: `Registry()`, `Registry(handlers, tools)`, `Register(handlers, tools)`, `Handlers`, `Tools` and `Registry.Compose(a, b)`. A registry is safe to share between threads. A second handler with the same provider, mode and provider list, or a second tool with the same key, replaces the first and logs a warning.

**Tools**: `FunctionTool(Func<JsonElement, CancellationToken, Task<object?>> function)` receives the arguments the model sent and returns the result, which is sent back to the model as text. `NativeTool(string toolName)` is a provider's built-in tool, which the SDK never calls. Only the tools the variation attaches, under the same key, reach the handler.

**Handler types**: `ProvidesFor(provider, mode)`, `HandlerModes` (`Messages`, `Agent`), `HandlerRequest` (`Config`, `UserInput`, `ToolHandlers`, `Variables`, `History`), `HandlerResult` (`Output`, `Usage`) and `ProviderHandler`.

## Migration notes

None yet: this is the first working version.

## Supported .NET versions

- .NET 8.0 and .NET 10.0
- .NET Standard 2.0, which includes .NET Framework 4.6.2 and later

## Learn more

- [LaunchDarkly AI Configs](https://docs.launchdarkly.com/home/ai-configs)
- [Contributing](https://github.com/launchdarkly/dotnet-ai-sdk/blob/main/CONTRIBUTING.md)
