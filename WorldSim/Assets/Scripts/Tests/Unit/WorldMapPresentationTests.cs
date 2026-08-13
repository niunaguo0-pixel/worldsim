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
            var mesh = new WorldMapPresenter().BuildMesh(snapshot);
            Assert.AreEqual(181 * 91, mesh.vertexCount);
            Assert.AreEqual(180 * 90 * 2, mesh.triangles.Length / 3);
            Object.DestroyImmediate(mesh);
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
