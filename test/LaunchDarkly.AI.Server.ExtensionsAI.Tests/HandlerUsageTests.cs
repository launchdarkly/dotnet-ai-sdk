using System;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace LaunchDarkly.AI.Server.ExtensionsAI.Tests
{
    /// <summary>
    /// Token usage from the chat client, and the package's helper-usage events.
    /// </summary>
    public class HandlerUsageTests : HandlerHarness
    {
        private const string SdkUsage = "$ld:ai:sdk:usage";

        public static TheoryData<string> Rows()
        {
            var data = new TheoryData<string>();
            foreach (var row in UsageTable.Read())
            {
                data.Add(row.Name);
            }

            return data;
        }

        private async Task CallAsync(string name, FakeLdClient fake)
        {
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            fake.ClearTracks();
            switch (name)
            {
                case "ChatClientHandler.Create":
                    ChatClientHandler.Create(Chat);
                    break;
                case "ChatClientMessages.InvokeAsync":
                    await ChatClientMessages.InvokeAsync(Fixtures.ConfigKey, "hi", Fixtures.UserContext, new ChatClientMessagesOptions { ChatClient = Chat });
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"No call is defined for usage-table row {name}.");
            }
        }

        [Fact]
        public async Task ReportsInputIncludingCacheWithoutDoubleCounting()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            Chat.Then(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15, CachedInputTokenCount = 4 },
            });

            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal(10, response.Usage.Input);
            Assert.Equal(5, response.Usage.Output);
            Assert.Equal(15, response.Usage.Total);
        }

        [Fact]
        public async Task MissingUsageIsZero()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            Chat.ThenText("ok");

            var response = await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = Handler() }).InvokeAsync("hi", Fixtures.UserContext);

            Assert.Equal(0, response.Usage.Total);
            Assert.Empty(fake.EventsStartingWith("$ld:ai:tokens:"));
        }

        [Fact]
        public void EveryRowNamesAnExportedMember()
        {
            var rows = UsageTable.Read();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.True(UsageTable.IsExported(typeof(ChatClientHandler).Assembly, row), row.Name));
            Assert.All(rows.SelectMany(r => r.Ids), id => Assert.StartsWith("extensionsai-messages.", id, StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task OneCallSendsExactlyItsOwnEvent(string name)
        {
            var row = UsageTable.Read().Single(r => r.Name == name);
            var fake = UseFakeClient();

            await CallAsync(name, fake);

            var events = fake.Events(SdkUsage);
            Assert.Equal(row.Ids, events.Select(e => e.DataString("helper")));
            var e = events.Single();
            Assert.Equal(PackageInfo.Name, e.DataString("helperPackageName"));
            Assert.Equal(PackageInfo.Version, e.DataString("helperPackageVersion"));
            Assert.Equal("LaunchDarkly.AI.Server", e.DataString("aiSdkName"));
            Assert.Equal("c#", e.DataString("aiSdkLanguage"));
            Assert.Equal(1.0, e.Metric);
            Assert.Equal("ld-internal-tracking", e.Context.Key);
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task RepeatCallSendsNothing(string name)
        {
            var fake = UseFakeClient();
            await CallAsync(name, fake);
            await CallAsync(name, fake);
            Assert.Empty(fake.Events(SdkUsage));
        }

        [Fact]
        public async Task WrapperDoesNotReportTheFactoryOrConfigInvoke()
        {
            var fake = UseFakeClient();
            await CallAsync("ChatClientMessages.InvokeAsync", fake);

            var helpers = fake.Events(SdkUsage).Select(e => e.DataString("helper")).ToList();
            Assert.DoesNotContain("extensionsai-messages.createChatClientMessagesHandler", helpers);
            Assert.DoesNotContain("client.config.invoke", helpers);
            Assert.DoesNotContain("client.createHandler", helpers);
        }

        [Fact]
        public async Task FactoryDoesNotReportCreateHandler()
        {
            var fake = UseFakeClient();
            await CallAsync("ChatClientHandler.Create", fake);
            Assert.DoesNotContain(fake.Events(SdkUsage), e => e.DataString("helper") == "client.createHandler");
        }

        [Fact]
        public async Task ShutdownRearmsReporting()
        {
            var first = UseFakeClient();
            await CallAsync("ChatClientHandler.Create", first);
            await LdAi.ShutdownAsync();
            var second = UseFakeClient();
            await CallAsync("ChatClientHandler.Create", second);
            Assert.Single(second.Events(SdkUsage));
        }
    }
}
