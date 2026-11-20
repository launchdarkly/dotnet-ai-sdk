# LaunchDarkly.AI

A convenience package for the LaunchDarkly AI SDK for .NET. It contains no code of its own: it references [`LaunchDarkly.AI.Server`](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/src/LaunchDarkly.AI.Server), so an application that references `LaunchDarkly.AI` uses the core package's types directly.

> [!CAUTION]
> **Pre-1.0 beta for testing. APIs may change before 1.0.**
> 0.x beta versions will be published to NuGet; none is published yet. Until the first release, build from source and install from a local `dotnet pack` feed.

## Install

Once a 0.x beta is published to NuGet, add it with `dotnet add package LaunchDarkly.AI` (pin the exact version: a 0.x minor release can change the API). Until then, build the packages from source (see the [repository README](https://github.com/launchdarkly/dotnet-ai-sdk#building-from-source)) and add the package from the local feed:

```bash
dotnet add package LaunchDarkly.AI --source /path/to/dotnet-ai-sdk/nupkgs
```

It brings in `LaunchDarkly.AI.Server` only. To run AI Configs on a `Microsoft.Extensions.AI` `IChatClient`, add [`LaunchDarkly.AI.Server.ExtensionsAI`](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/src/LaunchDarkly.AI.Server.ExtensionsAI) too.

## `ProvidesFor`

This package is not a handler and declares no `ProvidesFor`. The handler contract and `ProvidesFor` are in `LaunchDarkly.AI.Server`.

## Example

The types are the core package's, in the `LaunchDarkly.AI.Server` namespace. With the core package alone, an application writes its own handler and invokes a config with it:

<!-- snippet: custom-handler -->
```csharp
// A handler is a function from the evaluated request to the model's output and raw usage.
var handler = LdAi.CreateHandler(
    new ProvidesFor("Echo", HandlerModes.Messages),
    (request, cancellationToken) => Task.FromResult(new HandlerResult
    {
        Output = $"{request.Config.Model.Name} heard: {request.UserInput}",
        Usage = new Dictionary<string, object?> { ["input_tokens"] = 3, ["output_tokens"] = 4 },
    }));
```

<!-- snippet: core-invoke -->
```csharp
var config = LdAi.Config(new ConfigArgs { Key = "my-ai-config", Handler = handler });
var response = await config.InvokeAsync(
    "Hello",
    Context.New("user-123"),
    variables: new Dictionary<string, object?> { ["company"] = "Example Inc." });
Console.WriteLine(response.Response);
```

The [`LaunchDarkly.AI.Server` README](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/src/LaunchDarkly.AI.Server#examples) has the single-handler, multi-handler and registry examples.

## Environment variables

None of its own. `LaunchDarkly.AI.Server` reads `LD_SDK_KEY` when it initializes itself.

## API reference

The public API is `LaunchDarkly.AI.Server`'s: see its [API reference](https://github.com/launchdarkly/dotnet-ai-sdk/tree/main/src/LaunchDarkly.AI.Server#api-reference).

## Migration notes

None yet: 0.1.0 is the first beta.
