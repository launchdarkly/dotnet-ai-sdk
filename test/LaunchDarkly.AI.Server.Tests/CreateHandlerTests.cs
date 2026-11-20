using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Wrapping a handler function with its provider, mode, content-capture setting and provider list.
    /// </summary>
    public class CreateHandlerTests : LdAiTestBase
    {
        private static readonly Func<HandlerRequest, CancellationToken, Task<HandlerResult>> _fn =
            (_, _) => Task.FromResult(new HandlerResult { Output = "ok" });

        private static ProviderHandler Create(CreateHandlerOptions? options = null, string provider = "*", string mode = HandlerModes.Messages) =>
            options == null
                ? LdAi.CreateHandler(new ProvidesFor(provider, mode), _fn)
                : LdAi.CreateHandler(new ProvidesFor(provider, mode), _fn, options);

        [Theory]
        [InlineData(HandlerModes.Messages)]
        [InlineData(HandlerModes.Agent)]
        public void AttachesProvidesFor(string mode)
        {
            var handler = Create(provider: "MyProvider", mode: mode);
            Assert.Equal("MyProvider", handler.ProvidesFor.Provider);
            Assert.Equal(mode, handler.ProvidesFor.Mode);
        }

        [Fact]
        public async Task CallsTheFunctionWithTheRequestAndReturnsItsResult()
        {
            HandlerRequest? seen = null;
            using var cts = new CancellationTokenSource();
            CancellationToken seenToken = default;
            var result = new HandlerResult { Output = "out" };
            var handler = LdAi.CreateHandler(new ProvidesFor("P", HandlerModes.Messages), (r, ct) =>
            {
                seen = r;
                seenToken = ct;
                return Task.FromResult(result);
            });
            var request = new HandlerRequest(Fixtures.Config()) { UserInput = "hi" };

            Assert.Same(result, await handler.InvokeAsync(request, cts.Token));
            Assert.Same(request, seen);
            Assert.Equal(cts.Token, seenToken);
        }

        [Fact]
        public async Task DirectCallLatchesTheConfigureLock()
        {
            await Create().InvokeAsync(new HandlerRequest(Fixtures.Config()));
            Assert.Throws<ConfigureLockedException>(() => LdAi.Configure(new SdkOptions { CaptureContent = false }));
        }

        [Fact]
        public void EachCallReturnsAnIndependentHandler()
        {
            Assert.NotSame(Create(), Create());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExplicitCaptureIsAnOverride(bool value)
        {
            var handler = Create(new CreateHandlerOptions { CaptureContent = value });
            Assert.Equal(value, handler.CaptureContent);
            Assert.Equal(value, handler.EffectiveCaptureContent);
        }

        [Fact]
        public void OmittedCaptureIsUnsetAndReadAtUseTime()
        {
            var handler = Create();
            Assert.Null(handler.CaptureContent);
            Assert.True(handler.EffectiveCaptureContent);
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            Assert.False(handler.EffectiveCaptureContent);
        }

        [Fact]
        public void AssigningCaptureSetsTheOverrideOnly()
        {
            var handler = Create();
            handler.CaptureContent = false;
            Assert.False(handler.EffectiveCaptureContent);
            Assert.True(Create().EffectiveCaptureContent);
        }

        [Fact]
        public void RewrappingKeepsTheInnerOverrideAsOfWrapping()
        {
            var inner = Create(new CreateHandlerOptions { CaptureContent = false });
            var outer = LdAi.CreateHandler(new ProvidesFor("*", HandlerModes.Messages), inner);
            inner.CaptureContent = true;

            Assert.False(outer.CaptureContent);
        }

        [Fact]
        public void RewrappingAnUnsetHandlerStaysUnset()
        {
            var outer = LdAi.CreateHandler(new ProvidesFor("*", HandlerModes.Messages), Create());
            Assert.Null(outer.CaptureContent);
        }

        [Fact]
        public void ProvidersAreKeptOnceInFirstSeenOrder()
        {
            var handler = Create(new CreateHandlerOptions { Providers = new[] { "Bedrock", "Anthropic", "Bedrock" } });
            Assert.Equal(new[] { "Bedrock", "Anthropic" }, handler.Providers);
        }

        [Fact]
        public void ProvidersAreACopy()
        {
            var list = new List<string> { "Bedrock" };
            var handler = Create(new CreateHandlerOptions { Providers = list });
            list.Add("Anthropic");
            Assert.Equal(new[] { "Bedrock" }, handler.Providers);
        }

        [Fact]
        public void OmittedProvidersAreNull()
        {
            Assert.Null(Create().Providers);
        }

        [Fact]
        public void RewrappingKeepsTheInnerProviders()
        {
            var inner = Create(new CreateHandlerOptions { Providers = new[] { "Bedrock" } });
            var outer = LdAi.CreateHandler(new ProvidesFor("*", HandlerModes.Messages), inner);
            Assert.Equal(new[] { "Bedrock" }, outer.Providers);
        }

        public static TheoryData<string[]> BadProviderLists() => new TheoryData<string[]>
        {
            Array.Empty<string>(),
            new[] { "" },
            new[] { " Bedrock" },
            new[] { "Bedrock " },
            new[] { "Bedrock", "  " },
        };

        [Theory]
        [MemberData(nameof(BadProviderLists))]
        public void EmptyBlankOrPaddedProvidersAreRejected(string[] providers)
        {
            var ex = Assert.Throws<ArgumentException>(() => Create(new CreateHandlerOptions { Providers = providers }));
            Assert.Equal("providers", ex.ParamName);
        }
    }
}
