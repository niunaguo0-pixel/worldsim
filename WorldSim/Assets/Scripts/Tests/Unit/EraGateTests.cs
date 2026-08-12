// P1: S3 v1.4.4 EraGate — 禁绝对人口；TechTier/盈余/利用率/制度 stub.

using NUnit.Framework;
using WorldSim.Simulation.Core.Slice;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class EraGateTests
    {
        [Test]
        public void EraGate_DoesNotUseAbsolutePopulation()
        {
            // 同利用率/科技，人口差两个数量级仍同判定
            var small = MakePolity(pop: 50, tech: 2, surplus: 6, util: 0.50, div: 1, law: 0, writing: false);
            var large = MakePolity(pop: 50000, tech: 2, surplus: 6, util: 0.50, div: 1, law: 0, writing: false);
            Assert.IsTrue(EraGate.TryGetNextGate(0, out var g0));
            Assert.AreEqual(EraGate.Meets(small, g0), EraGate.Meets(large, g0),
                "绝对人口不得改变时代门闩");
        }

        [Test]
        public void EraGate_RequiresTechSurplusUtilization_NotBareHeadcount()
        {
            Assert.IsTrue(EraGate.TryGetNextGate(0, out var g0));
            var lowUtil = MakePolity(pop: 99999, tech: 2, surplus: 6, util: 0.10, div: 1, law: 0, writing: false);
            Assert.IsFalse(EraGate.Meets(lowUtil, g0), "高绝对人口+低利用率不得晋级");

            var ready = MakePolity(pop: 80, tech: 2, surplus: 6, util: 0.50, div: 1, law: 0, writing: false);
            Assert.IsTrue(EraGate.Meets(ready, g0));
        }

        [Test]
        public void EraGate_SpecsHaveNoRequiredPopulationField()
        {
            // 编译期/反射：EraGateSpec 不得含 requiredPopulation
            var fields = typeof(EraGateSpec).GetFields();
            foreach (var f in fields)
                StringAssert.DoesNotContain("population", f.Name.ToLowerInvariant(),
                    "EraGateSpec 不得含人口字段: " + f.Name);
        }

        private static PolityStub MakePolity(
            double pop, int tech, int surplus, double util, int div, int law, bool writing) =>
            new PolityStub
            {
                stableId = 1,
                name = "T",
                population = pop,
                techTier = tech,
                sustainedSurplusMonths = surplus,
                capacityUtilization = util,
                divisionDepth = div,
                lawStage = law,
                hasWriting = writing
            };
    }
}
