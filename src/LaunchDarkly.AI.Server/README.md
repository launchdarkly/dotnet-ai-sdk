# LaunchDarkly.AI.Server

The core of the LaunchDarkly AI SDK for .NET (server-side).

> [!CAUTION]
> This is a pre-1.0 working version. It is **not yet published to NuGet**: build it from source (see the [repository README](https://github.com/launchdarkly/dotnet-ai-sdk#readme)). The API may change before 1.0.

This package evaluates AI Configs for a LaunchDarkly context, runs them through a provider handler, and tracks the result (duration, tokens, success or error, tool calls) as LaunchDarkly metrics and `System.Diagnostics` activities.

## Status

The package builds and has no public API yet. Features are added one at a time before the 1.0 release; the [repository README](https://github.com/launchdarkly/dotnet-ai-sdk#readme) lists what works so far.

## Supported .NET versions

- .NET 8.0 and .NET 10.0
- .NET Standard 2.0, which includes .NET Framework 4.6.2 and later

## Learn more

- [LaunchDarkly AI Configs](https://docs.launchdarkly.com/home/ai-configs)
- [Contributing](https://github.com/launchdarkly/dotnet-ai-sdk/blob/main/CONTRIBUTING.md)
