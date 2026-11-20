# LaunchDarkly.AI.Server.ExtensionsAI

A provider for the LaunchDarkly AI SDK for .NET that runs AI Configs through any [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient`: OpenAI, Azure OpenAI, Anthropic, Ollama, Amazon Bedrock and others that have an `IChatClient` adapter.

> [!CAUTION]
> **Pre-1.0 beta for testing. APIs may change before 1.0.**
> 0.x beta versions will be published to NuGet; none is published yet. Until the first release, build from source and install from a local `dotnet pack` feed.

## Install

Once a 0.x beta is published to NuGet, add it with `dotnet add package LaunchDarkly.AI.Server.ExtensionsAI` (pin the exact version: a 0.x minor release can change the API). Until then, build the packages from source (see the [repository README](https://github.com/launchdarkly/dotnet-ai-sdk#building-from-source)) and add the package from the local feed:

```bash
dotnet add package LaunchDarkly.AI.Server.ExtensionsAI --source /path/to/dotnet-ai-sdk/nupkgs
```

It brings in `LaunchDarkly.AI.Server`. Add the `IChatClient` adapter for your model provider separately; this package depends on no concrete chat client.

## `ProvidesFor`

`ChatClientHandler` serves `["*", "messages"]`: any `provider.name`, in messages mode. A variation selects it when its `_ldMeta.mode` is anything but `agent` (`messages`, `completion`, `judge` or none) and no handler that names the variation's provider exactly is passed with it. With `ChatClientHandlerOptions.Providers`, the handler serves only the providers it lists (exact, case-sensitive names, such as `"OpenAI"` or `"Anthropic"`).

Agent-mode variations (`_ldMeta.mode` `agent`) are not served by this package yet: they fail with `NoHandlerException`.

## Examples

One chat client for every variation:

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

One chat client per provider:

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

A chat client chosen per request from the evaluated variation, so the model the variation names is the one called:

<!-- snippet: client-factory -->
```csharp
// Choose the chat client per request, from the evaluated variation's provider and model.
var handler = ChatClientHandler.Create((config, cancellationToken) =>
    new ValueTask<IChatClient>(chatClientFor(config.Provider.Name, config.Model.Name)));
```

Build the handler and invoke in one call:

<!-- snippet: chat-client-messages -->
```csharp
// Build the handler and invoke the config in one call.
var response = await ChatClientMessages.InvokeAsync(
    "my-ai-config",
    "What is a feature flag?",
    Context.New("user-123"),
    new ChatClientMessagesOptions { ChatClient = chatClient });
```

The [examples](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/examples) run this handler against a scripted `IChatClient`, with no network access.

## Environment variables

The package reads none. The core package reads `LD_SDK_KEY` when it initializes itself. Model provider credentials belong to the `IChatClient` you build.

## API reference

All public types are in the `LaunchDarkly.AI.Server.ExtensionsAI` namespace.

| Member | Description |
|---|---|
| `ChatClientHandler.Create(IChatClient client[, ChatClientHandlerOptions options])` | A handler that sends every request to `client`. Returns a `ProviderHandler`. |
| `ChatClientHandler.Create(Func<AiConfig, CancellationToken, ValueTask<IChatClient>> clientFactory[, ChatClientHandlerOptions options])` | A handler that asks `clientFactory` for a chat client once per request, with the evaluated config. |
| `ChatClientMessages.InvokeAsync(string configKey, string? userInput, Context context, ChatClientMessagesOptions? options = null, CancellationToken cancellationToken = default)` | Builds a `ChatClientHandler` and invokes the config with it. Returns a `ProviderResponse`. |

**`ChatClientHandlerOptions`**: `CaptureContent` (`bool?`, overrides `LdAi.Configure` for this handler) and `Providers` (the provider names this handler serves; `null` serves any).

**`ChatClientMessagesOptions`**: `ChatClient` or `ChatClientFactory` (exactly one is required in this version), `Variables`, `History`, `ToolHandlers`, `Registry`, `CaptureContent` and `Providers`. With neither a client nor a factory, or with both, the call throws `ArgumentException` with `ParamName` `options`.

What a request sends:

- **Messages:** the variation's `messages`, with its system-role messages joined into one system message, or its `instructions` as the system message when it has no messages; then the `history` (text only; system-role turns are dropped); then the user input. `{{...}}` placeholders in the variation are rendered from the `variables` and the context (`{{ldContext.key}}`).
- **Options:** the model ID is the variation's `model.name` (with the cross-region inference prefix from `model.region` when `provider.name` is `Bedrock`). From `model.parameters`, by snake_case key: `temperature`, `max_tokens`, `top_p`, `top_k`, `frequency_penalty`, `presence_penalty`, `seed`, and `stop_sequences` or `stop`. Other keys are dropped. An `outputFormat` is sent as a JSON-schema response format.
- **Tools:** each tool the variation attaches that has a `FunctionTool` under the same key. The handler runs the model's tool calls in a loop on `FunctionInvokingChatClient`, one at a time, until the model answers without a tool call. A tool's exception ends the run and is rethrown; a call to a tool that was not offered fails with `LdAiException`. A `NativeTool` is not offered (this handler has no built-in tools) and is skipped with one warning per tool key.

The response is the text of the last model turn, with the usage summed over every turn.

## Dependencies

| Package | Version | Why |
|---|---|---|
| `LaunchDarkly.AI.Server` | this repository | The core SDK. |
| `Microsoft.Extensions.AI.Abstractions` | 10.11.0 or later | `IChatClient` and the message, tool and usage types. |
| `Microsoft.Extensions.AI` | 10.11.0 or later | The tool loop (`FunctionInvokingChatClient`). |

On .NET 8.0 and .NET Standard 2.0, `Microsoft.Extensions.AI` 10.11.0 brings in `System.Text.Json` and `System.Diagnostics.DiagnosticSource` 10.0.13, among other 10.0.13 packages. That is higher than the 8.x versions the core package needs on .NET Standard 2.0, so an application that references either package at an earlier version gets a package downgrade warning (NU1605, an error by default) and must raise that version. On .NET 10.0 both are part of the runtime.

## Migration notes

None yet: 0.1.0 is the first beta.

## Supported .NET versions

- .NET 8.0 and .NET 10.0
- .NET Standard 2.0, which includes .NET Framework 4.6.2 and later
