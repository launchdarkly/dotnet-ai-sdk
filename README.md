# LaunchDarkly AI SDK for .NET

> [!CAUTION]
> **Pre-1.0 working version, not yet published to NuGet; build from source.**
> The API may change at any point before 1.0. The first NuGet release will be 1.0.

The LaunchDarkly AI SDK for .NET lets server-side .NET applications use [AI Configs](https://docs.launchdarkly.com/home/ai-configs): manage prompts, models and tools in LaunchDarkly, target and roll them out per context, and track cost, latency and errors.

## Packages

| Package | What it is |
|---|---|
| [`LaunchDarkly.AI.Server`](src/LaunchDarkly.AI.Server) | The core SDK. |
| [`LaunchDarkly.AI.Server.ExtensionsAI`](src/LaunchDarkly.AI.Server.ExtensionsAI) | Runs AI Configs through any `Microsoft.Extensions.AI` `IChatClient`. |
| [`LaunchDarkly.AI`](src/LaunchDarkly.AI) | Convenience package that references `LaunchDarkly.AI.Server`. |

## What works so far

Nothing yet: the repository has its build, test and documentation tooling, and the packages have no public API. This list grows with each working version.

<!-- experimental-features:start -->
Experimental features: None.
<!-- experimental-features:end -->

## Supported .NET versions

- .NET 8.0 and .NET 10.0
- .NET Standard 2.0, which includes .NET Framework 4.6.2 and later

## Building from source

You need the .NET 10 SDK (see `global.json`); the .NET 8 runtime is needed to run the net8.0 tests.

```bash
make build    # Release build, warnings as errors
make test     # unit tests
make pack     # NuGet packages into nupkgs/
```

To use a local build in an application, add `nupkgs/` as a package source:

```bash
dotnet nuget add source "$(pwd)/nupkgs" --name launchdarkly-ai-local
```

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
