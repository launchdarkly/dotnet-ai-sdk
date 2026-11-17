using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Registering handlers and tools, replacing duplicates in place, and composing registries.
    /// </summary>
    public class RegistryTests : LdAiTestBase
    {
        private static ProviderHandler H(string provider, string mode = HandlerModes.Messages, params string[] providers) =>
            Fixtures.RecordingHandler(provider, mode, new List<HandlerRequest>(), "ok", null, providers.Length == 0 ? null : providers);

        private static ToolHandler T() => new FunctionTool((_, _) => Task.FromResult<object?>(null));

        private static Dictionary<string, ToolHandler> Tools(params (string Key, ToolHandler Tool)[] tools) =>
            tools.ToDictionary(t => t.Key, t => t.Tool);

        [Fact]
        public void NewRegistryIsEmpty()
        {
            var registry = new Registry();
            Assert.Empty(registry.Handlers);
            Assert.Empty(registry.Tools);
        }

        [Fact]
        public void ConstructorTakesInitialHandlersAndTools()
        {
            var h = H("OpenAI");
            var t = T();
            var registry = new Registry(new[] { h }, Tools(("t", t)));
            Assert.Same(h, Assert.Single(registry.Handlers));
            Assert.Same(t, registry.Tools["t"]);
        }

        [Fact]
        public void RegisterAppendsHandlers()
        {
            var a = H("OpenAI");
            var b = H("Anthropic");
            var registry = new Registry();
            registry.Register(new[] { a });
            registry.Register(new[] { b });
            Assert.Equal(new[] { a, b }, registry.Handlers);
        }

        [Fact]
        public void DuplicateHandlerWarnsAndKeepsTheSecond()
        {
            var first = H("OpenAI");
            var second = H("OpenAI");
            var registry = new Registry();
            registry.Register(new[] { first });
            registry.Register(new[] { second });

            Assert.Same(second, Assert.Single(registry.Handlers));
            Assert.Single(Warnings);
        }

        [Fact]
        public void ReplacementKeepsTheOriginalPosition()
        {
            var a = H("*", HandlerModes.Messages, "Bedrock", "Gemini");
            var b = H("*", HandlerModes.Messages, "Bedrock", "Anthropic");
            var c = H("*", HandlerModes.Messages, "Gemini", "Bedrock");
            var registry = new Registry();
            registry.Register(new[] { a, b, c });

            Assert.Equal(new[] { c, b }, registry.Handlers);
        }

        [Fact]
        public void ProviderSetIdentityIgnoresOrderAndRepeats()
        {
            var registry = new Registry();
            registry.Register(new[] { H("*", HandlerModes.Messages, "Bedrock", "Anthropic") });
            registry.Register(new[] { H("*", HandlerModes.Messages, "Anthropic", "Bedrock") });
            registry.Register(new[] { H("*", HandlerModes.Agent, "Bedrock") });
            registry.Register(new[] { H("*", HandlerModes.Agent, "Bedrock", "Bedrock") });

            Assert.Equal(2, registry.Handlers.Count);
            Assert.Equal(2, Warnings.Count);
        }

        [Fact]
        public void WarningNamesTheProviderSetSorted()
        {
            var registry = new Registry();
            registry.Register(new[] { H("*", HandlerModes.Messages, "Bedrock", "Anthropic") });
            registry.Register(new[] { H("*", HandlerModes.Messages, "Bedrock", "Anthropic") });

            var warning = Assert.Single(Warnings);
            Assert.True(warning.IndexOf("Anthropic", System.StringComparison.Ordinal) < warning.IndexOf("Bedrock", System.StringComparison.Ordinal), warning);
        }

        [Fact]
        public void TwoUnscopedWildcardsForOneModeCollide()
        {
            var registry = new Registry();
            registry.Register(new[] { H("*"), H("*") });
            Assert.Single(registry.Handlers);
            Assert.Single(Warnings);
        }

        [Fact]
        public void ScopedWildcardsWithDifferentListsBothStay()
        {
            var registry = new Registry();
            registry.Register(new[] { H("*", HandlerModes.Agent, "Bedrock"), H("*", HandlerModes.Agent, "Anthropic") });
            Assert.Equal(2, registry.Handlers.Count);
            Assert.Empty(Warnings);
        }

        [Fact]
        public void UnscopedWildcardStaysBesideAScopedOne()
        {
            var registry = new Registry();
            registry.Register(new[] { H("*", HandlerModes.Agent), H("*", HandlerModes.Agent, "Bedrock") });
            Assert.Equal(2, registry.Handlers.Count);
            Assert.Empty(Warnings);
        }

        [Fact]
        public void ExactHandlersCollideWhateverTheirProviderList()
        {
            var registry = new Registry();
            registry.Register(new[] { H("OpenAI"), H("OpenAI", HandlerModes.Messages, "Bedrock") });
            Assert.Single(registry.Handlers);
            Assert.Single(Warnings);
        }

        [Fact]
        public void SameProviderDifferentModeBothStay()
        {
            var registry = new Registry();
            registry.Register(new[] { H("OpenAI", HandlerModes.Messages), H("OpenAI", HandlerModes.Agent) });
            Assert.Equal(2, registry.Handlers.Count);
        }

        [Fact]
        public void RegisterAppendsTools()
        {
            var registry = new Registry();
            registry.Register(tools: Tools(("a", T())));
            registry.Register(tools: Tools(("b", T())));
            Assert.Equal(new[] { "a", "b" }, registry.Tools.Keys.OrderBy(k => k, System.StringComparer.Ordinal));
        }

        [Fact]
        public void DuplicateToolWarnsAndKeepsTheSecond()
        {
            var second = T();
            var registry = new Registry();
            registry.Register(tools: Tools(("t", T())));
            registry.Register(tools: Tools(("t", second)));

            Assert.Same(second, registry.Tools["t"]);
            Assert.Single(Warnings);
        }

        [Fact]
        public void RegisterIsAdditive()
        {
            var registry = new Registry(new[] { H("OpenAI") }, Tools(("a", T())));
            registry.Register(new[] { H("Anthropic") }, Tools(("b", T())));
            Assert.Equal(2, registry.Handlers.Count);
            Assert.Equal(2, registry.Tools.Count);
        }

        [Fact]
        public void ComposeReturnsANewRegistryAndLeavesItsInputs()
        {
            var a = new Registry(new[] { H("OpenAI") }, Tools(("a", T())));
            var b = new Registry(new[] { H("Anthropic") }, Tools(("b", T())));

            var c = Registry.Compose(a, b);

            Assert.NotSame(a, c);
            Assert.NotSame(b, c);
            Assert.Single(a.Handlers);
            Assert.Single(b.Handlers);
            Assert.Single(a.Tools);
            Assert.Single(b.Tools);
        }

        [Fact]
        public void ComposeLetsBWinAHandlerConflictInAsPosition()
        {
            var a1 = H("OpenAI");
            var a2 = H("Anthropic");
            var b1 = H("Gemini");
            var b2 = H("OpenAI");
            var c = Registry.Compose(new Registry(new[] { a1, a2 }, null), new Registry(new[] { b1, b2 }, null));

            Assert.Equal(new[] { b2, a2, b1 }, c.Handlers);
        }

        [Fact]
        public void ComposeKeepsDifferentProviderSets()
        {
            var a = H("*", HandlerModes.Messages, "Bedrock");
            var b = H("*", HandlerModes.Messages, "Anthropic");
            var c = Registry.Compose(new Registry(new[] { a }, null), new Registry(new[] { b }, null));
            Assert.Equal(new[] { a, b }, c.Handlers);
        }

        [Fact]
        public void ComposeLetsBWinAToolConflict()
        {
            var winner = T();
            var c = Registry.Compose(new Registry(null, Tools(("t", T()), ("a", T()))), new Registry(null, Tools(("t", winner), ("b", T()))));
            Assert.Same(winner, c.Tools["t"]);
            Assert.Equal(new[] { "a", "b", "t" }, c.Tools.Keys.OrderBy(k => k, System.StringComparer.Ordinal));
        }

        [Fact]
        public void ThereIsNoProcessWideRegistry()
        {
            var statics = typeof(LdAi).Assembly.GetExportedTypes()
                .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Select(p => p.PropertyType)
                    .Concat(t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Select(f => f.FieldType)));
            Assert.DoesNotContain(typeof(Registry), statics);
            Assert.Empty(new Registry().Handlers);
        }
    }
}
