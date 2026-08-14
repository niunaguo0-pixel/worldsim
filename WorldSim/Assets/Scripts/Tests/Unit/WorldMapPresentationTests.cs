using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    public class WorldMapPresentationTests
    {
        // 球面 mesh 顶点预算 = (latSegments+1)*(lonSegments+1) = 91*181, 与 Low LOD 平面网格一致。
        // 真实重生产物用 UV 球面渲染全球地球, 不再用 simplified 占位。
        [Test]
        public void SnapshotAndMesh_HaveRealEarthLowModelBudget()
        {
            string root = Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");
            var world = new WorldState(1);
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent", StartRegionCenterLat = 33,
                StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(root, cfg, world);
            var snapshot = WorldMapViewSnapshot.Capture(world.Geography, world.Map.GeoDataBuild);
            var mesh = new WorldMapPresenter().BuildSphereMesh(snapshot);
            Assert.AreEqual(181 * 91, mesh.vertexCount, "Sphere mesh vertex budget must be 181*91");
            Assert.AreEqual(180 * 90 * 2, mesh.triangles.Length / 3, "Sphere mesh triangle count must be 180*90*2");
            Object.DestroyImmediate(mesh);
        }

        // Task 6: snapshot 携带 lock 派生 buildId (非旧 simplified id), 反映真实重生产物。
        [Test]
        public void Snapshot_CarriesLockDerivedBuildIdNotSimplified()
        {
            string root = Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");
            var world = new WorldState(1);
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent", StartRegionCenterLat = 33,
                StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(root, cfg, world);
            var snapshot = WorldMapViewSnapshot.Capture(world.Geography, world.Map.GeoDataBuild);
            Assert.IsTrue(snapshot.BuildId.StartsWith("geo-v1-", System.StringComparison.Ordinal),
                "snapshot buildId must be lock-derived: " + snapshot.BuildId);
            Assert.IsFalse(snapshot.BuildId.Contains("simplified"),
                "snapshot buildId must not be the legacy simplified id: " + snapshot.BuildId);
        }

        [Test]
        public void MissingBundleSnapshot_UsesExplicitErrorPlaceholder()
        {
            var snapshot = new WorldMapViewSnapshot { BundleAvailable = false, Error = "missing-test" };
            GameObject go = new WorldMapPresenter().Build(snapshot);
            Assert.AreEqual("WorldSim_GeoBundleError", go.name);
            Assert.IsNotNull(go.transform.Find("WorldSim_GeoBundleError_Label"));
            Object.DestroyImmediate(go);
        }
    }
}
