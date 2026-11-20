# LaunchDarkly.AI.Server: agents.md

The architectural contract of the core package, for contributors and coding agents. The repository-wide rules are in [`AGENTS.md`](../../AGENTS.md); the user-facing guide is [`README.md`](README.md).

## Role and tier

**Tier 0, the core client.** It evaluates AI Configs, selects a handler, scopes and tracks tools, tracks each invocation and owns the process-wide client. It is not a handler and has no `ProvidesFor` of its own. It never references a provider SDK or `Microsoft.Extensions.AI`; provider packages (Tier 1) reference it.

## File map

| File | Responsibility |
|---|---|
| `LdAi.cs` | The static entry point: initialization, shutdown, `Configure`, `Config`, `InspectConfigAsync`, `CreateHandler`, `ParseTemplate`, and the hidden provider-package hooks. |
| `ILdAiClient.cs` | The process-wide client as an interface, for injection and test doubles. |
| `InitOptions.cs` | SDK key, base URIs and start wait time for `InitClientAsync`. |
| `SdkOptions.cs` | Process-wide defaults set with `Configure` (content capture). |
| `Config.cs` | `ConfigArgs`, the bound `Config` and its `InvokeAsync` flow, and handler selection. |
| `Handlers.cs` | The handler contract: `ProvidesFor`, `HandlerRequest`, `HandlerResult`, `CreateHandlerOptions`, `ProviderHandler`. |
| `Registry.cs` | The caller-owned set of handlers and tools, with replacement by identity and `Compose`. |
| `Tools.cs` | `ToolHandler`, `FunctionTool`, `NativeTool`, and tool scoping and tracking wrappers. |
| `Model.cs` | The evaluated config and result records, and the mode and role constants. |
| `Exceptions.cs` | `LdAiException` and its subclasses. |
| `SdkInfo.cs` | The SDK's name, version and language as reported in telemetry. |
| `Internal/BedrockModelId.cs` | The Bedrock cross-region inference-profile prefix rule for model IDs. |
| `Internal/ConfigParser.cs` | Validates a variation and reads it into `AiConfig` and `VariationMeta`. |
| `Internal/InternalEvents.cs` | The SDK's own telemetry events, sent on an anonymous internal context. |
| `Internal/InvokeTracking.cs` | The duration, success or error and token events of an invocation. |
| `Internal/IsExternalInit.cs` | Lets netstandard2.0 compile init-only setters and records. |
| `Internal/JudgeWarning.cs` | The once-per-config-key warning that judges do not run yet. |
| `Internal/LdAiClient.cs` | The `ILdAiClient` implementation that stands for the singleton. |
| `Internal/Lifecycle.cs` | The singleton, the shared initialization attempt, and teardown. |
| `Internal/Modes.cs` | Maps a variation's mode to the handler mode. |
| `Internal/OnceKeys.cs` | Once-per-process keys, for warnings logged once. |
| `Internal/SdkInfoReporter.cs` | Registered AI packages and their once-per-client package-information events. |
| `Internal/SdkLog.cs` | The SDK's logger: the LaunchDarkly client's when there is one. |
| `Internal/SdkState.cs` | The `Configure` defaults and the lock that fixes them after the first invocation. |
| `Internal/StructuredOutput.cs` | Parses a handler's output as JSON when the config sets an output format. |
| `Internal/TaskExtensions.cs` | Waits on a shared task with a caller's own cancellation token. |
| `Internal/Telemetry.cs` | The `LaunchDarkly.AI.Server` activity source and the span helpers every handler uses. |
| `Internal/Template.cs` | Renders `{{name}}` and `{{a.b}}` placeholders. |
| `Internal/TestHooks.cs` | Test-only seams, reachable through `InternalsVisibleTo`. |
| `Internal/UsageParser.cs` | Normalizes a handler's raw token counts into `TokenUsage`. |
| `Internal/UsageReporter.cs` | Once-per-client helper-usage events, held until a client exists. |
| `Internal/Variables.cs` | Builds the variables a handler receives (`ldContext` and the tracking entries). |

## Public exports

The public types, all in `LaunchDarkly.AI.Server` (the exact members are in `PublicAPI.Unshipped.txt`; update this list with it):

```text
LdAi                 InitClientAsync, GetClient, ShutdownAsync, Configure, Config,
                     InspectConfigAsync, CreateHandler, ParseTemplate,
                     RegisterAiSdkPackage, ReportUsage (hidden: for provider packages)
ILdAiClient          Config, InspectConfigAsync, ShutdownAsync, LdClient
InitOptions          SdkOptions
Config               ConfigArgs
ProviderHandler      ProvidesFor  HandlerModes  HandlerRequest  HandlerResult  CreateHandlerOptions
Registry
ToolHandler          FunctionTool  NativeTool
AiConfig             ModelConfig  ProviderConfig  ConfigMessage  ToolDefinition  VariationMeta
HistoryMessage       MessageRoles
ProviderResponse     TokenUsage  InputTokenDetails  TrackData  InspectResult
LdAiException        AiConfigNotFoundException  AiConfigDisabledException
                     NoHandlerException  ConfigureLockedException
```

## Implementation details

**Lifecycle invariants** (`Internal/Lifecycle.cs`):

