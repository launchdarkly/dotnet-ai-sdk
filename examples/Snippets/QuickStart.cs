// The code samples in the README files. Each sample is the text between a "snippet: <name>"
// comment and the next "end-snippet" comment; a sample made of several such spans is joined with
// a blank line. The documentation tests check that every C# block in the README files matches
// its sample here, so the documented code always compiles against the current API.

// snippet: quick-start
using System;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.AI.Server.ExtensionsAI;
using LaunchDarkly.Sdk;
using Microsoft.Extensions.AI;
// end-snippet

namespace LaunchDarkly.AI.Examples.Snippets
{
    internal static class QuickStart
    {
        internal static async Task RunAsync(IChatClient chatClient)
        {
            // snippet: quick-start
            // chatClient is any Microsoft.Extensions.AI IChatClient (OpenAI, Azure OpenAI, Ollama, ...).
            var config = LdAi.Config(new ConfigArgs
            {
                Key = "my-ai-config",
                Handler = ChatClientHandler.Create(chatClient),
            });

            // The first invocation initializes the SDK from the LD_SDK_KEY environment variable.
            var response = await config.InvokeAsync("What is a feature flag?", Context.New("user-123"));
            Console.WriteLine(response.Response);
            Console.WriteLine($"{response.Usage.Total} tokens, run {response.TrackData.RunId}");

            // On application exit: flush events and close the LaunchDarkly client.
            await LdAi.ShutdownAsync();
            // end-snippet
        }

        internal static async Task MultiProviderAsync(IChatClient openAiClient, IChatClient anthropicClient)
        {
            // snippet: multi-provider
            // One handler per provider. The variation's provider.name selects the handler,
            // so changing the provider in LaunchDarkly changes the client that runs.
            var config = LdAi.Config(new ConfigArgs
            {
                Key = "my-ai-config",
                Handlers = new[]
                {
                    ChatClientHandler.Create(openAiClient, new ChatClientHandlerOptions { Providers = new[] { "OpenAI" } }),
                    ChatClientHandler.Create(anthropicClient, new ChatClientHandlerOptions { Providers = new[] { "Anthropic" } }),
                },
            });

            var response = await config.InvokeAsync("Summarize our rollout policy.", Context.New("user-123"));
            // end-snippet
            Console.WriteLine(response.Response);
        }

        internal static async Task WrapperAsync(IChatClient chatClient)
        {
            // snippet: chat-client-messages
            // Build the handler and invoke the config in one call.
            var response = await ChatClientMessages.InvokeAsync(
                "my-ai-config",
                "What is a feature flag?",
                Context.New("user-123"),
                new ChatClientMessagesOptions { ChatClient = chatClient });
            // end-snippet
            Console.WriteLine(response.Response);
        }

        internal static ProviderHandler ClientFactory(Func<string, string, IChatClient> chatClientFor)
        {
            // snippet: client-factory
            // Choose the chat client per request, from the evaluated variation's provider and model.
            var handler = ChatClientHandler.Create((config, cancellationToken) =>
                new ValueTask<IChatClient>(chatClientFor(config.Provider.Name, config.Model.Name)));
            // end-snippet
            return handler;
        }
    }
}
