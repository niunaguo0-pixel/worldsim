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
            Assert.AreEqual(AccessibilitySettings.ParticleCapBalanced, AccessibilitySettings.EffectiveParticleCap);
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
        public void SuggestFromOs_IsFalseOnDesktopShell()
        {
            Assert.IsFalse(AccessibilitySettings.SuggestReduceMotionFromOs());
        }
    }
}
