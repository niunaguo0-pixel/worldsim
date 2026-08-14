using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic6")]
    public class NprPolishGradeTests
    {
        [Test]
        public void SeasonTemperatures_MatchAssetSpec71()
        {
            Assert.AreEqual(8.1f, DioramaGradeMath.TemperatureForSeason(TimeSeason.Spring), 0.01f);
            Assert.AreEqual(2.7f, DioramaGradeMath.TemperatureForSeason(TimeSeason.Summer), 0.01f);
            Assert.AreEqual(-5.4f, DioramaGradeMath.TemperatureForSeason(TimeSeason.Autumn), 0.01f);
            Assert.AreEqual(-10.8f, DioramaGradeMath.TemperatureForSeason(TimeSeason.Winter), 0.01f);
        }

        [Test]
        public void GlobalBase_UsesSaturationMinus20_Contrast0()
        {
            var spring = DioramaGradeMath.SampleSeason(TimeSeason.Spring);
            Assert.AreEqual(-20f, spring.Saturation, 0.01f);
            Assert.AreEqual(0f, spring.Contrast, 0.01f);
            Assert.AreEqual(0.35f, spring.BloomIntensity, 0.01f);
            Assert.AreEqual(0.18f, spring.VignetteIntensity, 0.01f);
        }

        [Test]
        public void DroughtCompose_UsesFilter8B7D3C_AndLowerSaturation()
        {
            var full = DioramaGradeMath.Compose(TimeSeason.Summer, 1f);
            Assert.AreEqual(-35f, full.Saturation, 0.01f);
            Assert.AreEqual(-0.15f, full.PostExposure, 0.01f);
            Assert.AreEqual(0.22f, full.ColorFilterBlend, 0.01f);
            Assert.AreEqual(0x8B / 255f, full.ColorFilter.r, 0.01f);
            Assert.AreEqual(0x7D / 255f, full.ColorFilter.g, 0.01f);
            Assert.AreEqual(0x3C / 255f, full.ColorFilter.b, 0.01f);
        }

        [Test]
        public void As4_TransitionClampsAt20xToAtLeast1_5Seconds()
        {
            // MONTH_SECONDS=2 → 20× 下游戏月仅 0.1s，须钳到 1.5s
            float t = DioramaGradeMath.TransitionSeconds(TimeDriver.MONTH_SECONDS, 20, reduceMotion: false);
            Assert.AreEqual(1.5f, t, 0.01f);

            float reduced = DioramaGradeMath.TransitionSeconds(TimeDriver.MONTH_SECONDS, 20, reduceMotion: true);
            Assert.AreEqual(2.5f, reduced, 0.01f);

            float slow = DioramaGradeMath.TransitionSeconds(TimeDriver.MONTH_SECONDS, 1, reduceMotion: false);
            Assert.AreEqual(2f, slow, 0.01f);
        }

        [Test]
        public void As2Overlay_BuildsWarmPlateAndDeepStroke()
        {
            var host = new GameObject("As2Host");
            try
            {
                var overlay = As2HazardOverlay.EnsureOn(host);
                overlay.SetLabel("⚠ 旱灾前兆");
                overlay.SetVisible(true);
                Assert.IsTrue(overlay.IsVisible);
                Assert.IsTrue(overlay.CurrentLabel.Contains("旱灾"));

                var root = GameObject.Find(As2HazardOverlay.RootName);
                Assert.IsNotNull(root);
                var canvas = root.GetComponent<Canvas>();
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.GreaterOrEqual(canvas.sortingOrder, 1000);
            }
            finally
            {
                Object.DestroyImmediate(host);
                var leftover = GameObject.Find(As2HazardOverlay.RootName);
                if (leftover != null) Object.DestroyImmediate(leftover);
            }
        }

        [Test]
        public void NprMaterialFactory_ShaderSaturationNearOne_VolumeOwnsDesat()
        {
            var mat = NprMaterialFactory.CreateEarthMaterial();
            try
            {
                if (mat.HasProperty("_Saturation"))
                    Assert.AreEqual(0.95f, mat.GetFloat("_Saturation"), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void GradeMath_DoesNotChangeMonthlyHash()
        {
            var world = WorldState.CreateMinimalSlice(707);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            _ = DioramaGradeMath.Compose(TimeSeason.Winter, 0.4f);
            _ = DioramaGradeMath.TransitionSeconds(TimeDriver.MONTH_SECONDS, 20, false);
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }
    }
}
