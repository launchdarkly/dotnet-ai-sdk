using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.Server;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Integrations;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// The examples' entry point: <c>dotnet run --project examples -- &lt;scenario&gt; &lt;flag-key&gt; "&lt;input&gt;"</c>.
    /// It connects the SDK, builds the registry and runs one scenario. Exit codes: 0 on success,
    /// 1 when the scenario fails (one <c>Error: …</c> line on stderr), 2 on a usage error.
    /// </summary>
    internal static class Program
    {
        private static readonly Dictionary<string, Func<Registry, string, string, Task>> _scenarios =
            new Dictionary<string, Func<Registry, string, string, Task>>(StringComparer.Ordinal)
            {
                ["agent"] = Scenarios.AgentAsync,
                ["history"] = Scenarios.HistoryAsync,
            };

        internal static async Task<int> Main(string[] args)
        {
            if (args.Length < 2 || !_scenarios.TryGetValue(args[0], out var scenario))
            {
                Console.Error.WriteLine(
                    $"usage: dotnet run --project examples -- <{string.Join("|", _scenarios.Keys)}> <flag-key> [input]");
                return 2;
            }

            var key = args[1];
            var input = string.Join(" ", args.Skip(2));
            try
            {
                await ConnectAsync();
                await scenario(Register.BuildRegistry(), key, input);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
            finally
            {
                await LdAi.ShutdownAsync();
            }
        }

        /// <summary>
        /// Initializes the AI client. With <c>LD_SDK_KEY</c> set, it connects to LaunchDarkly and
        /// evaluates your own flags. Without it, it runs offline: a LaunchDarkly client on the
        /// examples' test flags (<see cref="OfflineFlags"/>), passed in as your own client.
        /// </summary>
        private static async Task ConnectAsync()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LD_SDK_KEY")))
            {
                Console.Error.WriteLine("[examples] LaunchDarkly flags from LD_SDK_KEY");
                await LdAi.InitClientAsync(new InitOptions
                {
                    BaseUri = UriFromEnvironment("LD_BASE_URI"),
                    StreamUri = UriFromEnvironment("LD_STREAM_URI"),
                    EventsUri = UriFromEnvironment("LD_EVENTS_URI"),
                });
                return;
            }

            Console.Error.WriteLine("[examples] offline: built-in test flags (set LD_SDK_KEY to use your own)");
            var testData = TestData.DataSource();
            OfflineFlags.AddTo(testData);
            var ldClient = new LdClient(Configuration.Builder("offline-examples")
                .DataSource(testData)
                .Events(Components.NoEvents)
                .Logging(Components.Logging(Logs.ToWriter(Console.Error)).Level(LogLevel.Warn))
                .Build());
            await LdAi.InitClientAsync(ldClient);
        }

        private static Uri? UriFromEnvironment(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? null : new Uri(value);
        }
    }
}
