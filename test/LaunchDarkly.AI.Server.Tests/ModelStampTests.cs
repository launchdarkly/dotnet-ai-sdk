using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// The pinned model config's key and version, copied from <c>_ldMeta</c> onto the tracking data
    /// and every tracked event.
    /// </summary>
    public class ModelStampTests : InvokeHarness
    {
        private async Task<ProviderResponse> InvokeWithMeta(JsonNode? modelKey, JsonNode? modelVersion, bool setKey = true, bool setVersion = true)
        {
            SetVariation(j =>
            {
                var meta = (JsonObject)j["_ldMeta"]!;
                if (setKey)
                {
                    meta["modelKey"] = modelKey;
                }

                if (setVersion)
                {
                    meta["modelVersion"] = modelVersion;
                }
            });
            return await InvokeAsync(Returning("ok", Fixtures.Usage(("input_tokens", 1), ("output_tokens", 1))));
        }

        private TrackedEvent[] AiEvents() =>
            Fake.EventsStartingWith("$ld:ai:").Where(e => !e.Name.StartsWith("$ld:ai:sdk:", System.StringComparison.Ordinal)).ToArray();

        [Fact]
        public async Task CopiesModelKeyAndVersion()
        {
            var response = await InvokeWithMeta("my-model", 3);

            Assert.Equal("my-model", response.TrackData.ModelKey);
            Assert.Equal(3, response.TrackData.ModelVersion);
            Assert.NotEmpty(AiEvents());
            Assert.All(AiEvents(), e =>
            {
                Assert.Equal("my-model", e.DataString("modelKey"));
                Assert.Equal(System.Text.Json.JsonValueKind.Number, e.DataJson.GetProperty("modelVersion").ValueKind);
                Assert.Equal(3, e.DataJson.GetProperty("modelVersion").GetInt32());
            });
        }

        [Fact]
        public async Task OmitsBothWhenAbsent()
        {
            var response = await InvokeWithMeta(null, null, setKey: false, setVersion: false);

            Assert.Null(response.TrackData.ModelKey);
            Assert.Null(response.TrackData.ModelVersion);
            Assert.All(AiEvents(), e =>
            {
                Assert.False(e.HasData("modelKey"));
                Assert.False(e.HasData("modelVersion"));
            });
        }

        [Fact]
        public async Task EmptyModelKeyIsAbsent()
        {
            var response = await InvokeWithMeta("", 1);
            Assert.Null(response.TrackData.ModelKey);
            Assert.All(AiEvents(), e => Assert.False(e.HasData("modelKey")));
        }

        public static TheoryData<string> NonStringKeys() => new TheoryData<string> { "5", "{}", "[]", "true" };

        [Theory]
        [MemberData(nameof(NonStringKeys))]
        public async Task NonStringModelKeyIsAbsent(string rawKey)
        {
            var response = await InvokeWithMeta(JsonNode.Parse(rawKey), 1);
            Assert.Null(response.TrackData.ModelKey);
        }

        public static TheoryData<string> MalformedVersions() => new TheoryData<string>
        {
            "\"abc\"", "1.5", "{}", "null", "true", "\"\"", "\"  \"", "\"1.5\"",
        };

        [Theory]
        [MemberData(nameof(MalformedVersions))]
        public async Task MalformedModelVersionIsOmittedAndTheCallProceeds(string rawVersion)
        {
            var response = await InvokeWithMeta("my-model", JsonNode.Parse(rawVersion));

            Assert.Equal("ok", response.Response);
            Assert.Null(response.TrackData.ModelVersion);
            Assert.All(AiEvents(), e => Assert.False(e.HasData("modelVersion")));
        }

        [Theory]
        [InlineData("\"3\"")]
        [InlineData("3.0")]
        [InlineData("3")]
        public async Task IntegralValuesAreAcceptedAsIntegers(string rawVersion)
        {
            var response = await InvokeWithMeta("my-model", JsonNode.Parse(rawVersion));
            Assert.Equal(3, response.TrackData.ModelVersion);
        }

        [Fact]
        public async Task StampsAreNotReadFromTheModelSection()
        {
            SetVariation(j =>
            {
                var model = (JsonObject)j["model"]!;
                model["modelKey"] = "wrong";
                model["modelVersion"] = 9;
            });
            var response = await InvokeAsync(Returning("ok"));
            Assert.Null(response.TrackData.ModelKey);
            Assert.Null(response.TrackData.ModelVersion);
        }

        [Fact]
        public async Task TrackDataCarriesTheConfigIdentity()
        {
            var response = await InvokeWithMeta("my-model", 3);

            Assert.Equal(Fixtures.ConfigKey, response.TrackData.ConfigKey);
            Assert.Equal("v1", response.TrackData.VariationKey);
            Assert.Equal(2, response.TrackData.Version);
            Assert.Equal("test-model", response.TrackData.ModelName);
            Assert.Equal("OpenAI", response.TrackData.ProviderName);
            Assert.True(System.Guid.TryParse(response.TrackData.RunId, out _));
        }
    }
}
