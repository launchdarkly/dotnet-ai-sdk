# LaunchDarkly.AI.Server: agents.md

Notes for contributors and coding agents working in this package. The repository-wide rules are in [`AGENTS.md`](../../AGENTS.md).

- **Role:** the core SDK. It depends on `LaunchDarkly.ServerSdk` and, on netstandard2.0 only, on `System.Text.Json` and `System.Diagnostics.DiagnosticSource`. It never references a provider SDK or `Microsoft.Extensions.AI`.
- **Public API:** every public member is recorded in `PublicAPI.Unshipped.txt` (moved to `PublicAPI.Shipped.txt` at release) and has an XML doc comment. `make api-check` regenerates the files.
- **Version:** `SdkInfo.Version` and the csproj `Version` change together.
- **Status:** skeleton. No behaviour yet.
