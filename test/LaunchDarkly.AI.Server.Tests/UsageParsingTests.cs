using System.Threading.Tasks;
using LaunchDarkly.AI.TestHelpers;
using Xunit;

namespace LaunchDarkly.AI.Server.Tests
{
    /// <summary>
    /// Normalizing a handler's raw token counts into <see cref="TokenUsage"/>.
    /// </summary>
    public class UsageParsingTests : InvokeHarness
    {
        private async Task<TokenUsage> UsageOf(params (string Key, object? Value)[] usage)
        {
            SetVariation();
            var response = await InvokeAsync(Returning("ok", Fixtures.Usage(usage)));
            return response.Usage;
        }

        private static void AssertCounts(TokenUsage usage, long input, long output)
        {
            Assert.Equal(input, usage.Input);
            Assert.Equal(output, usage.Output);
            Assert.Equal(input + output, usage.Total);
        }

        [Fact]
        public async Task SnakeCaseKeys() => AssertCounts(await UsageOf(("input_tokens", 10), ("output_tokens", 5)), 10, 5);

        [Fact]
        public async Task CamelCaseKeys() => AssertCounts(await UsageOf(("inputTokens", 10), ("outputTokens", 5)), 10, 5);

        [Fact]
        public async Task ShortKeys() => AssertCounts(await UsageOf(("input", 10), ("output", 5)), 10, 5);

        [Fact]
        public async Task TotalIsComputedNotTrusted() =>
            AssertCounts(await UsageOf(("input_tokens", 10), ("output_tokens", 5), ("total_tokens", 999), ("total", 999)), 10, 5);

        [Fact]
        public async Task UnknownKeysGiveZero() => AssertCounts(await UsageOf(("prompt", 10), ("completion", 5)), 0, 0);

        [Fact]
        public async Task NoUsageGivesZero()
        {
            SetVariation();
            var response = await InvokeAsync(Returning("ok", null));
            AssertCounts(response.Usage, 0, 0);
            Assert.Null(response.Usage.InputDetails);
        }

        [Fact]
        public async Task FirstCompletePairWins() =>
            AssertCounts(await UsageOf(("input_tokens", 10), ("output_tokens", 5), ("inputTokens", 1), ("outputTokens", 1)), 10, 5);

        [Fact]
        public async Task AHalfPairDoesNotWin() =>
            AssertCounts(await UsageOf(("input_tokens", 99), ("inputTokens", 10), ("outputTokens", 5)), 10, 5);

        [Theory]
        [InlineData("cache_read_input_tokens")]
        [InlineData("cache_creation_input_tokens")]
        [InlineData("cacheReadInputTokens")]
        [InlineData("cacheWriteInputTokens")]
        public async Task EachCacheSpellingFoldsIntoInput(string key) =>
            AssertCounts(await UsageOf(("input_tokens", 10), ("output_tokens", 5), (key, 7)), 17, 5);

        [Fact]
        public async Task CacheBreakdownIsReportedWhenPresent()
        {
            var usage = await UsageOf(("input_tokens", 10), ("output_tokens", 5), ("cache_read_input_tokens", 3), ("cache_creation_input_tokens", 2));
            AssertCounts(usage, 15, 5);
            Assert.NotNull(usage.InputDetails);
            Assert.Equal(10, usage.InputDetails!.Uncached);
            Assert.Equal(3, usage.InputDetails.CacheRead);
            Assert.Equal(2, usage.InputDetails.CacheCreation);
        }

        [Fact]
        public async Task NoCacheSpellingMeansNoBreakdown()
        {
            var usage = await UsageOf(("input_tokens", 10), ("output_tokens", 5));
            Assert.Null(usage.InputDetails);
        }

        [Fact]
        public async Task ZeroCacheSpellingStillGivesABreakdown()
        {
            var usage = await UsageOf(("input_tokens", 10), ("output_tokens", 5), ("cacheReadInputTokens", 0));
            Assert.NotNull(usage.InputDetails);
            Assert.Equal(0, usage.InputDetails!.CacheRead);
        }

        [Fact]
        public async Task NumericTypesOtherThanIntAreRead() =>
            AssertCounts(await UsageOf(("input_tokens", 10L), ("output_tokens", 5.0)), 10, 5);
    }
}
