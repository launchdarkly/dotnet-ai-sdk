# LaunchDarkly.AI.Server.ExtensionsAI: agents.md

Notes for contributors and coding agents working in this package. The repository-wide rules are in [`AGENTS.md`](../../AGENTS.md).

- **Role:** the `IChatClient` provider handler. It depends on `LaunchDarkly.AI.Server`, `Microsoft.Extensions.AI.Abstractions` and `Microsoft.Extensions.AI` (for its tool loop, `FunctionInvokingChatClient`); it never depends on a concrete chat client package.
- **Spans:** the handler emits its own `gen_ai` activities through the core package, so it must not wrap the client in `OpenTelemetryChatClient`, which would emit duplicates.
- **Public API:** recorded in `PublicAPI.Unshipped.txt`, as in the core package.
- **Status:** skeleton. No behaviour yet.
