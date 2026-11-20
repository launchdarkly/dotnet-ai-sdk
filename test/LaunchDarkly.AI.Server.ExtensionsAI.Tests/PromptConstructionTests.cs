using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// How the handler builds the prompt from instructions or configured messages, variables and
    /// the user's input.
    /// </summary>
    public class PromptConstructionTests : HandlerHarness
    {
        [Fact]
        public async Task InstructionsBecomeTheSystemPromptAndInputTheLastUserTurn()
        {
            await RunAsync(Config("You are helpful."), "Hello");

            var request = Chat.LastRequest;
            Assert.Equal("You are helpful.", request.SystemText);
            Assert.Equal(("user", "Hello"), request.Turns.Last());
            Assert.Single(request.Turns);
        }

        [Fact]
        public async Task InstructionPlaceholdersAreResolvedAndUnknownOnesKept()
        {
            await RunAsync(Config("Hello {{name}}, {{missing}}"), "hi", Vars(("name", "Ann")));
            Assert.Equal("Hello Ann, {{missing}}", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task UnresolvedPlaceholderWithEmptyVariablesIsSentUnchanged()
        {
            await RunAsync(Config("Hello {{missing}}"), "hi", Vars());
            Assert.Equal("Hello {{missing}}", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task SystemMessagesAreCombinedIntoTheSystemPrompt()
        {
            await RunAsync(Config(null, Msg("system", "Rule one."), Msg("system", "Rule two."), Msg("assistant", "Hi there")), "Hello");

            var request = Chat.LastRequest;
            Assert.Contains("Rule one.", request.SystemText, System.StringComparison.Ordinal);
            Assert.Contains("Rule two.", request.SystemText, System.StringComparison.Ordinal);
            Assert.DoesNotContain(request.Turns, t => t.Text.Contains("Rule"));
        }

        [Fact]
        public async Task ConversationMessagesKeepTheirOrderBeforeTheInput()
        {
            await RunAsync(Config(null, Msg("user", "first"), Msg("assistant", "second")), "third");
            Assert.Equal(new[] { ("user", "first"), ("assistant", "second"), ("user", "third") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task PlaceholdersInSystemAndUserMessagesAreResolved()
        {
            await RunAsync(Config(null, Msg("system", "Talk like {{style}}."), Msg("user", "About {{topic}}"), Msg("assistant", "ok")), "go",
                Vars(("style", "a pirate"), ("topic", "boats")));

            Assert.Equal("Talk like a pirate.", Chat.LastRequest.SystemText);
            Assert.Equal(("user", "About boats"), Chat.LastRequest.Turns[0]);
        }

        [Fact]
        public async Task LastUserMessageCarriesTheInputSoNoTurnIsAppended()
        {
            await RunAsync(Config(null, Msg("system", "s"), Msg("user", "{{user_input}}")), "ignored as a turn", Vars(("user_input", "the question")));
            Assert.Equal(new[] { ("user", "the question") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task LastAssistantMessageMeansTheInputIsAppended()
        {
            await RunAsync(Config(null, Msg("user", "a"), Msg("assistant", "b")), "c");
            Assert.Equal(("user", "c"), Chat.LastRequest.Turns.Last());
            Assert.Equal(3, Chat.LastRequest.Turns.Count);
        }

        [Fact]
        public async Task OnlySystemMessagesMeansTheInputIsAppended()
        {
            await RunAsync(Config(null, Msg("system", "s")), "c");
            Assert.Equal(new[] { ("user", "c") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task EmptyInputOnTheInstructionsPathSendsAnEmptyUserTurn()
        {
            await RunAsync(Config("sys"), "");
            Assert.Equal(new[] { ("user", "") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task NullInputSendsEmptyContentNeverNull()
        {
            await RunAsync(Config("sys"), null);

            var user = Chat.LastRequest.Messages.Single(m => m.Role == ChatRole.User);
            Assert.Equal("", user.Text);
            Assert.NotEmpty(user.Contents);
            Assert.All(user.Contents.OfType<TextContent>(), c => Assert.NotNull(c.Text));
        }

        [Fact]
        public async Task EmptyInputAfterAnAssistantMessageAppendsAnEmptyUserTurn()
        {
            await RunAsync(Config(null, Msg("user", "a"), Msg("assistant", "b")), "");
            Assert.Equal(("user", ""), Chat.LastRequest.Turns.Last());
        }

        [Fact]
        public async Task EmptyInputAfterAUserMessageAppendsNothing()
        {
            await RunAsync(Config(null, Msg("user", "a")), "");
            Assert.Equal(new[] { ("user", "a") }, Chat.LastRequest.Turns);
        }

        [Fact]
        public async Task MessagesWinOverInstructionsInMessagesMode()
        {
            await RunAsync(Config("Do not send me.", Msg("system", "From messages."), Msg("assistant", "x")), "y");

            Assert.Equal("From messages.", Chat.LastRequest.SystemText);
            Assert.DoesNotContain("Do not send me.", Chat.LastRequest.SystemText, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task InstructionsAreTheFallbackWhenMessagesAreEmpty()
        {
            await RunAsync(Config("Fallback.") with { Messages = new ConfigMessage[0] }, "y");
            Assert.Equal("Fallback.", Chat.LastRequest.SystemText);
        }

        [Fact]
        public async Task ModelIdComesFromTheConfig()
        {
            await RunAsync(Config());
            Assert.Equal("test-model", Chat.LastRequest.Options!.ModelId);
        }
    }
}
