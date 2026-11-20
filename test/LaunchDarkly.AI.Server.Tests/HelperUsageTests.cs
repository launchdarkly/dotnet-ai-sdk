using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using LaunchDarkly.Sdk;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The once-per-client <c>$ld:ai:sdk:usage</c> event for each public helper of the core package.
    /// </summary>
    public class HelperUsageTests : LdAiTestBase
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

        private static async Task CallAsync(string name, FakeLdClient fake)
        {
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            fake.ClearTracks();
            try
            {
                switch (name)
                {
                    case "Config.InvokeAsync":
                        // Built here, not for every row: building it reports client.createHandler,
                        // and each row must start from a client that has not reported its helper.
                        var handler = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages);
                        var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler });
                        fake.ClearTracks();
                        await config.InvokeAsync("hi", Fixtures.UserContext);
                        break;
                    case "LdAi.CreateHandler":
                        LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, _) => Task.FromResult(new HandlerResult()));
                        break;
                    case "LdAi.InspectConfigAsync":
                        await LdAi.InspectConfigAsync(Fixtures.ConfigKey, Fixtures.UserContext);
                        break;
                    default:
                        throw new Xunit.Sdk.XunitException($"No call is defined for usage-table row {name}.");
                }
            }
            catch (LdAiException)
            {
                // The report is the first statement, so a later failure still leaves the event.
            }
        }

        private static TrackedEvent[] Usage(FakeLdClient fake, string helper) =>
            fake.Events(SdkUsage).Where(e => e.DataString("helper") == helper).ToArray();

        [Fact]
        public void EveryRowNamesAnExportedMember()
        {
            var rows = UsageTable.Read();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.True(UsageTable.IsExported(typeof(LdAi).Assembly, row), row.Name));
        }

        [Fact]
        public void EachIdIsReportedByOneMember()
        {
            var ids = UsageTable.Read().SelectMany(r => r.Ids).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.All(ids, id => Assert.StartsWith("client.", id, StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task OneCallSendsExactlyItsOwnEvent(string name)
        {
            var row = UsageTable.Read().Single(r => r.Name == name);
            var fake = UseFakeClient();

            await CallAsync(name, fake);

            var events = fake.Events(SdkUsage);
            Assert.Equal(row.Ids.OrderBy(i => i, StringComparer.Ordinal), events.Select(e => e.DataString("helper")!).OrderBy(i => i, StringComparer.Ordinal));
            foreach (var e in events)
            {
                Assert.Equal(1.0, e.Metric);
                Assert.Equal(ContextKind.Of("ld_ai"), e.Context.Kind);
                Assert.Equal("ld-internal-tracking", e.Context.Key);
                Assert.True(e.Context.Anonymous);
                Assert.Equal("LaunchDarkly.AI.Server", e.DataString("aiSdkName"));
                Assert.Equal(Server.SdkInfo.Version, e.DataString("aiSdkVersion"));
                Assert.Equal("c#", e.DataString("aiSdkLanguage"));
                Assert.Equal(e.DataString("aiSdkName"), e.DataString("helperPackageName"));
                Assert.Equal(e.DataString("aiSdkVersion"), e.DataString("helperPackageVersion"));
            }
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

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task ShutdownRearmsReporting(string name)
        {
            var row = UsageTable.Read().Single(r => r.Name == name);
            var first = UseFakeClient();
            await CallAsync(name, first);
            await LdAi.ShutdownAsync();

            var second = UseFakeClient();
            await CallAsync(name, second);

            Assert.Single(Usage(second, row.Ids[0]));
        }

        [Fact]
        public async Task CallBeforeAClientExistsIsSentOnInit()
        {
            LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, _) => Task.FromResult(new HandlerResult()));

            var fake = await InitWithFakeAsync();

            Assert.Single(Usage(fake, "client.createHandler"));
        }

        [Fact]
        public void TestOnlyClientSetterDoesNotFlushHeldRecords()
        {
            LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, _) => Task.FromResult(new HandlerResult()));
            var fake = new FakeLdClient();

            Server.Internal.TestHooks.SetClient(fake);

            Assert.Empty(fake.Events(SdkUsage));
        }

        [Fact]
        public async Task ThrowingTrackDoesNotFailTheHelperAndIsNotRetried()
        {
            var fake = UseFakeClient();
            fake.TrackThrows = true;
            var handler = LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, _) => Task.FromResult(new HandlerResult()));
            Assert.NotNull(handler);

            fake.TrackThrows = false;
            LdAi.CreateHandler(new ProvidesFor("OpenAI", HandlerModes.Messages), (_, _) => Task.FromResult(new HandlerResult()));
            Assert.Empty(Usage(fake, "client.createHandler"));
        }

        [Fact]
        public async Task ExistingEventsGainNoUsageFields()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            var handler = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages, new System.Collections.Generic.List<HandlerRequest>(), "ok",
                Fixtures.Usage(("input_tokens", 3), ("output_tokens", 4)));
            await LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler }).InvokeAsync("hi", Fixtures.UserContext);

            var generation = fake.EventsStartingWith("$ld:ai:").Where(e => !e.Name.StartsWith("$ld:ai:sdk:", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(generation);
            foreach (var e in generation)
            {
                foreach (var key in new[] { "helper", "helperPackageName", "helperPackageVersion", "aiSdkName", "aiSdkVersion", "aiSdkLanguage" })
                {
                    Assert.False(e.HasData(key), $"{e.Name} carries {key}");
                }
            }
        }

        [Fact]
        public async Task ReportUsageDefaultsToTheCorePackage()
        {
            var fake = UseFakeClient();
            LdAi.ReportUsage("client.testOnlyHelper");

            var e = Assert.Single(Usage(fake, "client.testOnlyHelper"));
            Assert.Equal("LaunchDarkly.AI.Server", e.DataString("helperPackageName"));
            Assert.Equal(Server.SdkInfo.Version, e.DataString("helperPackageVersion"));
        }

        [Fact]
        public async Task ReportUsageCarriesTheCallersPackage()
        {
            var fake = UseFakeClient();
            LdAi.ReportUsage("other.helper", "Other.Package", "9.9.9");

            var e = Assert.Single(Usage(fake, "other.helper"));
            Assert.Equal("Other.Package", e.DataString("helperPackageName"));
            Assert.Equal("9.9.9", e.DataString("helperPackageVersion"));
            Assert.Equal("LaunchDarkly.AI.Server", e.DataString("aiSdkName"));
        }

        [Fact]
        public async Task CallsFromInsideTheSdkDoNotReport()
        {
            var fake = UseFakeClient();
            fake.Variations[Fixtures.ConfigKey] = Fixtures.EnabledConfig();
            var handler = Fixtures.RecordingHandler("OpenAI", HandlerModes.Messages);
            var config = LdAi.Config(new ConfigArgs { Key = Fixtures.ConfigKey, Handler = handler });
            fake.ClearTracks();

            await config.InvokeAsync("hi", Fixtures.UserContext, cancellationToken: CancellationToken.None);

            Assert.Empty(Usage(fake, "client.createHandler"));
            Assert.Empty(Usage(fake, "client.inspectConfig"));
        }
    }
}
