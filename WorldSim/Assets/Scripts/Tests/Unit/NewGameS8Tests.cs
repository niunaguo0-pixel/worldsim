namespace WorldSim.Tests.Unit
{
    using System.IO;
    using NUnit.Framework;
    using WorldSim.Simulation.WorldMap;
    using WorldSim.UI;

    [TestFixture]
    [Category("S8UI")]
    public class NewGameS8Tests
    {
        private static string PresetsPath()
        {
            string root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(root, "Assets", "StreamingAssets", "Data", "region-presets.json");
        }

        [Test]
        public void Assemble_ModernPreset_KeepsGeopoliticsAndLegalSeed()
        {
            var catalog = RegionPresetLoader.LoadFromFile(PresetsPath());
            var draft = new NewGameDraft
            {
                StartEra = StartEra.Modern,
                PresetKey = "fertile_crescent",
                BorderYear = 2026,
                UsePresetLegalBias = true,
                GoalMode = GoalMode.SandboxNoVictory
            };

            WorldInitConfig cfg = NewGameAssembler.Assemble(draft, catalog);
            Assert.AreEqual(StartMode.ModernGeopolitics, cfg.StartMode);
            Assert.AreEqual(2026, cfg.BorderYear);
            Assert.AreEqual("fertile_crescent", cfg.PresetKey);
            Assert.IsNotNull(cfg.LegalTraditionSeed);
            Assert.AreEqual(LegalFamilyBias.CustomaryLaw, cfg.LegalTraditionSeed.Bias);
            Assert.IsFalse(RegionPresetRedLines.HasPerPolityLawOrEthnicAssignment(cfg));
        }

        [Test]
        public void Assemble_Primordial_ClearsEthnicAndLegalSeeds()
        {
            var catalog = RegionPresetLoader.LoadFromFile(PresetsPath());
            var draft = new NewGameDraft
            {
                StartEra = StartEra.Primordial,
                PresetKey = "yellow_yangtze",
                BorderYear = 1945,
                GoalMode = GoalMode.MilestonePolity
            };

            WorldInitConfig cfg = NewGameAssembler.Assemble(draft, catalog);
            Assert.AreEqual(StartMode.PrimordialSandbox, cfg.StartMode);
            Assert.AreEqual(0, cfg.BorderYear);
            Assert.IsNull(cfg.EthnicDistribution);
            Assert.IsNull(cfg.LegalTraditionSeed);
            // GoalMode 不进 config：用草稿断言即可
            Assert.AreEqual(GoalMode.MilestonePolity, draft.GoalMode);
        }

        [Test]
        public void Assemble_LegalBiasOverride_ReplacesPresetMapping()
        {
            var catalog = RegionPresetLoader.LoadFromFile(PresetsPath());
            var draft = new NewGameDraft
            {
                StartEra = StartEra.Modern,
                PresetKey = "fertile_crescent",
                UsePresetLegalBias = false,
                LegalBiasOverride = LegalFamilyBias.CivilLaw
            };

            WorldInitConfig cfg = NewGameAssembler.Assemble(draft, catalog);
            Assert.IsNotNull(cfg.LegalTraditionSeed);
            Assert.AreEqual(LegalFamilyBias.CivilLaw, cfg.LegalTraditionSeed.Bias);
        }

        [Test]
        public void GoalModeLabel_CoversAllModes()
        {
            Assert.IsTrue(NewGameAssembler.GoalModeLabel(GoalMode.SandboxNoVictory).Contains("沙盒"));
            Assert.IsTrue(NewGameAssembler.GoalModeLabel(GoalMode.MilestonePolity).Contains("里程碑"));
            Assert.IsTrue(NewGameAssembler.GoalModeLabel(GoalMode.Custom).Contains("自定义"));
        }

        [Test]
        public void DescribeMode_ReflectsStartMode()
        {
            var geo = new WorldInitConfig { StartMode = StartMode.ModernGeopolitics, BorderYear = 1945 };
            var sand = new WorldInitConfig { StartMode = StartMode.PrimordialSandbox, BorderYear = 0 };
            Assert.IsTrue(NewGameAssembler.DescribeMode(geo).Contains("地缘"));
            Assert.IsTrue(NewGameAssembler.DescribeMode(sand).Contains("沙盒"));
        }
    }
}
