namespace WorldSim.Tests.Unit
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WorldSim.Narrative;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Serialization;

    [TestFixture]
    [Category("S6Narrative")]
    public class NarrativeS6Tests
    {
        [Test]
        public void Consume_BuildsReadableChronicleWithoutMutatingWorldHash()
        {
            var world = WorldState.CreateMinimalSlice(606);
            world.Events.Add(new SimEvent(1, SimEventCategory.War, 10, "civ.war.declared", 100));
            world.Events.Add(new SimEvent(1, SimEventCategory.War, 10, "civ.war.resolved", 1.2));
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            var engine = new EmergentNarrativeEngine();
            int added = engine.Consume(world.Events);

            Assert.GreaterOrEqual(added, 2);
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
            Assert.GreaterOrEqual(engine.EntryCount, 3); // 2 atomic + war arc composite

            bool sawComposite = false;
            bool sawTitle = false;
            for (int i = 0; i < engine.Chronicle.Count; i++)
            {
                var e = engine.Chronicle[i];
                if (e.IsComposite && e.TemplateId == "pattern.war.arc") sawComposite = true;
                if (e.Title == "战事爆发" || e.Title == "战事平息") sawTitle = true;
            }
            Assert.IsTrue(sawComposite);
            Assert.IsTrue(sawTitle);
        }

        [Test]
        public void Consume_IsIdempotentOnSameListGrowth()
        {
            var events = new List<SimEvent>
            {
                new SimEvent(2, SimEventCategory.Civ, 3, "civ.stability.warning", 0.2)
            };
            var engine = new EmergentNarrativeEngine();
            Assert.AreEqual(1, engine.Consume(events));
            Assert.AreEqual(0, engine.Consume(events));

            events.Add(new SimEvent(3, SimEventCategory.Era, 3, "civ.era.transition", 0.8));
            Assert.AreEqual(1, engine.Consume(events));
            Assert.AreEqual(2, engine.EntryCount);
        }

        [Test]
        public void NotableActors_RankBySignificanceScore()
        {
            var engine = new EmergentNarrativeEngine();
            engine.Consume(new[]
            {
                new SimEvent(1, SimEventCategory.Civ, 1, "civ.generation.milestone", 1),
                new SimEvent(2, SimEventCategory.War, 2, "civ.war.declared", 50),
                new SimEvent(3, SimEventCategory.Disaster, 2, "ecology.disaster", 40),
                new SimEvent(4, SimEventCategory.Civ, 1, "civ.generation.milestone", 1),
            });

            var top = engine.GetTopNotableActors(2);
            Assert.AreEqual(2, top.Count);
            Assert.AreEqual(2, top[0].SourceId);
            Assert.Greater(top[0].Score, top[1].Score);
        }

        [Test]
        public void Pattern_EraTurmoil_EmitsComposite()
        {
            var engine = new EmergentNarrativeEngine();
            engine.Consume(new[]
            {
                new SimEvent(9, SimEventCategory.Era, 7, "civ.era.transition", 0.9),
                new SimEvent(9, SimEventCategory.Civ, 7, "civ.stability.warning", 0.1),
            });

            bool found = false;
            for (int i = 0; i < engine.Chronicle.Count; i++)
            {
                if (engine.Chronicle[i].TemplateId == "pattern.era.turmoil")
                    found = true;
            }
            Assert.IsTrue(found);
        }

        [Test]
        public void Catalog_UnknownTemplate_FallsBackByCategory()
        {
            var entry = NarrativeTemplateCatalog.FromEvent(
                new SimEvent(5, SimEventCategory.Ecology, 99, "ecology.custom.signal", 3));
            Assert.AreEqual("生态纪事", entry.Title);
            Assert.IsTrue(entry.Body.Contains("ecology.custom.signal"));
        }
    }
}
