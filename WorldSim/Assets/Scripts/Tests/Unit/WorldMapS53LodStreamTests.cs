using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    [Category("S53")]
    public class WorldMapS53LodStreamTests
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
        public void S53_Build_ReturnsWithHighAndFocusMid_BeforeFarFieldReady()
        {
            var result = WorldMapFactory.Build(GeoRoot, Config());
            Assert.IsNotNull(result.LodStreamer);
            Assert.AreEqual(MapLodLevel.High,
                result.Geography.GetTile(new GeoCoordinate(33, 44), MapLodLevel.High).Lod);

            // 关键路径不得阻塞等待 Low：允许尚未就绪
            int criticalTiles = result.Geography.MaterializedTileCount;
            Assert.Greater(criticalTiles, 0);

            // Mid 焦点带内应已有 Mid（或更高）物化，而非仅靠纬度回退
            bool foundMid = result.Geography.TryGetExactTile(
                EquirectangularProjection.ToTileId(new GeoCoordinate(33, 44), MapLodLevel.Mid),
                out _);
            Assert.IsTrue(foundMid, "focus Mid tile should be materialized on critical path");
        }

        [Test]
        public async Task S53_DeferredLow_MergesWithoutOverwritingHigh()
        {
            var result = WorldMapFactory.Build(GeoRoot, Config());
            int highId = EquirectangularProjection.ToTileId(new GeoCoordinate(33, 44), MapLodLevel.High);
            Assert.IsTrue(result.Geography.TryGetExactTile(highId, out var before));
            Assert.AreEqual(MapLodLevel.High, before.Lod);
            int criticalBefore = result.Geography.MaterializedTileCount;

            await result.LodStreamer.EnsureFarFieldLoadedAsync();
            Assert.IsTrue(result.LodStreamer.IsFarFieldReady);
            int afterCount = result.Geography.MaterializedTileCount;
            Assert.Greater(afterCount, criticalBefore);

            Assert.IsTrue(result.Geography.TryGetExactTile(highId, out var after));
            Assert.AreEqual(MapLodLevel.High, after.Lod);
            Assert.AreEqual(before.Biome, after.Biome);
            Assert.AreEqual(MapLodLevel.Low,
                result.Geography.GetTile(new GeoCoordinate(-40, 120), MapLodLevel.High).Lod);
        }

        [Test]
        public void S53_DeferredLow_DoesNotBlockBuildReturn()
        {
            var sw = Stopwatch.StartNew();
            var result = WorldMapFactory.Build(GeoRoot, Config());
            sw.Stop();
            // Low 全量同步读在本机通常 > 数 ms；关键路径应明显更快并立即返回
            Assert.Less(sw.ElapsedMilliseconds, 15000, "critical Build should not hang on full Low IO");
            Assert.IsNotNull(result.Geography);
            // 未强制 Ensure 时远域可能尚未就绪 —— 这是异步契约本身
            result.LodStreamer.EnsureFarFieldLoaded();
            Assert.IsTrue(result.LodStreamer.IsFarFieldReady);
        }

        [Test]
        public async Task S53_PresentationLoad_CannotChangeMonthlyHash()
        {
            var world = new WorldState(0x553UL);
            WorldMapFactory.Build(GeoRoot, Config(), world);
            ulong before = WorldStateSerializer.ComputeMonthlyHash(world);

            var cache = new WorldMapChunkCache();
            await cache.LoadPresentationAsync("mid", Path.Combine(GeoRoot, "mid-global.wgeo.gz"));
            Assert.IsTrue(cache.TryGetPresentationChunk("mid", out _));

            Assert.AreEqual(before, WorldStateSerializer.ComputeMonthlyHash(world));
        }

        [Test]
        public void S53_RebuildGeography_WaitsForFarField()
        {
            var world = new WorldState(0x5531UL);
            WorldMapFactory.Build(GeoRoot, Config(), world);
            byte[] snap = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(snap);
            var geo = WorldMapFactory.RebuildGeography(loaded, GeoRoot);
            Assert.AreEqual(MapLodLevel.Low,
                geo.GetTile(new GeoCoordinate(-20, -60), MapLodLevel.High).Lod);
        }

        [Test]
        public void S53_MidFocusFilter_KeepsFewerTilesThanFullMidGrid()
        {
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(GeoRoot, "manifest.txt"));
            var critical = WorldMapLodStreamer.LoadCriticalBundles(manifest, GeoRoot, Config());
            int midCount = 0;
            foreach (var b in critical)
                if (b.Lod == MapLodLevel.Mid) midCount += b.Tiles.Count;
            Assert.Greater(midCount, 0);
            Assert.Less(midCount, 64800, "focus Mid must be spatially filtered, not full mid-global");
        }
    }
}
