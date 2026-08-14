using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic6")]
    public class NprProbeWaterTests
    {
        [Test]
        public void DetailProbe_AverageSaturation_InArtBibleBand()
        {
            var tex = NprDetailProbeFactory.Build(128, seed: 0x4E5052);
            try
            {
                float sat = NprDetailProbeFactory.AverageSaturation(tex);
                Assert.GreaterOrEqual(sat, 0.20f);
                Assert.LessOrEqual(sat, 0.40f);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void DetailStrength_AttenuatesWithCameraDistance()
        {
            Assert.AreEqual(0.40f, NprDetailProbeFactory.DetailStrengthForCameraDistance(4f), 0.01f);
            Assert.AreEqual(0.20f, NprDetailProbeFactory.DetailStrengthForCameraDistance(9f), 0.01f);
            Assert.AreEqual(0f, NprDetailProbeFactory.DetailStrengthForCameraDistance(20f), 0.01f);
        }

        [Test]
        public void EarthMaterial_BindsDetailProbe()
        {
            var mat = NprMaterialFactory.CreateEarthMaterial();
            try
            {
                Assert.AreEqual("WorldSim_NprEarth", mat.name);
                if (mat.HasProperty("_DetailMap"))
                {
                    Assert.IsNotNull(mat.GetTexture("_DetailMap"));
                    Assert.AreEqual(NprDetailProbeFactory.TextureName, mat.GetTexture("_DetailMap").name);
                }
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void WaterMaterial_UsesWaterShaderOrFallback_AndWaterBlue()
        {
            var mat = NprMaterialFactory.CreateWaterMaterial();
            try
            {
                Assert.AreEqual("WorldSim_NprWater", mat.name);
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    Assert.AreEqual(NprDioramaPalette.WaterBlue.r, c.r, 0.02f);
                    Assert.AreEqual(NprDioramaPalette.WaterBlue.g, c.g, 0.02f);
                    Assert.AreEqual(NprDioramaPalette.WaterBlue.b, c.b, 0.02f);
                }
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void LandTile_HasAlphaOne_ForLandMask()
        {
            var land = new WorldTileData { IsLand = true, Biome = BiomeType.Grassland };
            Assert.AreEqual(1f, NprDioramaPalette.ColorForTile(land).a, 0.001f);
        }

        [Test]
        public void ProbeAndWaterFactories_DoNotChangeMonthlyHash()
        {
            var world = WorldState.CreateMinimalSlice(808);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            _ = NprDetailProbeFactory.GetOrCreate();
            _ = NprMaterialFactory.CreateEarthMaterial();
            _ = NprMaterialFactory.CreateWaterMaterial();
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }
    }
}