- There is one process-wide client. Every change to it happens under one lock; the LaunchDarkly client is built and closed outside the lock.
- Concurrent first calls share one initialization attempt. A failed attempt is not cached, so the next call tries again. A caller's cancellation token cancels only its own wait.
- With an SDK key, the first successful initialization wins and later options are ignored. `InitClientAsync(ILdClient)` replaces an attempt in flight, but not an installed client: with one installed, it returns that client and neither installs nor closes the passed one (call `ShutdownAsync` first to switch). It does not check that the client has initialized.
- `ShutdownAsync` detaches the client at once, restores the `Configure` defaults, re-arms the once-per-client warnings and events, then flushes and disposes the LaunchDarkly client off the caller's thread. A second call does nothing.
- `Configure` is locked by the first invocation: the same options are accepted again, different ones throw `ConfigureLockedException`.

**`Config(...).InvokeAsync` flow** (`Config.cs`):

1. `LdAi.Config` copies the local handlers and tools. The registry is read again on each invocation.
2. Get the LaunchDarkly client, initializing from `LD_SDK_KEY` when there is none.
3. Evaluate the flag as JSON (`JsonVariation` with a `null` default) and parse it: no value throws `AiConfigNotFoundException`, a variation not enabled throws `AiConfigDisabledException`, an invalid one throws `LdAiException`. A judge configuration logs the once-per-key warning.
4. Select the handler from the local handlers, then the registry's, by `provider.name` and the normalized mode.
5. Create the `TrackData` with a new run ID.
6. Scope the tools to the ones the variation attaches (local tools win over the registry's), and wrap each to track its call. A `NativeTool` becomes a tracking stub.
7. Build the handler's variables: the caller's, plus `ldContext` and the tracking entries the span helpers read.
8. Run the handler. Its exception is tracked as an error and rethrown. Otherwise track the duration, success and normalized usage, and return the response, parsed as JSON when the variation sets `outputFormat`.

The handler contract a provider package implements: a `ProviderHandler` built with `LdAi.CreateHandler(ProvidesFor, Func<HandlerRequest, CancellationToken, Task<HandlerResult>>)`. The request's `Config`, `UserInput`, `ToolHandlers`, `Variables` and `History` are its four contract parameters plus history. Agent graphs (`graph()`) are not in .NET yet.

## OTel setup

The package writes spans on the `System.Diagnostics.ActivitySource` named `LaunchDarkly.AI.Server` (`Internal/Telemetry.cs`). In this version `InitClientAsync` registers no tracer provider and no exporter: the application does, and handler packages do neither. To export with OpenTelemetry:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Then add the source to the tracer provider (`.AddSource("LaunchDarkly.AI.Server")`) and set the endpoint with the exporter's options or `OTEL_EXPORTER_OTLP_ENDPOINT`. The SDK reads no OpenTelemetry environment variables and stamps no resource attributes. `StartActivity` returns `null` when nothing listens, so every span helper accepts a `null` activity.

## `InitClientAsync`: when to call it

Explicit initialization is optional. The three paths:

- **Lazy (default):** the first `InvokeAsync` or `InspectConfigAsync` initializes from `LD_SDK_KEY`. The first call pays the connection time (up to the 10-second start wait).
- **Options:** call `InitClientAsync(new InitOptions { ... })` at startup to pass the SDK key in code, set base URIs (non-production or Relay Proxy), change the start wait time, or warm the connection before the first request.
- **Bring your own client:** call `InitClientAsync(ILdClient)` when the application already builds its `LdClient` (custom data source or store, logging, or a test data source). The application owns that client's configuration and readiness.

## Dependencies

| Package | Version | Targets | Why |
|---|---|---|---|
| `LaunchDarkly.ServerSdk` | `[8.17.2, 9.0.0)` | all | Flag evaluation, events and logging. |
| `System.Text.Json` | 8.0.6 or later | netstandard2.0 | Parsing variations and serializing span values. In-box on .NET 8 and later. |
| `System.Diagnostics.DiagnosticSource` | 8.0.1 or later | netstandard2.0 | `ActivitySource` for spans. In-box on .NET 8 and later. |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | build only | all | Keeps `PublicAPI.*.txt` in step with the code. |
| `Microsoft.NETFramework.ReferenceAssemblies` | build only | net462 tests | Builds the .NET Framework tests on any OS. |

## Common pitfalls

1. **`ShutdownAsync` disposes a client you passed in.** After `InitClientAsync(ldClient)`, shutting down the AI SDK flushes and disposes that `LdClient` too. An application that keeps using the client for flags must shut the AI SDK down last.
2. **A tool key that differs from the variation's is dropped silently.** Tools are scoped by exact, case-sensitive key. A registered tool under another key is never offered, and the model answers without it; nothing is logged.
3. **`Configure` after the first invocation throws.** Call `LdAi.Configure` at startup. In tests, `ShutdownAsync` restores the defaults.
4. **No spans without a listener.** If the tracer provider does not add the `LaunchDarkly.AI.Server` source, no spans are created and nothing reports it. LaunchDarkly events are sent either way.
5. **Lazy initialization without `LD_SDK_KEY`.** `InvokeAsync` throws `LdAiException`, but `InspectConfigAsync` returns the error in `InspectResult.Error` and does not throw.
