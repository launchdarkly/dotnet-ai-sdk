# LaunchDarkly.AI.Server.ExtensionsAI: agents.md

The architectural contract of the `IChatClient` handler package, for contributors and coding agents. The repository-wide rules are in [`AGENTS.md`](../../AGENTS.md); the user-facing guide is [`README.md`](README.md).

## Role and tier

**Tier 1, a handler.** `ChatClientHandler` provides for `["*", "messages"]` (`ProvidesFor.Wildcard`, `HandlerModes.Messages`): any provider in messages mode, optionally scoped to a provider list. It runs a request on a caller-supplied `Microsoft.Extensions.AI` `IChatClient` and never depends on a concrete chat client package. Its helper-usage IDs are `extensionsai-messages.createChatClientMessagesHandler` and `extensionsai-messages.chatClientMessages`.

## File map

| File | Responsibility |
|---|---|
| `ChatClientHandler.cs` | `ChatClientHandler.Create` (client or client factory) and `ChatClientHandlerOptions`. |
| `ChatClientMessages.cs` | `ChatClientMessages.InvokeAsync`, which builds the handler and invokes a config in one call, and its options. |
| `PackageInfo.cs` | The package ID and version this package registers and reports. |
| `Internal/ChatClientRunner.cs` | Runs one request: resolves the client, builds the prompt, tools and options, runs the tool loop under the `invoke_agent` span, returns the last turn's text and the summed usage. |
| `Internal/PromptBuilder.cs` | Builds the messages: system prompt, config messages, history, user input. |
| `Internal/ChatOptionsBuilder.cs` | Builds the `ChatOptions`: model ID, forwarded `model.parameters`, tools and response format. |
| `Internal/OfferedTools.cs` | Selects the tools offered to the model and runs a tool call, failing closed on a tool that was not offered. |
| `Internal/ToolLoop.cs` | The `IToolLoop` seam, the model-turn and tool-call delegates, and the loop request. |
| `Internal/FunctionInvokingToolLoop.cs` | The tool loop on `FunctionInvokingChatClient`. |
| `Internal/PackageRegistration.cs` | Registers the package with the core package; holds the helper-usage IDs. |
| `Internal/IsExternalInit.cs` | Lets netstandard2.0 compile init-only setters and records. |

## Public exports

All in `LaunchDarkly.AI.Server.ExtensionsAI` (the exact members are in `PublicAPI.Unshipped.txt`; update this list with it):

```text
ChatClientHandler           Create(IChatClient[, ChatClientHandlerOptions])
                            Create(Func<AiConfig, CancellationToken, ValueTask<IChatClient>>[, ChatClientHandlerOptions])
ChatClientHandlerOptions    CaptureContent, Providers
ChatClientMessages          InvokeAsync(configKey, userInput, context, options, cancellationToken)
ChatClientMessagesOptions   ChatClient, ChatClientFactory, Variables, History, ToolHandlers,
                            Registry, CaptureContent, Providers
```

## Implementation details

How the handler consumes the four contract parameters of `HandlerRequest`:

- **`Config`:** the model ID is `model.name`, with the cross-region inference prefix from `model.region` when `provider.name` is `Bedrock`. The prompt is the config's `messages` (system-role messages joined into one system message), or its `instructions` as the system message when it has no messages. From `model.parameters`, only `temperature`, `max_tokens`, `top_p`, `top_k`, `frequency_penalty`, `presence_penalty`, `seed` and `stop_sequences` / `stop` are forwarded, by snake_case key; a value of the wrong type is dropped. `outputFormat` becomes `ChatResponseFormat.ForJsonSchema`. `model.custom` is never read.
- **`UserInput`:** appended as the last user turn, unless the config's last message is already a user turn (without history), or it is empty and the history ends with a user turn.
- **`ToolHandlers`:** a tool is offered only when the config attaches it and the request holds a `FunctionTool` under that key; the config's tools are the allowlist. Each is an `AIFunction` named by the tool key. `FunctionInvokingChatClient` runs the loop with `AllowConcurrentInvocation = false`, `MaximumConsecutiveErrorsPerRequest = 0` (a tool exception is rethrown) and `TerminateOnUnknownCalls`; after the loop, a call that never ran fails with an `LdAiException` naming the tool. A `NativeTool` is skipped with one warning per key. The loop runs with no current `Activity` on an inner client that exposes no `ActivitySource`, so the client adds no spans of its own.
- **`Variables`:** render `{{...}}` placeholders in the instructions and messages (unknown ones are kept), and carry `ldContext` and the tracking entries the span helpers read for the `launchdarkly.*` attributes and the `feature_flag` event.
- **`History`** (text only): placed after the config's messages; system-role turns are dropped.

The result's `Output` is the visible text of the last model turn (reasoning and other content are left out); `Usage` is the sum over every turn. What happens at `FunctionInvokingChatClient`'s iteration limit is the client's default for now: the last turn goes without tools, and no error is raised.

## OTel setup

The handler writes `invoke_agent`, `chat` and `execute_tool` spans through the core package's `LaunchDarkly.AI.Server` activity source. It registers no tracer provider and configures no exporter; neither does the core package in this version, so the application adds the source to its own tracer provider:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Endpoint configuration is in the core package's [agents.md](../LaunchDarkly.AI.Server/agents.md#otel-setup). Do not wrap the chat client in `OpenTelemetryChatClient` (`UseOpenTelemetry()`): it writes its own `chat` spans, which duplicate the handler's.

## `InitClientAsync`: when to call it

Not required: lazy initialization is the default, and the first invocation initializes the core client from `LD_SDK_KEY`. Call `LdAi.InitClientAsync` explicitly to pass custom options (the SDK key in code, base URIs, the start wait time), to bring your own `LdClient` (`InitClientAsync(ILdClient)`), or to pre-warm the connection at startup. The handler itself never initializes anything.

## Dependencies

| Package | Version | Why |
|---|---|---|
| `LaunchDarkly.AI.Server` | project reference | The handler contract, tracking and span helpers. |
| `Microsoft.Extensions.AI.Abstractions` | 10.11.0 or later | `IChatClient`, `ChatMessage`, `ChatOptions`, `AIFunction` and the usage types. |
| `Microsoft.Extensions.AI` | 10.11.0 or later | `FunctionInvokingChatClient`, the tool loop. Kept at the same version as the abstractions. |

`Microsoft.Extensions.AI` 10.11.0 raises `System.Text.Json` and `System.Diagnostics.DiagnosticSource` to 10.0.13 on net8.0 and netstandard2.0, above the core package's 8.x minimums. On net10.0 both are in-box.

## Common pitfalls

1. **Agent-mode variations find no handler.** This handler serves messages mode only. A variation with `_ldMeta.mode` `agent` throws `NoHandlerException`, even though the handler is a wildcard.
2. **A `NativeTool` is never offered.** The `IChatClient` handler has no built-in tools: a variation tool registered as a `NativeTool` is skipped with one warning, and the model runs without it.
3. **`OpenTelemetryChatClient` in the pipeline duplicates spans.** Leave it out of the client you pass; the handler already writes `chat` spans.
4. **A client factory runs once per request.** `Create(clientFactory)` calls the factory on every invocation. Cache expensive clients in the factory; the handler does not dispose them.
5. **Only snake_case parameter keys are forwarded.** `maxTokens` in `model.parameters` is dropped silently; use `max_tokens`.
