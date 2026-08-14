using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    public class Task5DiscoveryTests
    {
        // Task 6: 替换原 Task5 占位探针 (Assert.Pass) 为有意义的断言。
        // 验证已提交的派生包是真实重生产物 (非 simplified 占位), 且 manifest 携带 lock 派生 buildId。
        private static string GeoRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        [Test]
        public void Task5_CommittedBundleIsRealEarthNotSimplified()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            Assert.IsTrue(manifest.BuildId.StartsWith("geo-v1-", System.StringComparison.Ordinal), manifest.BuildId);
            Assert.IsFalse(manifest.Fidelity.Contains("simplified"),
                "committed bundle must be the real regenerated derivative, not a simplified placeholder: " + manifest.Fidelity);
            Assert.AreEqual(3, manifest.Chunks.Count, "Low/Mid/High chunks must all be present");
            Assert.AreEqual(3, manifest.Assets.Count, "political/probes/NOTICE assets must all be present");
            Assert.IsTrue(manifest.Assets.Exists(a => a.RelativePath == "political-2026.wgeo.gz"),
                "political asset must be the WSP1 binary");
        }

        [Test]
        public void Task5_SourcesLockSha256IsConsistentWithBuildId()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            Assert.AreEqual(64, manifest.SourcesLockSha256.Length, "sourcesLockSha256 must be 64 hex chars");
            Assert.IsTrue(manifest.SourcesLockSha256.StartsWith(
                manifest.BuildId.Substring("geo-v1-".Length),
                System.StringComparison.OrdinalIgnoreCase),
                "buildId suffix must equal the first 16 hex chars of sourcesLockSha256");
        }
    }
}
