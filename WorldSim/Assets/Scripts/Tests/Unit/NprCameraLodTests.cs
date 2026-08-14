using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic6")]
    public class NprCameraLodTests
    {
        [Test]
        public void NprPalette_MatchesArtBibleHexForCoreRoles()
        {
            AssertColor(NprDioramaPalette.EarthOchre, 0xC4, 0xA3, 0x5A);
            AssertColor(NprDioramaPalette.Sienna, 0x8B, 0x5E, 0x3C);
            AssertColor(NprDioramaPalette.SageGreen, 0x5B, 0x8C, 0x5A);
            AssertColor(NprDioramaPalette.WaterBlue, 0x5E, 0x7A, 0x8C);
            AssertColor(NprDioramaPalette.DeepBrown, 0x3A, 0x2A, 0x1A);
            AssertColor(NprDioramaPalette.WarmGold, 0xD4, 0xA8, 0x4B);
        }

        [Test]
        public void ColorForTile_UsesWaterAndBiomePalette()
        {
            var water = new WorldTileData { IsLand = false };
            Color waterColor = NprDioramaPalette.ColorForTile(water);
            Assert.AreEqual(NprDioramaPalette.WaterBlue.r, waterColor.r, 0.001f);
            Assert.AreEqual(NprDioramaPalette.WaterBlue.g, waterColor.g, 0.001f);
            Assert.AreEqual(NprDioramaPalette.WaterBlue.b, waterColor.b, 0.001f);
            Assert.AreEqual(0f, waterColor.a, 0.001f);

            var desert = new WorldTileData { IsLand = true, Biome = BiomeType.Desert };
            Assert.AreEqual(NprDioramaPalette.EarthOchre, NprDioramaPalette.ColorForTile(desert));

            var forest = new WorldTileData { IsLand = true, Biome = BiomeType.TemperateForest };
            Assert.AreEqual(NprDioramaPalette.SageGreen, NprDioramaPalette.ColorForTile(forest));
        }

        [Test]
        public void CameraLod_ProvidesMeshPrecisionBudgetsPerLevel()
        {
            var near = CameraLodPolicy.ForLevel(CameraLodLevel.Individual);
            var far = CameraLodPolicy.ForLevel(CameraLodLevel.GenerationOverview);
            Assert.AreEqual(180, near.MeshLonSegments);
            Assert.AreEqual(90, near.MeshLatSegments);
            Assert.AreEqual(60, far.MeshLonSegments);
            Assert.AreEqual(30, far.MeshLatSegments);
            Assert.Greater(near.ElevationScale, far.ElevationScale);
            Assert.IsFalse(near.AllowAutoRotate);
            Assert.IsTrue(far.AllowAutoRotate);
        }

        [Test]
        public void Hysteresis_HoldsLevelNearBoundaryWhenZoomingOut()
        {
            // Settlement 上界 11；无迟滞在 11.01 会进 Civilization，有迟滞仍留 Settlement
            var held = CameraLodPolicy.EvaluateWithHysteresis(11.4f, CameraLodLevel.Settlement);
            Assert.AreEqual(CameraLodLevel.Settlement, held.Level);

            var crossed = CameraLodPolicy.EvaluateWithHysteresis(11.8f, CameraLodLevel.Settlement);
            Assert.AreEqual(CameraLodLevel.Civilization, crossed.Level);
        }

        [Test]
        public void BuildSphereMesh_RespectsLodSegmentBudget()
        {
            var snapshot = CaptureSnapshot();
            var decision = CameraLodPolicy.ForLevel(CameraLodLevel.Civilization);
            var mesh = WorldMapPresenter.BuildSphereMesh(
                snapshot, decision.MeshLonSegments, decision.MeshLatSegments, decision.ElevationScale);
            Assert.AreEqual((decision.MeshLatSegments + 1) * (decision.MeshLonSegments + 1), mesh.vertexCount);
            Assert.AreEqual(decision.MeshLonSegments * decision.MeshLatSegments * 2, mesh.triangles.Length / 3);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ApplyRenderLod_RebuildsMeshWhenLevelChanges_Only()
        {
            var snapshot = CaptureSnapshot();
            GameObject go = WorldMapPresenter.Build(snapshot);
            try
            {
                var presenter = go.GetComponent<WorldMapPresenter>();
                var filter = go.GetComponent<MeshFilter>();
                int fullVerts = filter.sharedMesh.vertexCount;
                Assert.AreEqual(CameraLodLevel.Individual, presenter.AppliedRenderLod);

                presenter.ApplyRenderLod(CameraLodPolicy.ForLevel(CameraLodLevel.GenerationOverview));
                Assert.AreEqual(CameraLodLevel.GenerationOverview, presenter.AppliedRenderLod);
                Assert.Less(filter.sharedMesh.vertexCount, fullVerts);

                int after = filter.sharedMesh.vertexCount;
                presenter.ApplyRenderLod(CameraLodPolicy.ForLevel(CameraLodLevel.GenerationOverview));
                Assert.AreEqual(after, filter.sharedMesh.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Build_UsesNprEarthMaterial()
        {
            var snapshot = CaptureSnapshot();
            GameObject go = WorldMapPresenter.Build(snapshot);
            try
            {
                var renderer = go.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer.sharedMaterial);
                Assert.AreEqual("WorldSim_NprEarth", renderer.sharedMaterial.name);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NprAndLodEvaluation_DoNotChangeMonthlyHash()
        {
            var world = WorldState.CreateMinimalSlice(606);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            NprDioramaPalette.ColorForTile(new WorldTileData { IsLand = true, Biome = BiomeType.Grassland });
            CameraLodPolicy.Evaluate(4f);
            CameraLodPolicy.EvaluateWithHysteresis(12f, CameraLodLevel.Settlement);
            CameraLodPolicy.ForLevel(CameraLodLevel.Civilization);
            _ = NprMaterialFactory.CreateEarthMaterial();

            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        private static WorldMapViewSnapshot CaptureSnapshot()
        {
            string root = Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");
            var world = new WorldState(1);
            var cfg = new WorldInitConfig
            {
                PresetKey = "fertile_crescent",
                StartRegionCenterLat = 33,
                StartRegionCenterLon = 44,
                StartRegionRadiusDeg = 8
            };
            WorldMapFactory.Build(root, cfg, world);
            return WorldMapViewSnapshot.Capture(world.Geography, world.Map.GeoDataBuild);
        }

        private static void AssertColor(Color c, byte r, byte g, byte b)
        {
            Assert.AreEqual(r / 255f, c.r, 0.002f);
            Assert.AreEqual(g / 255f, c.g, 0.002f);
            Assert.AreEqual(b / 255f, c.b, 0.002f);
        }
    }
}
