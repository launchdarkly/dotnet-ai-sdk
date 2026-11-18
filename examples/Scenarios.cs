using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// One method per scenario. Each takes the registry <see cref="Register.BuildRegistry"/> built,
    /// the AI Config key and the user input.
    /// </summary>
    internal static class Scenarios
    {
        /// <summary>
        /// <c>agent</c>: binds the config to the registry, invokes it for a new context, prints the
        /// response JSON and writes it to <c>output/</c>.
        /// </summary>
        internal static async Task AgentAsync(Registry registry, string key, string input)
        {
            var config = LdAi.Config(new ConfigArgs { Key = key, Registry = registry });
            var response = await config.InvokeAsync(input, Utils.NewContext());
            Utils.PrintJson(response);
            Utils.WriteOutput(response);
        }

        /// <summary>
        /// <c>history</c>: the same config and call as <see cref="AgentAsync"/>, with prior
        /// conversation turns passed as history, so the model can refer back to them.
        /// </summary>
        internal static async Task HistoryAsync(Registry registry, string key, string input)
        {
            var config = LdAi.Config(new ConfigArgs { Key = key, Registry = registry });
            var response = await config.InvokeAsync(input, Utils.NewContext(), history: SampleHistory());
            Utils.PrintJson(response);
            Utils.WriteOutput(response);
        }

        /// <summary>A short earlier conversation for <see cref="HistoryAsync"/>.</summary>
        private static IReadOnlyList<HistoryMessage> SampleHistory() => new[]
        {
            new HistoryMessage(MessageRoles.User, "My name is Ada and I am evaluating feature flag tools."),
            new HistoryMessage(MessageRoles.Assistant, "Nice to meet you, Ada. What matters most in your evaluation?"),
            new HistoryMessage(MessageRoles.User, "Safe rollouts and experimentation."),
            new HistoryMessage(MessageRoles.Assistant, "Noted: safe rollouts and experimentation."),
        };
    }
}
