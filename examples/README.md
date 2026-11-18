# Examples

A console project that runs one scenario of the LaunchDarkly AI SDK for .NET. It is not a test suite: nothing asserts automatically. It is the end-to-end check that a change to the core package or a handler still works.

```bash
dotnet run --project examples -- <scenario> <flag-key> "<user input>"
```

From the repository root, `make examples` runs every scenario offline, plus the failure case.

## Offline and against your own flags

**Offline (default).** With no `LD_SDK_KEY`, the examples need no network access or API keys. The LaunchDarkly client is passed in by the examples (bring your own client) and serves one built-in AI Config, `launch-darkly-documentation-summarizer`, from a test data source. It is a messages-mode variation with a system message, model parameters and the `get-user-preferences` tool. The model is `ScriptedChatClient`, an in-memory `IChatClient` that calls `get-user-preferences` when it is offered, then answers with text built from the request.

**Your own flags.** With `LD_SDK_KEY` set, the SDK connects to LaunchDarkly and evaluates the flag key you pass. Optional: `LD_BASE_URI`, `LD_STREAM_URI` and `LD_EVENTS_URI` for a non-production environment or a Relay Proxy. Use a **messages-mode** AI Config; agent-mode configs have no .NET handler yet and fail with `NoHandlerException`. The model is still the scripted client: the examples ship no model provider package. To call a real model, return your provider's `IChatClient` from `Register.ChatClientFor`.

## Scenarios

| Scenario | Demonstrates | Offline flag key |
|---|---|---|
| `agent` | `LdAi.Config(...).InvokeAsync(input, context)` through the examples' registry; prints the response JSON and writes it to `output/` | `launch-darkly-documentation-summarizer` |
| `history` | The same call with four earlier conversation turns passed as `history`; the scripted answer quotes the first one | same |

Expected behaviour:

- **Success** (exit 0): `agent launch-darkly-documentation-summarizer "What is a feature flag?"` and `history launch-darkly-documentation-summarizer "What did I say matters to me?"` print the response JSON (response, usage, tracking data) on stdout and write it to a new file in `output/`, under the current directory.
- **Failure** (exit 1): `agent doesnt-exist "hi"` prints one line on stderr, `Error: No AI Config variation was found for "doesnt-exist".`, and writes no file.
- A missing scenario or flag key prints the usage and exits 2.

Not in .NET yet, so not here: `graph`, `streaming`, `graph-streaming`, `native-graph`, `native-graph-langchain`, `langchain-thinking`, `conversation`, `graph-history`, `openai-only` and `judge`. Each arrives with the feature it shows.

## Files

| File | Purpose |
|---|---|
| `Program.cs` | Argument parsing, client initialization (offline or `LD_SDK_KEY`), dispatch to `Scenarios`, shutdown, exit codes |
| `Scenarios.cs` | One method per scenario: `AgentAsync`, `HistoryAsync` |
| `Register.cs` | Builds the `Registry` every scenario is passed: every handler this repository provides (the `IChatClient` handler, `["*", "messages"]`) and every tool |
| `Tools.cs` | The tools, keyed exactly as the flag variations name them: `get-user-preferences` and `user-preferences-lookup` (from `user_preferences.json`), `search-ld-documentation`, `fetch-ld-documentation` and `fetch-launchdarkly-documentation` (these two call the network), and `web-search`, a `NativeTool("WebSearch")` sentinel the `IChatClient` handler skips |
| `Utils.cs` | `NewContext`, `PrintJson`, `WriteOutput` (to `output/`) and `SolidColorPngBase64` (a generated PNG, for multimodal examples) |
| `Offline.cs` | The offline AI Config (`OfflineFlags`) and the scripted model (`ScriptedChatClient`) |
| `user_preferences.json` | Fixture for the user-preferences tools |
| `Snippets/` | The code samples in the README files, compiled with the project and checked against the READMEs by the unit tests |

The registry belongs to the examples: the SDK has no process-wide registry, so `Program` builds one with `Register.BuildRegistry()` and passes it to each scenario, which passes it on `ConfigArgs.Registry`.
