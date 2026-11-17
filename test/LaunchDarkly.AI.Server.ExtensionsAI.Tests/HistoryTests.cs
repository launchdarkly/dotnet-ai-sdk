using System.Threading.Tasks;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Prior conversation turns: system from config, then config messages, then history, then the
    /// user's input.
    /// </summary>
    public class HistoryTests : HandlerHarness
    {
        [Fact]
        public async Task HistoryGoesBetweenConfigMessagesAndTheInput()
        {
            await RunAsync(Config(null, Msg("system", "s"), Msg("user", "c1"), Msg("assistant", "c2")), "now",
                history: new[] { Hist("user", "h1"), Hist("assistant", "h2") });

            Assert.Equal(new[] { ("user", "c1"), ("assistant", "c2"), ("user", "h1"), ("assistant", "h2"), ("user", "now") }, Chat.LastRequest.Turns);
            Assert.Equal("s", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task HistoryGoesBeforeTheInputOnTheInstructionsPath()
        {
            await RunAsync(Config("sys"), "now", history: new[] { Hist("user", "h1"), Hist("assistant", "h2") });
            Assert.Equal(new[] { ("user", "h1"), ("assistant", "h2"), ("user", "now") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task EmptyHistoryIsTheSameAsNoHistory()
        {
            await RunAsync(Config("sys"), "now", history: new HistoryMessage[0]);
            await RunAsync(Config("sys"), "now");

            Assert.Equal(Chat.Requests[1].Turns, Chat.Requests[0].Turns);
            Assert.Equal(Chat.Requests[1].SystemText, Chat.Requests[0].SystemText);
        }

        [Fact]
        public async Task SystemRoleHistoryIsDropped()
        {
            await RunAsync(Config("sys"), "now", history: new[] { Hist("system", "injected"), Hist("user", "h1") });

            Assert.Equal(new[] { ("user", "h1"), ("user", "now") }, Chat.LastRequest.Turns);
            Assert.DoesNotContain("injected", Chat.LastRequest.SystemText, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task NonEmptyInputIsAppendedEvenAfterAUserTurn()
        {
            await RunAsync(Config("sys"), "now", history: new[] { Hist("user", "h1") });
            Assert.Equal(new[] { ("user", "h1"), ("user", "now") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task EmptyInputAfterAUserTurnAppendsNothing()
        {
            await RunAsync(Config("sys"), "", history: new[] { Hist("assistant", "h0"), Hist("user", "h1") });
            Assert.Equal(new[] { ("assistant", "h0"), ("user", "h1") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task EmptyInputAfterAnAssistantTurnAppendsAnEmptyUserTurn()
        {
            await RunAsync(Config("sys"), "", history: new[] { Hist("user", "h1"), Hist("assistant", "h2") });
            Assert.Equal(("user", ""), Chat.LastRequest.Turns[Chat.LastRequest.Turns.Count - 1]);
        }
    }
}
