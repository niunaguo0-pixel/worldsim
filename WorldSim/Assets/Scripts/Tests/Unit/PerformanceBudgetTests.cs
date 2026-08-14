using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic8")]
    [Category("Perf")]
    public class PerformanceBudgetTests
    {
        private static string GeoRoot =>
            Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        [Test]
        public void T3_CoreFullyOpen_MedianMonthlyPass_UnderFiftyMilliseconds()
        {
            var world = MonthlyPassBudget.CreateCoreFullyOpen(0xE81301UL, settlementCount: 32);
            Assert.IsTrue(world.ModuleToggles["ecology.v2"]);
            Assert.IsTrue(world.ModuleToggles["civilization.v2"]);
            Assert.IsNotNull(world.InterventionSettler);
            Assert.GreaterOrEqual(world.Civilization.Settlements.Count, 32);

            double median = MonthlyPassBudget.MeasureMedianMonthMilliseconds(world);
            Assert.Less(
                median,
                MonthlyPassBudget.BudgetMilliseconds,
                "B6/T3: core-fully-open median month pass must be < 50ms, was " + median.ToString("F3") + "ms");
        }

        [Test]
        public void T3_CoreFullyOpen_WithRealGeo_MedianMonthlyPass_UnderFiftyMilliseconds()
        {
            var world = MonthlyPassBudget.CreateCoreFullyOpen(
                0xE81302UL, settlementCount: 24, geoRoot: GeoRoot);
            Assert.IsNotNull(world.Geography);

            double median = MonthlyPassBudget.MeasureMedianMonthMilliseconds(world);
            Assert.Less(
                median,
                MonthlyPassBudget.BudgetMilliseconds,
                "B6/T3+geo: median month pass must be < 50ms, was " + median.ToString("F3") + "ms");
        }

        [Test]
        public void T3_Fallback2_SerialFix_StillUnderBudget()
        {
            var world = MonthlyPassBudget.CreateCoreFullyOpen(0xE81303UL, settlementCount: 32);
            world.Fallback.SetLevel(DeterminismFallbackLevel.SerialFix);
            Assert.IsTrue(world.Fallback.ForceSerialPass);
            Assert.IsTrue(world.Fallback.UseFixForKeyQuantities);

            double median = MonthlyPassBudget.MeasureMedianMonthMilliseconds(world);
            Assert.Less(
                median,
                MonthlyPassBudget.BudgetMilliseconds,
                "B6 fallback-2 serial path must stay < 50ms, was " + median.ToString("F3") + "ms");
        }

        [Test]
        public void T3_Profiling_DoesNotChangeMonthlyHashSemantics()
        {
            var a = MonthlyPassBudget.CreateCoreFullyOpen(0xE81304UL, settlementCount: 16);
            var b = MonthlyPassBudget.CreateCoreFullyOpen(0xE81304UL, settlementCount: 16);
            var oa = new SimOrchestrator(a);
            var ob = new SimOrchestrator(b);
            for (int i = 0; i < 8; i++)
            {
                oa.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
                ob.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            }

            Assert.AreEqual(
                WorldStateSerializer.ComputeMonthlyHash(a),
                WorldStateSerializer.ComputeMonthlyHash(b));

            ulong before = WorldStateSerializer.ComputeMonthlyHash(a);
            MonthlyPassBudget.MeasureMedianMonthMilliseconds(a, warmupMonths: 0, sampleMonths: 4);
            var c = MonthlyPassBudget.CreateCoreFullyOpen(0xE81304UL, settlementCount: 16);
            var oc = new SimOrchestrator(c);
            for (int i = 0; i < 12; i++)
                oc.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.AreEqual(WorldStateSerializer.ComputeMonthlyHash(a), WorldStateSerializer.ComputeMonthlyHash(c));
            Assert.That(WorldStateSerializer.ComputeMonthlyHash(a), Is.Not.EqualTo(before));
        }
    }
}
