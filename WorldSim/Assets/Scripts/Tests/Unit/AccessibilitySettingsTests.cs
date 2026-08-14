using NUnit.Framework;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Accessibility")]
    public class AccessibilitySettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            AccessibilitySettings.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AccessibilitySettings.ResetForTests();
        }

        [Test]
        public void Default_ReduceMotion_IsOff()
        {
            AccessibilitySettings.Load();
            Assert.IsFalse(AccessibilitySettings.ReduceMotion);
            Assert.IsFalse(AccessibilitySettings.HighContrast);
            Assert.IsFalse(AccessibilitySettings.CvdMode);
            Assert.AreEqual(1f, AccessibilitySettings.FontScale, 0.001f);
            Assert.AreEqual(AccessibilitySettings.ParticleCapBalanced, AccessibilitySettings.EffectiveParticleCap);
            Assert.AreEqual(1f, AccessibilitySettings.CrisisPulseAmplitude, 0.001f);
            Assert.AreEqual(0f, AccessibilitySettings.LodCrossFadeSeconds, 0.001f);
        }

        [Test]
        public void SetReduceMotion_PersistsAcrossLoad()
        {
            AccessibilitySettings.SetReduceMotion(true);
            Assert.IsTrue(AccessibilitySettings.ReduceMotion);

            AccessibilitySettings.Load();
            Assert.IsTrue(AccessibilitySettings.ReduceMotion);
            Assert.AreEqual(
                AccessibilitySettings.ParticleCapReduceMotion,
                AccessibilitySettings.EffectiveParticleCap);
            Assert.AreEqual(0f, AccessibilitySettings.CrisisPulseAmplitude, 0.001f);
            Assert.AreEqual(
                AccessibilitySettings.LodCrossFadeReduceMotionSeconds,
                AccessibilitySettings.LodCrossFadeSeconds,
                0.001f);
        }

        [Test]
        public void HighContrastAndCvdAndFontScale_Persist()
        {
            AccessibilitySettings.SetHighContrast(true);
            AccessibilitySettings.SetCvdMode(true);
            AccessibilitySettings.SetFontScale(1.25f);
            AccessibilitySettings.Load();
            Assert.IsTrue(AccessibilitySettings.HighContrast);
            Assert.IsTrue(AccessibilitySettings.CvdMode);
            Assert.AreEqual(1.25f, AccessibilitySettings.FontScale, 0.001f);
            Assert.IsTrue(CvdPatternHook.IsActive);
            Assert.IsTrue(CvdPatternHook.ShouldForceIconText);
            Assert.AreEqual(0.18f, CvdPatternHook.PatternOverlayAlpha, 0.001f);
        }

        [Test]
        public void FontScale_ClampsToRange()
        {
            AccessibilitySettings.SetFontScale(0.1f);
            Assert.AreEqual(AccessibilitySettings.FontScaleMin, AccessibilitySettings.FontScale, 0.001f);
            AccessibilitySettings.SetFontScale(9f);
            Assert.AreEqual(AccessibilitySettings.FontScaleMax, AccessibilitySettings.FontScale, 0.001f);
        }

        [Test]
        public void ReduceMotion_ClampsAs4TransitionTo2_5SecondsAt20x()
        {
            float t = DioramaGradeMath.TransitionSeconds(
                TimeDriver.MONTH_SECONDS, 20, reduceMotion: true);
            Assert.AreEqual(DioramaGradeMath.ReduceMotionMinTransitionSeconds, t, 0.01f);
        }

        [Test]
        public void ApplyReduceMotion_HalvesBloom()
        {
            var baseGrade = DioramaGradeMath.SampleSeason(TimeSeason.Summer);
            float expected = baseGrade.BloomIntensity * 0.5f;
            var reduced = DioramaGradeMath.ApplyReduceMotion(baseGrade);
            Assert.AreEqual(expected, reduced.BloomIntensity, 0.01f);
        }

        [Test]
        public void ApplyHighContrast_SetsContrastSatVignetteAndBloom()
        {
            var baseGrade = DioramaGradeMath.SampleSeason(TimeSeason.Summer);
            var hc = DioramaGradeMath.ApplyHighContrast(baseGrade);
            Assert.AreEqual(12f, hc.Contrast, 0.01f);
            Assert.AreEqual(-30f, hc.Saturation, 0.01f);
            Assert.AreEqual(0f, hc.VignetteIntensity, 0.01f);
            Assert.AreEqual(baseGrade.BloomIntensity * 0.4f, hc.BloomIntensity, 0.01f);
        }

        [Test]
        public void SuggestFromOs_IsFalseOnDesktopShell()
        {
            Assert.IsFalse(AccessibilitySettings.SuggestReduceMotionFromOs());
        }
    }
}
