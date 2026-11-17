using System.Collections.Generic;
using LaunchDarkly.AI.TestHelpers;
using LaunchDarkly.Sdk;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// <c>{{placeholder}}</c> substitution with <see cref="LdAi.ParseTemplate"/>.
    /// </summary>
    public class TemplateTests
    {
        private static IReadOnlyDictionary<string, object?> Vars(params (string Key, object? Value)[] entries)
        {
            var d = new Dictionary<string, object?>();
            foreach (var (k, v) in entries)
            {
                d[k] = v;
            }

            return d;
        }

        [Fact]
        public void SimpleSubstitution() =>
            Assert.Equal("Hello world", LdAi.ParseTemplate("Hello {{name}}", Vars(("name", "world"))));

        [Fact]
        public void MultiplePlaceholders() =>
            Assert.Equal("Hi Ann, you are 30", LdAi.ParseTemplate("Hi {{name}}, you are {{age}}", Vars(("name", "Ann"), ("age", "30"))));

        [Fact]
        public void UnknownPlaceholderIsPreserved() =>
            Assert.Equal("Hello {{missing}}", LdAi.ParseTemplate("Hello {{missing}}", Vars(("name", "world"))));

        [Fact]
        public void DotNotationReadsNestedDictionaries() =>
            Assert.Equal("Hi Alice", LdAi.ParseTemplate("Hi {{user.name}}", Vars(("user", new Dictionary<string, object?> { ["name"] = "Alice" }))));

        [Fact]
        public void PartialDotNotationMissIsPreserved() =>
            Assert.Equal("{{user.missing}}", LdAi.ParseTemplate("{{user.missing}}", Vars(("user", new Dictionary<string, object?>()))));

        [Fact]
        public void DeeplyNestedValue()
        {
            var vars = Vars(("a", new Dictionary<string, object?> { ["b"] = new Dictionary<string, object?> { ["c"] = 42 } }));
            Assert.Equal("42", LdAi.ParseTemplate("{{a.b.c}}", vars));
        }

        [Fact]
        public void DotNotationReadsJsonValues() =>
            Assert.Equal("Alice", LdAi.ParseTemplate("{{user.name}}", Vars(("user", Fixtures.Json("{\"name\":\"Alice\"}")))));

        [Theory]
        [InlineData(42, "42")]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        [InlineData(2.5, "2.5")]
        public void NonStringValuesAreCoercedToText(object value, string expected) =>
            Assert.Equal("v=" + expected, LdAi.ParseTemplate("v={{x}}", Vars(("x", value))));

        [Fact]
        public void EmptyVariablesSubstituteNothing() =>
            Assert.Equal("Hello {{name}}", LdAi.ParseTemplate("Hello {{name}}", new Dictionary<string, object?>()));

        [Fact]
        public void NullVariablesSubstituteNothing() =>
            Assert.Equal("Hello {{name}}", LdAi.ParseTemplate("Hello {{name}}", null));

        [Fact]
        public void TemplateWithoutPlaceholdersIsUnchanged() =>
            Assert.Equal("plain text", LdAi.ParseTemplate("plain text", Vars(("name", "world"))));

        [Fact]
        public void ContextAttributesResolveThroughDotNotation()
        {
            var context = Context.Builder("user-123").Kind("user").Set("email", "user@example.com").Build();
            var vars = Vars(("ldContext", context));

            Assert.Equal("user-123 user user@example.com",
                LdAi.ParseTemplate("{{ldContext.key}} {{ldContext.kind}} {{ldContext.email}}", vars));
        }
    }
}
