using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Process-wide defaults, content-capture precedence and the lock after the first invocation.
    /// </summary>
    public class ConfigureTests : LdAiTestBase
    {
        private static ProviderHandler Handler(bool? capture = null)
        {
            var calls = new List<HandlerRequest>();
            var h = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages, calls);
            return capture == null
                ? h
                : LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), h, new CreateHandlerOptions { CaptureContent = capture });
        }

        [Fact]
        public void CaptureIsOnWithoutConfigure()
        {
            Assert.True(Handler().EffectiveCaptureContent);
            Assert.True(new SdkOptions().CaptureContent);
        }

        [Fact]
        public void ConfigureAfterTheHandlerIsBuiltApplies()
        {
            var handler = Handler();
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            Assert.False(handler.EffectiveCaptureContent);
        }

        [Fact]
        public void SecondConfigureBeforeAnInvocationReplacesTheFirst()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            LdAi.Configure(new SdkOptions { CaptureContent = true });
            Assert.True(Handler().EffectiveCaptureContent);
        }

        [Fact]
        public void ExplicitFalseOnTheHandlerBeatsAConfiguredTrue()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = true });
            Assert.False(Handler(capture: false).EffectiveCaptureContent);
        }

        [Fact]
        public void ExplicitTrueOnTheHandlerBeatsAConfiguredFalse()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            Assert.True(Handler(capture: true).EffectiveCaptureContent);
        }

        [Fact]
        public void OmittedSettingStaysUnset()
        {
            var handler = Handler();
            Assert.Null(handler.CaptureContent);
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            Assert.Null(handler.CaptureContent);
        }

        [Fact]
        public async Task DifferentConfigureAfterAnInvocationIsRefused()
        {
            await Handler().InvokeAsync(new HandlerRequest(Fixtures.Config()));
            Assert.Throws<ConfigureLockedException>(() => LdAi.Configure(new SdkOptions { CaptureContent = false }));
        }

        [Fact]
        public async Task IdenticalConfigureAfterAnInvocationIsAccepted()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            await Handler().InvokeAsync(new HandlerRequest(Fixtures.Config()));
            LdAi.Configure(new SdkOptions { CaptureContent = false });
        }

        [Fact]
        public async Task InvocationThroughConfigLatchesTheLock()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() })
                .InvokeAsync("hi", Fixtures.UserContext);

            Assert.Throws<ConfigureLockedException>(() => LdAi.Configure(new SdkOptions { CaptureContent = false }));
        }

        [Fact]
        public async Task InspectConfigDoesNotLatchTheLock()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);

            LdAi.Configure(new SdkOptions { CaptureContent = false });
        }

        [Fact]
        public async Task ShutdownRestoresTheDefaultAndUnlocks()
        {
            LdAi.Configure(new SdkOptions { CaptureContent = false });
            await Handler().InvokeAsync(new HandlerRequest(Fixtures.Config()));
            await LdAi.ShutdownAsync();

            Assert.True(Handler().EffectiveCaptureContent);
            LdAi.Configure(new SdkOptions { CaptureContent = false });
        }
    }
}
