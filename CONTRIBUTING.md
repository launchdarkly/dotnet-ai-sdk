# Contributing to the LaunchDarkly AI SDK for .NET

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/sdk/concepts/contributors-guide) that explains how our SDKs work. See below for how to contribute to this SDK.

## Submitting bug reports and feature requests

The LaunchDarkly SDK team monitors the [issue tracker](https://github.com/launchdarkly/dotnet-ai-sdk/issues). Bug reports and feature requests specific to this SDK should be filed there. The SDK team will respond to all newly filed issues within two business days.

## Submitting pull requests

We encourage pull requests and other contributions from the community. Before submitting a pull request, make sure all temporary or unintended code is removed. Don't worry about adding reviewers; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.

Pull-request titles and commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `build:`, `ci:`, `test:`, `chore:`); CI checks the title.

## Build instructions

### Prerequisites

- The .NET 10 SDK (the exact feature band is in `global.json`).
- The .NET 8 runtime, to run the net8.0 tests.
- `make`. Optionally [lefthook](https://lefthook.dev) for the pre-commit hook: `lefthook install`.

### Building and testing

```bash
make build         # Release build, warnings as errors
make test          # unit tests (TESTFRAMEWORK=net8.0 for one framework)
make lint          # analyzers
make format        # apply formatting; make format-check verifies it
make api-check     # public API files match the code
make docs          # API reference with docfx
```

The .NET Framework 4.6.2 tests run on Windows only.

### Public API changes

A new or changed public member must be recorded in the package's `PublicAPI.Unshipped.txt`, or the build fails. `make api-check` updates the file; review the diff and commit it with the change. Every public member needs an XML doc comment.

### Strong naming

Release builds are strong-named. Without the private key (`LaunchDarkly.snk`, which is never committed) a Release build is public-signed with `LaunchDarkly.pk`, which is enough to build and test. Debug builds are not strong-named.
