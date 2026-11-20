using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// A variation's mode string selects one of the two handler modes.
    /// </summary>
    public class ModeNormalizationTests : InvokeHarness
    {
        private async Task<string> SelectedModeFor(string? mode)
        {
            SetVariation(mode: mode);
            var config = LdAi.Config(new ConfigArgs
            {
                Key = Fixtures.ConfigKey,
                Handlers = new[]
                {
                    Fixtures.RecordingHandler("OpenAI", HandlerModes.Agent, "agent"),
                    Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages, "messages"),
                },
            });
            return (string)(await config.InvokeAsync("hi", Fixtures.UserContext)).Response!;
        }

        [Theory]
        [InlineData("agent", "agent")]
        [InlineData("messages", "messages")]
        [InlineData("completion", "messages")]
        [InlineData("judge", "messages")]
        [InlineData("something-new", "messages")]
        [InlineData(null, "messages")]
        public async Task ModeSelectsTheHandlerMode(string? variationMode, string expected) =>
            Assert.Equal(expected, await SelectedModeFor(variationMode));
    }
}
