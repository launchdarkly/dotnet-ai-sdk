# LaunchDarkly.AI: agents.md

The architectural contract of the metapackage, for contributors and coding agents. The repository-wide rules are in [`AGENTS.md`](../../AGENTS.md); the user-facing guide is [`README.md`](README.md).

## Role and tier

**Tier 2, the convenience package.** It ships no assembly: the package's only content is its dependency on `LaunchDarkly.AI.Server`, so an application that references `LaunchDarkly.AI` compiles against the core package's own types. It is not a handler and has no `ProvidesFor`. It does not reference `LaunchDarkly.AI.Server.ExtensionsAI`, so referencing it pulls in no `Microsoft.Extensions.AI` dependency.

## File map

| File | Responsibility |
|---|---|
| `LaunchDarkly.AI.csproj` | Packs no build output (`IncludeBuildOutput` false, no compile items) and references `LaunchDarkly.AI.Server`. |
| `README.md` | The package README, shipped in the package. |
| `agents.md` | This file. |

## Public exports

None of its own. Its public API is exactly `LaunchDarkly.AI.Server`'s; see that package's [agents.md](../LaunchDarkly.AI.Server/agents.md#public-exports). `test/LaunchDarkly.AI.Tests` fails to compile if a public type listed in the core package's `PublicAPI` files is not reachable through this package, and `make pack-check` fails if the package contains an assembly.

## Implementation details

There is no code. The lifecycle invariants and the `Config(...).InvokeAsync` flow are the core package's ([agents.md](../LaunchDarkly.AI.Server/agents.md#implementation-details)). Its version moves with the core package's version.

## OTel setup

Nothing to configure here. Spans come from the core package's `LaunchDarkly.AI.Server` activity source, which the application adds to its tracer provider; the core package's [agents.md](../LaunchDarkly.AI.Server/agents.md#otel-setup) has the install commands and the endpoint configuration.

## `InitClientAsync`: when to call it

The core package's three paths apply unchanged:

- **Lazy (default):** the first invocation initializes from `LD_SDK_KEY`.
- **Options:** `LdAi.InitClientAsync(new InitOptions { ... })` for an SDK key in code, base URIs, the start wait time, or pre-warming.
- **Bring your own client:** `LdAi.InitClientAsync(ILdClient)` when the application builds its own `LdClient`.

## Dependencies

| Package | Why |
|---|---|
| `LaunchDarkly.AI.Server` | The whole API. |

## Common pitfalls

1. **Adding code here breaks the package's shape.** The metapackage must stay assembly-free; `make pack-check` rejects an assembly. A new type belongs in `LaunchDarkly.AI.Server`.
2. **It does not bring in a handler.** An application that references only `LaunchDarkly.AI` has no `IChatClient` handler; `InvokeAsync` throws `NoHandlerException` until it passes one, such as `LaunchDarkly.AI.Server.ExtensionsAI`'s or its own.
