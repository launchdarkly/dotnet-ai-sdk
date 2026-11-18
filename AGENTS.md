# LaunchDarkly AI SDK for .NET: AGENTS.md

Architectural contract for contributors and coding agents. Behaviour follows the
LaunchDarkly AI SDK contract shared with the JavaScript, Python and Go SDKs; where
it leaves a public detail open, follow the JavaScript SDK, then Python. Internals
follow .NET idiom (see [Language idioms](#language-idioms)).

## Status

Pre-1.0 working version. Nothing is published to NuGet before 1.0; each feature
bumps the working version (0.1.0, 0.2.0, ...) in its own pull request. The
repository is currently a **skeleton**: build, test, lint and docs plumbing, with
no public API and no behaviour.

## Packages

| Package | Folder | Role |
|---|---|---|
| `LaunchDarkly.AI.Server` | `src/LaunchDarkly.AI.Server` | Core: AI Config evaluation, invocation, tracking, activities. No provider dependency. |
| `LaunchDarkly.AI.Server.ExtensionsAI` | `src/LaunchDarkly.AI.Server.ExtensionsAI` | Provider handler for any `Microsoft.Extensions.AI` `IChatClient`. |
| `LaunchDarkly.AI` | `src/LaunchDarkly.AI` | Metapackage: no assembly, depends on `LaunchDarkly.AI.Server` only. |

Dependency direction: `ExtensionsAI` and `LaunchDarkly.AI` reference the core; the
core references neither. Each package folder has its own `README.md` (shipped in
the package) and, for the two libraries, an `agents.md`.

## Repository layout

```
LaunchDarkly.AI.Server.sln
Directory.Build.props        shared settings: Nullable, LangVersion, warnings as errors, package metadata
Directory.Packages.props     central package versions (library versions are minimums)
global.json                  .NET 10 SDK, rollForward latestFeature
LaunchDarkly.pk              public half of the shared LaunchDarkly strong-name key
src/Directory.Build.props    library settings: target frameworks, XML docs, packaging, SourceLink, signing
src/Directory.Build.targets  PublicApiAnalyzers for every library that ships an assembly
src/<Package>/               one folder per package (PublicAPI.Shipped.txt / PublicAPI.Unshipped.txt)
test/Directory.Build.props   xUnit settings and test target frameworks
test/<Package>.Tests/        one xUnit project per package
examples/                    runnable examples (none yet)
scripts/                     pack-check.sh (packaging check), api-update.sh (PublicAPI file update),
                             api-compat.sh (breaking-change check), api-check-selftest.sh
docs-src/, docfx.json, index.md, toc.yml    API reference (docfx)
.github/workflows/           CI, PR-title lint, docs, release (not run until the first push)
```

## Build and checks

| Command | What it does |
|---|---|
| `make build` | Release build, warnings as errors (includes the public API check) |
| `make test` | xUnit tests on the Debug build: net8.0 and net10.0; net462 too on Windows |
| `make lint` | Analyzer diagnostics without changing files |
| `make format` / `make format-check` | Apply / verify whitespace and code style (`.editorconfig`) |
| `make api-update` | Update `PublicAPI.*.txt` from the code: add new public members (RS0016) and remove deleted ones (RS0017) |
| `make api-check` | Run `make api-update` and fail if `PublicAPI.*.txt` then differ from what is committed |
| `make docs` | docfx API reference into `docs/` (docfx is pinned in `.config/dotnet-tools.json`) |
| `make api-compat BASE=<ref>` | Breaking-change check of the public API against a git ref (default `main`), with ApiCompat |
| `make pack` | NuGet packages into `nupkgs/` |
| `make pack-check` | Install each package from a local feed into a fresh project, build and run it |

`TESTFRAMEWORK=net8.0 make test` runs one target framework. `BUILDFRAMEWORKS`
does the same for the libraries. The pre-commit hook (`lefthook.yml`) runs
`dotnet format --verify-no-changes`.

## Public API

- Every public member is listed in the package's `PublicAPI.Unshipped.txt`. The
  build fails on a public change that isn't recorded (RS0016/RS0017); `make api-update`
  records it. At 1.0 the release moves the entries to `PublicAPI.Shipped.txt`.
- Every public member has an XML doc comment; missing or invalid docs fail the build.
- Breaking changes against the base branch fail the API compatibility check
  (`make api-compat`, and a CI job on every pull request) unless the pull request
  has the `api-compatible` label. Checks against the last release (package
  validation) start after 1.0, the first published version.
- Deprecate with `[Obsolete("Use X.")]`, never by removing a shipped member.

## Versioning and release

- The csproj `Version` of each package and `SdkInfo.Version` move together. They
  are `0.0.0` in the skeleton; each feature pull request sets the next 0.x working version.
- No NuGet push, GitHub release or version tag before 1.0. release-please runs
  only when dispatched by hand, and its first run is the 1.0 release.
- The release workflow strong-names the assemblies with the shared key, signs them
  with Authenticode, packs, attests provenance and pushes to NuGet behind a
  protected environment. The repository settings this needs are listed in
  CONTRIBUTING.md ("For maintainers: repository settings").

## Deviations from established LaunchDarkly .NET practice

Each is deliberate. None conflicts with a company rule.

| Deviation | Why |
|---|---|
| `<Nullable>enable</Nullable>` | Consumers get nullability in the API; it is source-compatible for callers who don't use it. |
| Root `Directory.Build.props` and central package versions | Three packages share one set of settings and versions; nothing drifts between them. |
| `LangVersion` latest with the .NET 10 SDK pinned in `global.json` | Modern C# with a reproducible compiler; the netstandard2.0 build still runs on .NET Framework. |
| SourceLink (built into the .NET SDK) | Debuggers step into the exact published source. |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers`, then package validation after 1.0 | The public surface can't change by accident. |
| Hand-written fakes instead of Moq | Fakes are readable, need no dynamic proxies, and work unchanged on every target framework. |
| Release builds without the key file are public-signed with `LaunchDarkly.pk` | A local Release build has the same assembly identity as the published one without needing the private key. |

## Design decisions

- **The messages-mode tool loop runs on `FunctionInvokingChatClient`.** `LaunchDarkly.AI.Server.ExtensionsAI`
  depends on `Microsoft.Extensions.AI` for it; the core package does not. The loop sits behind an internal
  `IToolLoop` seam. The handler keeps the `chat` and `execute_tool` spans, usage, tool scoping and
  tool-result text, and the client's own options cover the rest: tool exceptions are rethrown
  (`MaximumConsecutiveErrorsPerRequest = 0`), calls in one response run in order
  (`AllowConcurrentInvocation = false`), and an unknown tool ends the loop (`TerminateOnUnknownCalls`).
  Three small pieces are hand-written, because the client has no option for them:
  - **Fail closed.** After the loop, a tool call that was never run means the model named a tool that was
    not offered, and the handler throws an `LdAiException` naming it. The client would otherwise answer
    the model "not found", or return the call unhandled.
  - **No duplicate spans.** The loop runs with no current `Activity`, under an inner client that exposes
    no `ActivitySource`. Otherwise the client adds its own `execute_tool` span on the source of a current
    `invoke_agent` span, which is the SDK's.
  - **Invocable tools.** Each offered tool is an `AIFunction` whose calls are routed through the client's
    `FunctionInvoker` to the handler's tool runner. The client treats a bare `AIFunctionDeclaration` as
    not invocable and stops the loop.
- **Agent mode (planned) will add a max-turns middleware below `FunctionInvokingChatClient`.** It is the one
  hand-written loop piece agent mode needs. `MaximumIterationsPerRequest` does not throw at the limit (the
  last turn is sent without tools), and it is a per-instance setting, not a per-request one.
- **What messages mode does at the turn limit is open.** The SDKs differ, so this one keeps the client's
  default: at its iteration limit the last turn goes without tools, and no error is raised.

## Language idioms

The public contract is shared with the JavaScript, Python and Go SDKs. Everything
else is written the way a .NET developer expects: see the
[.NET Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/)
and the [C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions).
Do not port another SDK's shape into C#. If the contract and a .NET idiom
conflict, the contract wins: raise the conflict instead of bending either one.

### What never varies
Public behaviour, defaults, telemetry (activity names, attributes, events), wire
payloads, usage IDs and error *conditions* (what fails, and before or after which
I/O). A name is the contract's name in .NET casing. A .NET-only public name is
fine when C# needs a named type for a member, parameter or result.

### Errors
- Throw where the other SDKs throw: typed exceptions deriving from one SDK base
  exception, so callers can catch one type or a specific one. Callers never match
  exception messages.
- Validate arguments up front with `ArgumentException` / `ArgumentNullException`.
- Return a safe fallback where the contract says so, and where it pairs a result
  with an error, return a result object with an `Error` member instead of throwing.
- Never let an exception from caller code run on a background task go unobserved.

### Layout
One package per folder under `src/`, one test project per package under `test/`.
The core never references a provider package. Experimental names live only in an
`Experimental` namespace. Internals are `internal`; tests use the public API.

### Naming
PascalCase for types and members, two-letter acronyms upper-case (`AI`), longer
ones Pascal-cased (`Json`). Interfaces start with `I`. Async methods end in
`Async`. Private fields are `_camelCase`. Spec string values stay strings, in a
static class of constants, not enums.

### Concurrency
Every I/O call is `async Task<T>`, takes `CancellationToken cancellationToken = default`
last, and honours it. Library code awaits with `ConfigureAwait(false)`. No
sync-over-async (`.Result`, `.Wait()`). Streams are `IAsyncEnumerable<T>`. No
task outlives the call that started it unless the contract says so.

### Options
An options class with init-only properties, validated when it is used. Durations
are `TimeSpan`. A nullable property means "not set"; the default is documented on
the property. Results are `sealed record`s.

### Testing
xUnit, hand-written fakes, no network. One test class per behaviour, named for
the behaviour (`ConfigParsingTests`). Activities are checked with an in-memory
`ActivityListener`. A test of a once-per-process effect resets it or runs in
isolation, so test order doesn't matter.

### Docs
An XML doc comment on every public member, starting with what it does. Each
package ships its own `README.md`; each library has an `agents.md`. Reuse the
other SDKs' wording for the same concept.

### Dependencies
The BCL first. Core: `LaunchDarkly.ServerSdk` (8.x), plus `System.Text.Json` and
`System.Diagnostics.DiagnosticSource` on netstandard2.0 only. A provider SDK goes
only in its own provider package. A new dependency needs a reason in the pull
request and a minimum version in `Directory.Packages.props`.

### References
`make build`, `make test`, `make lint`, `make format-check`, `make api-check`, `make docs`.

## Rules for every change

- This repository is public. Commit messages, code, comments and docs name no
  internal tickets, repositories, documents or hostnames. Comments describe
  behaviour.
- Conventional-commit messages and pull-request titles (`feat:`, `fix:`, `docs:`,
  `build:`, `ci:`, `test:`, `chore:`).
- Never commit a key, token or credential. `*.snk` is ignored; tests use generated
  fake keys only.
- Don't skip the hooks.
