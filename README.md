# LaunchDarkly AI SDK for .NET

> [!CAUTION]
> **Pre-1.0 beta for testing. APIs may change before 1.0.**
> 0.x beta versions will be published to NuGet; none is published yet. Until the first release, build from source and install from a local `dotnet pack` feed.

The LaunchDarkly AI SDK for .NET lets server-side .NET applications use [AI Configs](https://docs.launchdarkly.com/home/ai-configs): manage prompts, models and tools in LaunchDarkly, target and roll them out per context, and track cost, latency and errors.

## Packages

| Package | What it is |
|---|---|
| [`LaunchDarkly.AI.Server`](src/LaunchDarkly.AI.Server) | The core SDK: evaluates AI Configs, routes them to a provider handler, tracks the result and writes spans. |
| [`LaunchDarkly.AI.Server.ExtensionsAI`](src/LaunchDarkly.AI.Server.ExtensionsAI) | The provider handler that runs AI Configs through any `Microsoft.Extensions.AI` `IChatClient`. |
| [`LaunchDarkly.AI`](src/LaunchDarkly.AI) | Convenience package that references `LaunchDarkly.AI.Server`. It has no code of its own. |
| [`examples/`](examples) | A console project that runs the example scenarios, offline or against your own flags. |

## What works so far

This beta (0.1.0) supports messages-mode AI Configs through an `IChatClient`, without streaming:

- **Initialization:** lazy from `LD_SDK_KEY` on the first call, explicit with `InitOptions` (SDK key, base URIs, start wait time), or with a LaunchDarkly client you built yourself. `LdAi.ShutdownAsync` flushes and closes it.
- **`LdAi.Config(...).InvokeAsync(...)`:** evaluates the AI Config for a context, selects the handler for the variation's provider and mode, renders `{{...}}` template variables, passes text history and the variation's tools, tracks duration, token usage, success or error and each tool call, and returns the response with its usage and tracking data.
- **Handlers:** one handler, several handlers, or a `Registry` your application owns. A handler names a provider, or serves any provider (optionally scoped to a list of providers).
- **Tools:** application functions (`FunctionTool`) that the handler runs in a tool loop, and provider built-in tools (`NativeTool`) that the SDK never calls.
- **`ChatClientHandler` and `ChatClientMessages`** in `LaunchDarkly.AI.Server.ExtensionsAI`: model parameters, structured output (`outputFormat`), and the tool loop on `FunctionInvokingChatClient`.
- **`LdAi.InspectConfigAsync`**, **`LdAi.ParseTemplate`** and **`LdAi.CreateHandler`** for custom handlers.
- **Spans** through the `LaunchDarkly.AI.Server` `ActivitySource` (see [Telemetry](#telemetry)).

Not yet in .NET: streaming, agent-mode handlers, agent graphs, judges (a variation with a judge configuration runs no judge and logs one warning per config key), conversation IDs and the OpenTelemetry setup package. Each arrives in a later 0.x release.

<!-- experimental-features:start (generated; do not edit by hand) -->
Experimental features: None.
<!-- experimental-features:end -->

## Quick start

Once a 0.x beta is published to NuGet, add it with `dotnet add package LaunchDarkly.AI.Server.ExtensionsAI` (pin the exact version: a 0.x minor release can change the API). Until then, build the packages from source and add them to your application from the local feed (see [Building from source](#building-from-source)):

```bash
make pack
dotnet add package LaunchDarkly.AI.Server.ExtensionsAI --source /path/to/dotnet-ai-sdk/nupkgs
```

`LaunchDarkly.AI.Server.ExtensionsAI` brings in `LaunchDarkly.AI.Server`. Then add the `IChatClient` for your model provider, for example an OpenAI, Azure OpenAI or Ollama adapter for `Microsoft.Extensions.AI`.

**One handler.** Every variation of the AI Config runs on one chat client:

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

**Several providers.** Pass several handlers; the one that serves the variation's `provider.name` runs:

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

**A registry.** Keep the handlers and tools in one `Registry` your application owns, and pass it to each config. Tool keys must match the tool keys in the variation exactly:

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

**Agent graphs** (`graph()` in the other LaunchDarkly AI SDKs) are not in .NET yet.

The [examples](examples) run these patterns end to end without network access.

## Configuration

**SDK key.** The SDK reads your server-side SDK key from the `LD_SDK_KEY` environment variable when it initializes itself on the first call. To pass it in code, initialize explicitly before the first call. `InitOptions` also sets the base URIs for a non-production environment or a Relay Proxy:

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

| Option | Default | Description |
|---|---|---|
| `SdkKey` | `LD_SDK_KEY` | The server-side SDK key. Without either, initialization throws `LdAiException`. |
| `BaseUri` | LaunchDarkly | Base URI for polling and other requests. |
| `StreamUri` | LaunchDarkly | Streaming URI. |
| `EventsUri` | LaunchDarkly | Events URI. |
| `StartWaitTime` | 10 seconds | How long to wait for the LaunchDarkly client to initialize before failing. |

**Your own LaunchDarkly client.** If the application already builds an `LdClient` (custom configuration, data store, logging), pass it in. The SDK uses it as it is and does not check that it has initialized:

<!-- snippet: byoc -->
```csharp
// Bring your own client: the application builds the LaunchDarkly client and owns its
// configuration and readiness.
var ldClient = new LdClient(Configuration.Builder(sdkKey).StartWaitTime(TimeSpan.FromSeconds(5)).Build());
var aiClient = await LdAi.InitClientAsync(ldClient);
```

With an SDK key, the first initialization wins: a later `InitClientAsync(options)` returns the existing client and ignores its options. Passing your own client also returns the existing client when there is one, and leaves the client you passed unused. `LdAi.ShutdownAsync` clears it, so the next call initializes again: call it first to switch clients.

## Telemetry

The SDK writes its spans as `System.Diagnostics` activities on one `ActivitySource` named `LaunchDarkly.AI.Server`. A run of the `IChatClient` handler writes an `invoke_agent` span, a `chat` span per model turn and an `execute_tool` span per tool call, with OpenTelemetry `gen_ai.*` attributes (`gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.usage.*`, `gen_ai.tool.*`), the `launchdarkly.config.key`, `launchdarkly.variation.key` and `launchdarkly.run.id` attributes, and a `feature_flag` event for the evaluated AI Config.

The SDK does not configure OpenTelemetry. With the OpenTelemetry .NET SDK, add the source to your tracer provider (`.AddSource("LaunchDarkly.AI.Server")`) and configure the exporter as usual; the exporter reads its own environment variables, such as `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME`. The SDK itself reads no telemetry environment variables and stamps no resource attributes in this version. When nothing listens to the source, no spans are created.

By default, spans record prompts, completions and tool arguments and results. To turn that off for every handler, or to listen without OpenTelemetry:

<!-- snippet: telemetry -->
```csharp
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
```

A handler's own setting (`CaptureContent` on `ChatClientHandlerOptions`) wins over `LdAi.Configure`. `LdAi.Configure` must run before the first invocation: changing it afterwards throws `ConfigureLockedException`.

Separately from spans, each invocation sends LaunchDarkly events (duration, token usage, success or error, tool calls) through the LaunchDarkly client for the AI Config's monitoring page.

## Supported .NET versions

- .NET 8.0 and .NET 10.0
- .NET Standard 2.0, which includes .NET Framework 4.6.2 and later

## Building from source

You need the .NET 10 SDK (see `global.json`); the .NET 8 runtime is needed to run the net8.0 tests and the examples. `make` restores the NuGet dependencies the first time.

```bash
make build    # Release build, warnings as errors
make test     # unit tests, including the documentation sample checks
make examples # run the example scenarios offline
make pack     # NuGet packages into nupkgs/
```

To use a local build in an application, add `nupkgs/` as a package source:

```bash
dotnet nuget add source "$(pwd)/nupkgs" --name launchdarkly-ai-local
```

[AGENTS.md](AGENTS.md) lists every check (`make lint`, `make format-check`, `make api-check`, `make docs`, `make pack-check`).

## Signing

Published assemblies will be digitally signed with Authenticode and [strong-named](https://learn.microsoft.com/dotnet/standard/assembly/strong-named). Debug builds are not strong-named. Release builds made without the private key are public-signed with `LaunchDarkly.pk`, so they have the same assembly identity as the published build:

```
Public Key:
0024000004800000940000000602000000240000525341310004000001000100f121bbf427e4d7
edc64131a9efeefd20978dc58c285aa6f548a4282fc6d871fbebeacc13160e88566f427497b625
56bf7ff01017b0f7c9de36869cc681b236bc0df0c85927ac8a439ecb7a6a07ae4111034e03042c
4b1569ebc6d3ed945878cca97e1592f864ba7cc81a56b8668a6d7bbe6e44c1279db088b0fdcc35
52f746b4

Public Key Token: f86add69004e6885
```

## Contributing

We encourage pull requests and other contributions from the community. See [CONTRIBUTING.md](CONTRIBUTING.md). Contributors and coding agents: read [AGENTS.md](AGENTS.md) first.

## Verifying build provenance

Published packages will carry GitHub artifact attestations; see [PROVENANCE.md](PROVENANCE.md).

## About LaunchDarkly

- LaunchDarkly is a continuous delivery platform that provides feature flags as a service and allows developers to iterate quickly and safely. We allow you to easily flag your features and manage them from the LaunchDarkly dashboard. With LaunchDarkly, you can:
  - Roll out a new feature to a subset of your users (like a group of users who opt-in to a beta tester group), gathering feedback and bug reports from real-world use cases.
  - Gradually roll out a feature to an increasing percentage of users, and track the effect that the feature has on key metrics (for instance, how likely is a user to complete a purchase if they have feature A versus feature B?).
  - Turn off a feature that you realize is causing performance problems in production, without needing to re-deploy, or even restart the application with a changed configuration file.
  - Grant access to certain features based on user attributes, like payment plan (eg: users on the 'gold' plan get access to more features than users in the 'silver' plan).
  - Disable parts of your application to facilitate maintenance, without taking everything offline.
- LaunchDarkly provides feature flag SDKs for a wide variety of languages and technologies. Read [our documentation](https://docs.launchdarkly.com/sdk) for a complete list.
- Explore LaunchDarkly
  - [launchdarkly.com](https://www.launchdarkly.com/ "LaunchDarkly Main Website") for more information
  - [docs.launchdarkly.com](https://docs.launchdarkly.com/ "LaunchDarkly Documentation") for our documentation and SDK reference guides
  - [apidocs.launchdarkly.com](https://apidocs.launchdarkly.com/ "LaunchDarkly API Documentation") for our API documentation
  - [launchdarkly.com/blog](https://launchdarkly.com/blog/ "LaunchDarkly Blog Documentation") for the latest product updates
