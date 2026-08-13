using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Slice;
using WorldSim.Simulation.Time;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic4")]
    public class TimePresentationEpic4Tests
    {
        [TestCase(0, 1, 1, TimeSeason.Spring)]
        [TestCase(2, 1, 3, TimeSeason.Spring)]
        [TestCase(3, 1, 4, TimeSeason.Summer)]
        [TestCase(8, 1, 9, TimeSeason.Autumn)]
        [TestCase(11, 1, 12, TimeSeason.Winter)]
        [TestCase(12, 2, 1, TimeSeason.Spring)]
        public void Snapshot_DerivesCalendarFromMonthIndex(
            int monthIndex,
            int expectedYear,
            int expectedMonth,
            TimeSeason expectedSeason)
        {
            var snapshot = new TimeViewSnapshot(
                monthIndex, 0, false, 1, 0, 0, 0, null);

            Assert.AreEqual(expectedYear, snapshot.GameYear);
            Assert.AreEqual(expectedMonth, snapshot.MonthOfYear);
            Assert.AreEqual(expectedSeason, snapshot.Season);
        }

        [Test]
        public void PausedOrchestrator_FreezesTimeAndSnapshot()
        {
            var world = WorldState.CreateMinimalSlice(401);
            var orchestrator = new SimOrchestrator(world);
            var model = new TimePresentationModel();
            orchestrator.SetPaused(true);

            orchestrator.Update(10f);
            var snapshot = model.Capture(world, 0);

            Assert.IsTrue(snapshot.IsPaused);
            Assert.AreEqual(0, snapshot.MonthIndex);
            Assert.AreEqual(0.0, world.Time.gameClock);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(5, true)]
        [TestCase(20, true)]
        public void SpeedCommand_ProjectsAllSupportedTiersAndHighSpeedHint(
            int speed,
            bool expectedHighSpeedHint)
        {
            var world = WorldState.CreateMinimalSlice(402);
            var orchestrator = new SimOrchestrator(world);
            orchestrator.SetSpeedMultiplier(speed);

            var snapshot = new TimePresentationModel().Capture(world, 0);

            Assert.AreEqual(speed, snapshot.SpeedMultiplier);
            Assert.AreEqual(expectedHighSpeedHint, snapshot.ShowHighSpeedHint);
        }

        [Test]
        public void EventCursor_ConsumesOnlyNewEvents()
        {
            var events = new List<SimEvent>
            {
                new SimEvent(0, SimEventCategory.Civ, 1, "first", 1)
            };
            var cursor = new TimeEventCursor();

            Assert.AreEqual(1, cursor.Consume(events).Count);
            Assert.AreEqual(0, cursor.Consume(events).Count);
            events.Add(new SimEvent(1, SimEventCategory.Civ, 2, "second", 2));
            var added = cursor.Consume(events);

            Assert.AreEqual(1, added.Count);
            Assert.AreEqual("second", added[0].templateId);
        }

        [Test]
        public void Snapshot_CopiesEventSliceAndAggregatesVisibleMetrics()
        {
            var world = WorldState.CreateMinimalSlice(403);
            world.Settlements.Add(new SettlementStub { stableId = 2, population = 25 });
            world.Resources.Add(new ResourceStub { stableId = 201, name = "Food", currentAmount = 7 });
            world.Events.Add(new SimEvent(0, SimEventCategory.Civ, 1, "before", 1));
            var snapshot = new TimePresentationModel().Capture(world, 3);

            world.Events[0] = new SimEvent(0, SimEventCategory.Civ, 1, "after", 1);

            Assert.AreEqual(125, snapshot.Population);
            Assert.AreEqual(57, snapshot.FoodReserve);
            Assert.AreEqual(3, snapshot.PendingCount);
            Assert.AreEqual("before", snapshot.Events[0].templateId);
        }

        [Test]
        public void GenerationTimeline_ConsumesReloadedEventsWithoutDuplicatingNodes()
        {
            var original = new List<SimEvent>
            {
                new SimEvent(4, SimEventCategory.Civ, 1, "civ.individual.death", 20),
                new SimEvent(4, SimEventCategory.Civ, 2, "civ.individual.inheritance", 1),
                new SimEvent(4, SimEventCategory.Chronicle, 2, "civ.generation.milestone", 1)
            };
            var reloaded = new List<SimEvent>(original);
            var presenter = new GenerationTimelinePresenter();

            Assert.AreEqual(3, presenter.Consume(original));
            Assert.AreEqual(0, presenter.Consume(original));
            Assert.AreEqual(0, presenter.Consume(reloaded));
            Assert.AreEqual(3, presenter.Nodes.Count);
            Assert.AreEqual(GenerationTimelineKind.Death, presenter.Nodes[0].Kind);
            Assert.AreEqual(GenerationTimelineKind.Inheritance, presenter.Nodes[1].Kind);
            Assert.AreEqual(GenerationTimelineKind.Milestone, presenter.Nodes[2].Kind);
        }
    }
}
