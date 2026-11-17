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

### API compatibility

`make api-compat BASE=<git ref>` (default `main`) builds the base ref in a temporary worktree and fails if the working tree breaks the public API compared with it (a removed or changed type or member, a renamed parameter, a dropped target framework). On a pull request, CI runs the same check against the base branch. If a break is intended, a maintainer adds the `api-compatible` label: the check then passes with a warning. Locally, `API_COMPATIBLE=true make api-compat` does the same.

### Packaging

`make pack-check` packs every package into a local feed, installs each one into a fresh console project from that feed and nuget.org, and builds and runs it on net8.0 and net10.0. It fails when a package's assembly needs something its declared dependencies don't provide, and it checks that `LaunchDarkly.AI` has no assembly of its own.

## For maintainers: repository settings

These are set in the GitHub repository and on nuget.org, not in code. They are needed before the first release (1.0); nothing is published before then.

- **`nuget-publish` environment** (Settings → Environments): required reviewers from the SDK maintainers, deployments limited to the `main` branch and release tags. The NuGet push job in `release.yml` runs only in this environment, so every push to NuGet waits for an approval.
- **NuGet trusted publishing** (nuget.org, under the publishing account): a trusted-publisher policy for the repository `launchdarkly/dotnet-ai-sdk`, workflow file `release.yml` and environment `nuget-publish`, covering the packages `LaunchDarkly.AI.Server`, `LaunchDarkly.AI.Server.ExtensionsAI` and `LaunchDarkly.AI`. The workflow logs in with the `NUGET_USER` repository variable through OIDC; no API key is stored.
- **Repository variables:** `NUGET_USER` (the nuget.org account) and `AWS_ROLE_ARN` (the role the release job assumes to fetch the signing credentials and the strong-name key).
- **The `api-compatible` label**, used by the API compatibility workflow.
- **GitHub Pages** from the `gh-pages` branch, for the API reference published by `publish-docs.yml`.
- **Required status checks** on `main` once the CI job names are settled: the build-and-test matrix, the checks job and the API compatibility job.
