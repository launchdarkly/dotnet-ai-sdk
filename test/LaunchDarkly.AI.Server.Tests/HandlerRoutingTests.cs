using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Choosing the handler for a variation: exact provider, scoped wildcard, unscoped wildcard,
    /// and mode, from local handlers and a registry.
    /// </summary>
    public class HandlerRoutingTests : InvokeHarness
    {
        private static ProviderHandler H(string provider, string output, string mode = HandlerModes.Messages, params string[] providers) =>
            Fixtures.RecordingHandler(provider, mode, new List<HandlerRequest>(), output, null, providers.Length == 0 ? null : providers);

        private Task<ProviderResponse> RouteAsync(string provider, string? mode, ConfigArgs args)
        {
            SetVariation(provider: provider, mode: mode);
            return LdAi.Config(args).InvokeAsync("hi", Fixtures.UserContext);
        }

        private Task<ProviderResponse> RouteAsync(string provider, params ProviderHandler[] handlers) =>
            RouteAsync(provider, HandlerModes.Messages, new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = handlers });

        private async Task<string> RoutedTo(string provider, params ProviderHandler[] handlers) =>
            (string)(await RouteAsync(provider, handlers)).Response!;

        [Fact]
        public async Task SingleMatchingHandlerRuns()
        {
            SetVariation(provider: "OpenAI");
            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = H("OpenAI", "openai") })
                .InvokeAsync("hi", Fixtures.UserContext);
            Assert.Equal("openai", response.Response);
        }

        [Fact]
        public async Task SingleHandlerForAnotherProviderThrowsNamingTheProvider()
        {
            SetVariation(provider: "OpenAI");
            var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = H("Anthropic", "x") });
            var ex = await Assert.ThrowsAsync<NoHandlerException>(() => config.InvokeAsync("hi", Fixtures.UserContext));
            Assert.Contains("OpenAI", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SingleHandlerForAnotherModeThrows()
        {
            SetVariation(provider: "OpenAI", mode: "agent");
            var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = H("OpenAI", "x") });
            await Assert.ThrowsAsync<NoHandlerException>(() => config.InvokeAsync("hi", Fixtures.UserContext));
        }

        [Fact]
        public async Task SelectsByProviderAndMode()
        {
            Assert.Equal("anthropic", await RoutedTo("Anthropic", H("OpenAI", "openai"), H("Anthropic", "anthropic")));
        }

        [Fact]
        public async Task CompletionModeSelectsAMessagesHandler()
        {
            var response = await RouteAsync("SomeProvider", "completion",
                new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = new[] { H("SomeProvider", "agent", HandlerModes.Agent), H("SomeProvider", "messages") } });
            Assert.Equal("messages", response.Response);
        }

        [Fact]
        public async Task MissingProviderThrows()
        {
            SetVariation(j => j["provider"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "" });
            var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = new[] { H("*", "any") } });
            await Assert.ThrowsAnyAsync<LdAiException>(() => config.InvokeAsync("hi", Fixtures.UserContext));
        }

        [Fact]
        public async Task NoMatchAmongSeveralThrowsNamingTheProvider()
        {
            var ex = await Assert.ThrowsAsync<NoHandlerException>(() => RouteAsync("OpenAI", H("Anthropic", "a"), H("Gemini", "g")));
            Assert.Contains("OpenAI", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WildcardRunsWhenNoExactMatchExists()
        {
            Assert.Equal("any", await RoutedTo("Anthropic", H("*", "any")));
        }

        [Fact]
        public async Task ExactMatchWinsOverWildcard()
        {
            var calls = new List<HandlerRequest>();
            var wildcard = Fixtures.RecordingHandler("*", HandlerModes.Messages, calls, "any");
            Assert.Equal("openai", await RoutedTo("OpenAI", wildcard, H("OpenAI", "openai")));
            Assert.Empty(calls);
        }

        [Fact]
        public async Task WildcardDoesNotMatchAnotherMode()
        {
            await Assert.ThrowsAsync<NoHandlerException>(() =>
                RouteAsync("OpenAI", "agent", new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = new[] { H("*", "any") } }));
        }

        [Fact]
        public async Task NoExactMatchAndNoWildcardThrows()
        {
            await Assert.ThrowsAsync<NoHandlerException>(() => RouteAsync("OpenAI", H("Anthropic", "a")));
        }

        [Fact]
        public async Task ListedProviderSelectsAScopedWildcard()
        {
            Assert.Equal("bedrock", await RoutedTo("Bedrock", H("*", "bedrock", HandlerModes.Messages, "Bedrock")));
        }

        [Fact]
        public async Task UnlistedProviderThrowsTheNotFoundError()
        {
            var calls = new List<HandlerRequest>();
            var scoped = Fixtures.RecordingHandler("*", HandlerModes.Messages, calls, "x", null, new[] { "Bedrock" });
            var ex = await Assert.ThrowsAsync<NoHandlerException>(() => RouteAsync("OpenAI", scoped));
            Assert.Equal("Handler for provider OpenAI not found", ex.Message);
            Assert.Empty(calls);
        }

        [Fact]
        public async Task ProviderNamesAreComparedExactly()
        {
            await Assert.ThrowsAsync<NoHandlerException>(() => RouteAsync("bedrock", H("*", "x", HandlerModes.Messages, "Bedrock")));
        }

        [Fact]
        public async Task TwoScopedWildcardsDivideTheProviders()
        {
            var bedrock = H("*", "bedrock", HandlerModes.Messages, "Bedrock");
            var anthropic = H("*", "anthropic", HandlerModes.Messages, "Anthropic");
            Assert.Equal("bedrock", await RoutedTo("Bedrock", bedrock, anthropic));
            Assert.Equal("anthropic", await RoutedTo("Anthropic", bedrock, anthropic));
        }

        [Fact]
        public async Task ExactMatchWinsOverAScopedWildcard()
        {
            Assert.Equal("exact", await RoutedTo("OpenAI", H("*", "scoped", HandlerModes.Messages, "OpenAI"), H("OpenAI", "exact")));
        }

        [Fact]
        public async Task UnscopedWildcardIsTheFallback()
        {
            var scoped = H("*", "scoped", HandlerModes.Messages, "Bedrock");
            var unscoped = H("*", "unscoped");
            Assert.Equal("scoped", await RoutedTo("Bedrock", scoped, unscoped));
            Assert.Equal("unscoped", await RoutedTo("OpenAI", scoped, unscoped));
        }

        [Fact]
        public async Task ShorterListWinsWhenBothMatch()
        {
            Assert.Equal("one", await RoutedTo("Bedrock", H("*", "two", HandlerModes.Messages, "Bedrock", "Anthropic"), H("*", "one", HandlerModes.Messages, "Bedrock")));
        }

        [Fact]
        public async Task ListLengthCountsDistinctNames()
        {
            Assert.Equal("first", await RoutedTo("Bedrock", H("*", "first", HandlerModes.Messages, "Bedrock", "Bedrock"), H("*", "second", HandlerModes.Messages, "Bedrock")));
        }

        [Fact]
        public async Task EqualLengthOverlapKeepsTheEarlierHandler()
        {
            Assert.Equal("first", await RoutedTo("Bedrock", H("*", "first", HandlerModes.Messages, "Bedrock", "Gemini"), H("*", "second", HandlerModes.Messages, "Bedrock", "Anthropic")));
        }

        [Fact]
        public async Task ScopedWildcardStillNeedsTheMode()
        {
            await Assert.ThrowsAsync<NoHandlerException>(() =>
                RouteAsync("Bedrock", "agent", new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = new[] { H("*", "x", HandlerModes.Messages, "Bedrock") } }));
        }

        [Fact]
        public async Task ExactHandlerIgnoresAnAttachedList()
        {
            Assert.Equal("exact", await RoutedTo("OpenAI", H("OpenAI", "exact", HandlerModes.Messages, "Bedrock")));
        }

        [Fact]
        public async Task TwoUnscopedWildcardsInALocalListRunTheFirst()
        {
            Assert.Equal("first", await RoutedTo("OpenAI", H("*", "first"), H("*", "second")));
        }

        [Fact]
        public async Task RegistryHandlerIsUsedWithoutALocalOne()
        {
            var registry = new Registry(new[] { H("OpenAI", "registry") }, null);
            var response = await RouteAsync("OpenAI", HandlerModes.Messages, new ConfigArgs { Key = Fixtures.ConfigKey, Registry = registry });
            Assert.Equal("registry", response.Response);
        }

        [Fact]
        public async Task LocalHandlerWinsOverTheRegistry()
        {
            var registry = new Registry(new[] { H("OpenAI", "registry") }, null);
            var response = await RouteAsync("OpenAI", HandlerModes.Messages,
                new ConfigArgs { Key = Fixtures.ConfigKey, Handler = H("OpenAI", "local"), Registry = registry });
            Assert.Equal("local", response.Response);
        }

        [Fact]
        public async Task LocalHandlersArePlacedBeforeRegistryHandlers()
        {
            var registry = new Registry(new[] { H("*", "registry") }, null);
            var response = await RouteAsync("OpenAI", HandlerModes.Messages,
                new ConfigArgs { Key = Fixtures.ConfigKey, Handlers = new[] { H("*", "local") }, Registry = registry });
            Assert.Equal("local", response.Response);
        }

        [Fact]
        public async Task EmptyRegistryAndNoLocalHandlersThrows()
        {
            await Assert.ThrowsAsync<NoHandlerException>(() =>
                RouteAsync("OpenAI", HandlerModes.Messages, new ConfigArgs { Key = Fixtures.ConfigKey, Registry = new Registry() }));
        }
    }
}
