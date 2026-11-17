using System.Linq;
using LaunchDarkly.AI.Server;
using Xunit;

namespace LaunchDarkly.AI.Tests
{
    /// <summary>
    /// The LaunchDarkly.AI metapackage exposes exactly the core package's types. That the package
    /// carries no assembly of its own is checked by the packaging check (make pack-check).
    /// </summary>
    public class MetapackageTests
    {
        [Fact]
        public void EveryPublicCoreTypeIsReachableThroughTheMetapackage()
        {
            var core = typeof(Config).Assembly;

            Assert.NotEmpty(CoreTypes.All);
            Assert.All(CoreTypes.All, t => Assert.Same(core, t.Assembly));
            Assert.Equal(
                core.GetExportedTypes().Select(t => t.FullName).OrderBy(n => n, System.StringComparer.Ordinal),
                CoreTypes.All.Select(t => t.FullName).OrderBy(n => n, System.StringComparer.Ordinal));
        }

        [Fact]
        public void ReExportedTypesAreTheCoreTypesThemselves()
        {
            Assert.Equal("LaunchDarkly.AI.Server", typeof(Config).Assembly.GetName().Name);
            Assert.Same(typeof(Config).Assembly, typeof(LdAi).Assembly);
            Assert.Same(typeof(Config).Assembly, typeof(NativeTool).Assembly);
            Assert.Same(typeof(Config).Assembly, typeof(Registry).Assembly);
        }

    }
}
