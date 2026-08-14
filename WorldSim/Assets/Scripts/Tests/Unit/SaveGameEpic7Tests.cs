using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Ecology;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic7")]
    public class SaveGameEpic7Tests
    {
        private static string GeoRoot => Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");

        private static WorldInitConfig Config() => new WorldInitConfig
        {
            PresetKey = "fertile_crescent",
            StartEra = StartEra.Primordial,
            StartRegionCenterLat = 33,
            StartRegionCenterLon = 44,
            StartRegionRadiusDeg = 8
        };

        [Test]
        public void Schema9_IsCurrentAndLoadsLegacyEight()
        {
            Assert.AreEqual(10, WorldStateSerializer.SchemaVersion);
            var world = WorldState.CreateMinimalSlice(9);
            byte[] legacy8 = WorldStateSerializer.SaveLegacy(world, 8);
            var loaded = WorldStateSerializer.Load(legacy8);
            Assert.AreEqual((ulong)9, loaded.worldSeed);
        }

        [Test]
        public void Schema9_HighOverridesRoundTripLossless()
        {
            var world = new WorldState(41);
            int highId = EquirectangularProjection.EncodeTileId(MapLodLevel.High, 10, 20);
            world.Map.DynamicOverrides.Add(new WorldTileOverride
            {
                TileId = highId, HasBiome = true, Biome = BiomeType.Desert,
                HasElevation = true, ElevationMeters = 123.4
            });
            var loaded = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
            Assert.AreEqual(1, loaded.Map.DynamicOverrides.Count);
            Assert.AreEqual(highId, loaded.Map.DynamicOverrides[0].TileId);
            Assert.AreEqual(BiomeType.Desert, loaded.Map.DynamicOverrides[0].Biome);
            Assert.AreEqual(123.4, loaded.Map.DynamicOverrides[0].ElevationMeters, 1e-9);
        }

        [Test]
        public void Schema9_MidLowOverridesCompressSmallerThanFlatLegacy()
        {
            var world = new WorldState(42);
            // 同一 Low 父格内多个 Mid 覆盖 → Schema9 聚合成 1 条
            int midBaseX = 40;
            int midBaseY = 20;
            for (int i = 0; i < 32; i++)
            {
                world.Map.DynamicOverrides.Add(new WorldTileOverride
                {
                    TileId = EquirectangularProjection.EncodeTileId(MapLodLevel.Mid, midBaseX + (i % 4), midBaseY + (i / 4)),
                    HasBiome = true,
                    Biome = BiomeType.Grassland,
                    HasElevation = true,
                    ElevationMeters = 100 + i
                });
            }

            byte[] schema9 = WorldStateSerializer.Save(world);
            byte[] schema8 = WorldStateSerializer.SaveLegacy(world, 8);
            Assert.Less(schema9.Length, schema8.Length, "Mid aggregation should shrink Schema9 vs flat Schema8");

            var loaded = WorldStateSerializer.Load(schema9);
            Assert.Less(loaded.Map.DynamicOverrides.Count, 32);
            Assert.Greater(loaded.Map.DynamicOverrides.Count, 0);
            Assert.AreEqual(MapLodLevel.Low, LodOverrideCodec.LodOf(loaded.Map.DynamicOverrides[0].TileId));
        }

        [Test]
        public void HistoryDelta_EncodeApply_MergesWithoutDuplicate()
        {
            var world = WorldState.CreateMinimalSlice(7);
            world.Events.Add(new SimEvent(1, SimEventCategory.Chronicle, 1, "a", 1.0));
            world.Events.Add(new SimEvent(5, SimEventCategory.Civ, 2, "b", 2.0));

            byte[] delta = HistoryDeltaCodec.Encode(world.Events, sinceMonthInclusive: 5);
            int added = HistoryDeltaCodec.Apply(world, delta);
            Assert.AreEqual(0, added, "identical events must not duplicate");

            var other = WorldState.CreateMinimalSlice(7);
            other.Events.Add(new SimEvent(1, SimEventCategory.Chronicle, 1, "a", 1.0));
            added = HistoryDeltaCodec.Apply(other, delta);
            Assert.AreEqual(1, added);
            Assert.AreEqual(2, other.Events.Count);
        }

        [Test]
        public void IndicatorDelta_Schema9_RoundTripsCurrentFromPreviousPlusDelta()
        {
            var world = WorldState.CreateMinimalSlice(11);
            world.Ecology = world.Ecology ?? new EcologyState();
            world.Ecology.Indicators.Add(new EcologicalIndicatorState
            {
                stableId = 1,
                regionId = 0,
                code = "ndvi",
                previousValue = 10.0,
                currentValue = 12.5,
                zone = EcologyZone.Stable,
                stressMonths = 0,
                warningCode = ""
            });
            var loaded = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
            Assert.AreEqual(1, loaded.Ecology.Indicators.Count);
            Assert.AreEqual(10.0, loaded.Ecology.Indicators[0].previousValue, 1e-9);
            Assert.AreEqual(12.5, loaded.Ecology.Indicators[0].currentValue, 1e-6);
        }

        [Test]
        public void SV2_RebuildGeographyDeferred_ReturnsBeforeFarFieldReady()
        {
            var world = new WorldState(0xE7UL);
            WorldMapFactory.Build(GeoRoot, Config(), world);
            byte[] snap = SaveGameService.Save(world);
            var loaded = WorldStateSerializer.Load(snap);

            var sw = Stopwatch.StartNew();
            var result = WorldMapFactory.RebuildGeographyDeferred(loaded, GeoRoot);
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 15000);
            Assert.IsNotNull(result.LodStreamer);
            Assert.IsNotNull(result.Geography);
            Assert.Greater(result.Geography.MaterializedTileCount, 0);

            // 逻辑 tick / 月哈希不得因远域未齐而失败
            ulong hash = WorldStateSerializer.ComputeMonthlyHash(loaded);
            Assert.That(hash, Is.Not.EqualTo(0UL));

            result.LodStreamer.EnsureFarFieldLoaded();
            Assert.IsTrue(result.LodStreamer.IsFarFieldReady);
            Assert.AreEqual(MapLodLevel.Low,
                result.Geography.GetTile(new GeoCoordinate(-20, -60), MapLodLevel.High).Lod);
        }

        [Test]
        public void SV2_SaveGameService_LoadDeferred_DoesNotBlockOnFarField()
        {
            var world = new WorldState(0xE72UL);
            WorldMapFactory.Build(GeoRoot, Config(), world);
            byte[] snap = SaveGameService.Save(world);

            var result = SaveGameService.LoadDeferred(snap, GeoRoot);
            Assert.IsNotNull(result.Geography);
            // 允许尚未就绪；契约是不阻塞返回
            result.LodStreamer.EnsureFarFieldLoaded();
            Assert.IsTrue(result.LodStreamer.IsFarFieldReady);
        }

        [Test]
        public void SV2_LoadComplete_WaitsForFarFieldLikeRebuildGeography()
        {
            var world = new WorldState(0xE73UL);
            WorldMapFactory.Build(GeoRoot, Config(), world);
            byte[] snap = SaveGameService.Save(world);
            var loaded = SaveGameService.LoadComplete(snap, GeoRoot);
            Assert.AreEqual(MapLodLevel.Low,
                loaded.Geography.GetTile(new GeoCoordinate(-40, 120), MapLodLevel.High).Lod);
        }

        [Test]
        public void Schema9_SaveLoad_DoesNotRewriteMonthlyHashSemantics()
        {
            var world = WorldState.CreateMinimalSlice(99);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);
            var loaded = WorldStateSerializer.Load(WorldStateSerializer.Save(world));
            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(loaded));
        }
    }
}
