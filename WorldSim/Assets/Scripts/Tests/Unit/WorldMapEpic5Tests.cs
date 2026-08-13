using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Civilization;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    public class WorldMapEpic5Tests
    {
        private static string GeoRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        [TestCase(-180.0)]
        [TestCase(180.0)]
        [TestCase(540.0)]
        [TestCase(-540.0)]
        public void Projection_AntimeridianCloses(double longitude)
        {
            int expected = EquirectangularProjection.ToTileId(new GeoCoordinate(0, -180), MapLodLevel.Low);
            Assert.AreEqual(expected,
                EquirectangularProjection.ToTileId(new GeoCoordinate(0, longitude), MapLodLevel.Low));
        }

        [TestCase(MapLodLevel.Low, 180, 90)]
        [TestCase(MapLodLevel.Mid, 360, 180)]
        [TestCase(MapLodLevel.High, 720, 360)]
        public void Projection_RoundTripsStableTileId(MapLodLevel lod, int width, int height)
        {
            int id = EquirectangularProjection.ToTileId(new GeoCoordinate(34, 110), lod);
            Assert.AreEqual(id, EquirectangularProjection.ToTileId(
                EquirectangularProjection.ToCoordinate(id), lod));
            Assert.AreEqual(width, EquirectangularProjection.Width(lod));
            Assert.AreEqual(height, EquirectangularProjection.Height(lod));
        }

        [Test]
        public void Manifest_HasPinnedBuildAndAllLods()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            Assert.AreEqual("geo-v1-simplified-real-samples-20260813", manifest.BuildId);
            Assert.AreEqual("simplified-real-earth-fixed-samples-not-full-source", manifest.Fidelity);
            Assert.AreEqual(3, manifest.Chunks.Count);
            foreach (var chunk in manifest.Chunks)
            {
                string path = Path.Combine(GeoRoot, chunk.RelativePath);
                Assert.Less(new FileInfo(path).Length, 100L * 1024 * 1024);
                Assert.DoesNotThrow(() => WorldMapFactory.VerifyChecksum(path, chunk.Checksum));
            }
        }

        [TestCase("low-global.wgeo.gz", MapLodLevel.Low, 16200)]
        [TestCase("mid-global.wgeo.gz", MapLodLevel.Mid, 64800)]
        [TestCase("high-global.wgeo.gz", MapLodLevel.High, 259200)]
        public void Bundle_ReadsExpectedGlobalGrid(string file, MapLodLevel lod, int count)
        {
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, file));
            Assert.AreEqual(lod, bundle.Lod);
            Assert.AreEqual(count, bundle.Tiles.Count);
            Assert.AreEqual("geo-v1-simplified-real-samples-20260813", bundle.BuildId);
        }

        [Test]
        public void FixedBiomeProbes_MeetEightyPercent()
        {
            var bundle = WorldMapBundleReader.ReadBundle(Path.Combine(GeoRoot, "high-global.wgeo.gz"));
            int total = 0, correct = 0;
            foreach (string raw in File.ReadAllLines(Path.Combine(GeoRoot, "biome-probes.tsv")))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] p = raw.Split('\t');
                var coordinate = new GeoCoordinate(D(p[1]), D(p[2]));
                int id = EquirectangularProjection.ToTileId(coordinate, MapLodLevel.High);
                total++;
                if (bundle.Tiles[id].Biome.ToString() == p[3]) correct++;
            }
            Assert.GreaterOrEqual(total, 20);
            Assert.GreaterOrEqual(correct / (double)total, 0.80,
                $"biome probes {correct}/{total}");
        }

        [Test]
        public void Geography_StartHighAndGlobalLowAreSynchronous()
        {
            var world = new WorldState(1);
            var cfg = Config(StartEra.Primordial);
            var result = WorldMapFactory.Build(GeoRoot, cfg, world);
            Assert.AreEqual(MapLodLevel.High,
                result.Geography.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High).Lod);
            Assert.AreEqual(MapLodLevel.Low,
                result.Geography.GetTile(new GeoCoordinate(-20, -60), MapLodLevel.High).Lod);
            Assert.AreSame(result.Geography, world.Geography);
        }

        [Test]
        public void Geography_NaturalBoundariesAndSettlementSites()
        {
            var result = WorldMapFactory.Build(GeoRoot, Config(StartEra.Primordial));
            var nile = result.Geography.GetTile(new GeoCoordinate(26, 31), MapLodLevel.High);
            Assert.IsTrue(result.Geography.HasRiver(nile.TileId) || result.Geography.HasWaterNearby(nile.TileId));
            var score = SettlementSiteEvaluator.Evaluate(result.Geography, nile.TileId);
            Assert.IsTrue(score.IsHabitable, score.Reason);
            Assert.Greater(result.Geography.GetCoastBoundaryTiles().Count, 0);
            Assert.Greater(result.Geography.GetMountainBoundaryTiles(2200).Count, 0);
        }

        [Test]
        public void WorldStart_PrimordialHasNoModernSeedsOrBorders()
        {
            var cfg = Config(StartEra.Primordial);
            cfg.EthnicDistribution = new RealEthnicDistribution();
            cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = LegalFamilyBias.CivilLaw };
            var start = WorldStartFactory.Create(7, cfg, GeoRoot);
            Assert.AreEqual(StartMode.PrimordialSandbox, start.Config.StartMode);
            Assert.IsNull(start.Config.EthnicDistribution);
            Assert.IsNull(start.Config.LegalTraditionSeed);
            Assert.IsNull(start.GeoPolitical);
            Assert.AreEqual(1, start.World.Civilization.Settlements.Count);
        }

        [Test]
        public void WorldStart_ModernUsesSharedGeographyAndStableOrdering()
        {
            var cfg = Config(StartEra.Modern);
            cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = LegalFamilyBias.SocialistLaw };
            var a = WorldStartFactory.Create(8, cfg, GeoRoot);
            var b = WorldStartFactory.Create(8, Config(StartEra.Modern), GeoRoot);
            Assert.IsNotNull(a.GeoPolitical);
            Assert.AreEqual(12, a.GeoPolitical.Countries.Count);
            Assert.Greater(a.World.Civilization.Settlements.Count, 0);
            Assert.IsNotNull(a.World.Geography);
            Assert.AreEqual(a.World.Civilization.Settlements[0].stableId,
                b.World.Civilization.Settlements[0].stableId);
            Assert.AreEqual(LawFamily.SocialistLaw, a.World.Civilization.Polities[0].lawFamily);
        }

        [Test]
        public void UnsupportedHistoricalBorderYear_DoesNotMasqueradeAs2026()
        {
            var cfg = Config(StartEra.EarlyModern);
            cfg.BorderYear = 1914;
            var ex = Assert.Throws<NotSupportedException>(() => WorldStartFactory.Create(9, cfg, GeoRoot));
            StringAssert.Contains("only", ex.Message);
        }

        [Test]
        public async Task PresentationChunkCache_CompletionCannotChangeSimulationHash()
        {
            var world = new WorldState(10);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            var cache = new WorldMapChunkCache();
            await cache.LoadPresentationAsync("mid",
                Path.Combine(GeoRoot, "mid-global.wgeo.gz"));
            ulong after = WorldStateSerializer.ComputeMonthlyHash(world);
            Assert.AreEqual(before, after);
            Assert.IsTrue(cache.TryGetPresentationChunk("mid", out var bundle));
            Assert.AreEqual(MapLodLevel.Mid, bundle.Lod);
        }

        [TestCase(LegalFamilyBias.CivilLaw, LawFamily.CivilLaw)]
        [TestCase(LegalFamilyBias.CommonLaw, LawFamily.CommonLaw)]
        [TestCase(LegalFamilyBias.SocialistLaw, LawFamily.SocialistLaw)]
        [TestCase(LegalFamilyBias.CustomaryLaw, LawFamily.CustomaryLaw)]
        public void LegalBias_MapsExplicitlyWithoutCountryBinding(LegalFamilyBias bias, LawFamily expected)
        {
            var seed = new LegalTraditionSeed { Bias = bias };
            Assert.AreEqual(expected, seed.ToLawFamily());
            Assert.IsFalse(RegionPresetRedLines.HasPerPolityLawOrEthnicAssignment(Config(StartEra.Modern)));
        }

        [Test]
        public void Schema6_RoundTripsReferencesAndDynamicOverridesWithoutTiles()
        {
            var world = new WorldState(99);
            world.Map.GeoDataBuild = "build";
            world.Map.ManifestChecksum = "manifest";
            world.Map.StaticChunks.Add(new WorldMapChunkRef
                { ChunkId = "low", Lod = MapLodLevel.Low, RelativePath = "low.gz", Checksum = "abc" });
            world.Map.DynamicOverrides.Add(new WorldTileOverride
                { TileId = 3000001, HasBiome = true, Biome = BiomeType.Wetland });
            byte[] bytes = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(bytes);
            Assert.AreEqual(6, WorldStateSerializer.SchemaVersion);
            Assert.AreEqual("build", loaded.Map.GeoDataBuild);
            Assert.AreEqual(1, loaded.Map.StaticChunks.Count);
            Assert.AreEqual(BiomeType.Wetland, loaded.Map.DynamicOverrides[0].Biome);
            Assert.Less(bytes.Length, 4096, "静态全球 tile 不应重复写入存档");
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Schema6_LoadsLegacyThreeFourFive(int version)
        {
            var loaded = WorldStateSerializer.Load(
                WorldStateSerializer.SaveLegacy(WorldState.CreateMinimalSlice(5), version));
            Assert.AreEqual((ulong)5, loaded.worldSeed);
            Assert.IsNotNull(loaded.Map);
            Assert.AreEqual("", loaded.Map.GeoDataBuild);
        }

        private static WorldInitConfig Config(StartEra era) => new WorldInitConfig
        {
            PresetKey = "fertile_crescent", StartEra = era,
            StartRegionCenterLat = 33, StartRegionCenterLon = 44, StartRegionRadiusDeg = 8
        };
        private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }
}
