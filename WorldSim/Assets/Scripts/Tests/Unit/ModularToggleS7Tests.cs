namespace WorldSim.Tests.Unit
{
    using NUnit.Framework;
    using WorldSim.ModularToggle;
    using WorldSim.Simulation.Civilization;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Serialization;
    using WorldSim.Simulation.Time;

    [TestFixture]
    [Category("S7ModularToggle")]
    public class ModularToggleS7Tests
    {
        [Test]
        public void MvpMinimal_DisablesOptionalCivilizationSubsystems()
        {
            var world = WorldState.CreateMinimalSlice(707);
            ModularToggleService.ApplyPreset(world, ModulePreset.MvpMinimal);
            Assert.IsFalse(ModularToggleService.IsEnabled(world, ModuleIds.TechTree));
            Assert.IsFalse(ModularToggleService.IsEnabled(world, ModuleIds.PoliticsStructure));
            Assert.IsFalse(ModularToggleService.IsEnabled(world, ModuleIds.MilitarySystem));
            Assert.IsFalse(ModularToggleService.IsEnabled(world, ModuleIds.GenerationInheritance));
        }

        [Test]
        public void AttachTo_WithSubsystemDefaults_EnablesTechAndPolitics()
        {
            var world = WorldState.CreateMinimalSlice(708);
            CivilizationSimEngine.AttachTo(world, applyAttachedSubsystemDefaults: true);
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.CivilizationV2));
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.TechTree));
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.PoliticsStructure));
        }

        [Test]
        public void AttachTo_PreservePlayerFacing_KeepsTechOff()
        {
            var world = WorldState.CreateMinimalSlice(709);
            ModularToggleService.ApplyPreset(world, ModulePreset.MvpMinimal);
            ModularToggleService.Set(world, ModuleIds.TechTree, false);
            CivilizationSimEngine.AttachTo(world, applyAttachedSubsystemDefaults: false);
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.CivilizationV2));
            Assert.IsFalse(ModularToggleService.IsEnabled(world, ModuleIds.TechTree));
        }

        [Test]
        public void TechModuleOff_DoesNotAdvanceAgricultureTech()
        {
            var world = WorldState.CreateMinimalSlice(710);
            CivilizationSimEngine.AttachTo(world, applyAttachedSubsystemDefaults: false);
            ModularToggleService.Set(world, ModuleIds.TechTree, false);
            double before = world.Civilization.Tech[0].agriculture;
            var orch = new SimOrchestrator(world);
            orch.AdvanceGameTime(TimeDriver.MONTH_SECONDS);
            Assert.AreEqual(before, world.Civilization.Tech[0].agriculture, 1e-9);
        }

        [Test]
        public void ModuleToggles_EnterMonthlyHash()
        {
            var a = WorldState.CreateMinimalSlice(711);
            var b = WorldState.CreateMinimalSlice(711);
            ModularToggleService.ApplyPreset(a, ModulePreset.MvpMinimal);
            ModularToggleService.ApplyPreset(b, ModulePreset.MvpMinimal);
            Assert.AreEqual(
                WorldStateSerializer.ComputeMonthlyHash(a),
                WorldStateSerializer.ComputeMonthlyHash(b));

            ModularToggleService.Set(b, ModuleIds.TechTree, true);
            Assert.AreNotEqual(
                WorldStateSerializer.ComputeMonthlyHash(a),
                WorldStateSerializer.ComputeMonthlyHash(b));
        }

        [Test]
        public void ApplyPlayerFacing_IgnoresEngineRails()
        {
            var world = WorldState.CreateMinimalSlice(712);
            ModularToggleService.ApplyPreset(world, ModulePreset.MvpMinimal);
            // 目录默认：ecology.v2 / civilization.v2 = true；玩家面板不能改引擎轨。
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.EcologyV2));
            ModularToggleService.ApplyPlayerFacing(world, new System.Collections.Generic.Dictionary<string, bool>
            {
                { ModuleIds.EcologyV2, false },
                { ModuleIds.TechTree, true }
            });
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.EcologyV2));
            Assert.IsTrue(ModularToggleService.IsEnabled(world, ModuleIds.TechTree));
        }
    }
}
