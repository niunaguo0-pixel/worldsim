using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldSim.Presentation;
using WorldSim.Simulation.Core;
using WorldSim.Simulation.Core.Serialization;
using WorldSim.Simulation.Core.WorldGeography;
using WorldSim.Simulation.Intervention;
using WorldSim.Simulation.Time;
using WorldSim.Simulation.WorldMap;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Epic5WorldMap")]
    public partial class WorldMapTask4Tests
    {
        // ---------- WSP1 读取往返 ----------

        [Test]
        public void Wsp1_ReadRoundTripsAllFields()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-test");
            var read = PoliticalAssetReader.ReadBytes(bytes);
            Assert.AreEqual(2026, read.BorderYear);
            Assert.AreEqual("geo-v1-test", read.BuildId);
            Assert.AreEqual(2, read.DeFactoCountries.Count);
            Assert.AreEqual(1, read.SovereigntyClaims.Count);
            Assert.AreEqual(1, read.DisputedAreas.Count);
            Assert.AreEqual(2, read.Cities.Count);

            var usa = read.DeFactoCountries[1];
            Assert.AreEqual("USA", usa.StableId);
            Assert.AreEqual("US1", usa.SovereignId);
            Assert.AreEqual("United States", usa.Name);
            Assert.AreEqual(3, usa.Rings[0].Count);
            Assert.AreEqual(-100.0, usa.Rings[0].Points[0].Longitude);
            Assert.AreEqual(40.0, usa.Rings[0].Points[0].Latitude);

            var city = read.Cities[0];
            Assert.AreEqual("Washington D.C.", city.Name);
            Assert.AreEqual(1, city.IsCapital);
            Assert.AreEqual(38.9, city.Latitude);
            Assert.AreEqual(-77.0, city.Longitude);
        }

        [Test]
        public void Wsp1_BadMagicFailsClosed()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "b");
            bytes[0] = 0;
            Assert.Throws<InvalidDataException>(() => PoliticalAssetReader.ReadBytes(bytes));
        }

        [Test]
        public void Wsp1_BadBorderYearFailsClosed()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "b");
            bytes[5] = 0; bytes[6] = 0;
            var ex = Assert.Throws<NotSupportedException>(() => PoliticalAssetReader.ReadBytes(bytes));
            StringAssert.Contains("2026", ex.Message);
        }

        // Important 3: 尾随字节 fail-closed, 与 Python political_binary.py:341-342 跨实现一致
        [Test]
        public void Wsp1_TrailingBytesFailClosed()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "b");
            // 追加 3 字节垃圾, 模拟损坏/截断后多出的尾字节
            byte[] padded = new byte[bytes.Length + 3];
            System.Array.Copy(bytes, padded, bytes.Length);
            padded[bytes.Length] = 0xDE;
            padded[bytes.Length + 1] = 0xAD;
            padded[bytes.Length + 2] = 0xBE;
            var ex = Assert.Throws<InvalidDataException>(() => PoliticalAssetReader.ReadBytes(padded));
            StringAssert.Contains("trailing", ex.Message);
            StringAssert.Contains("3", ex.Message);
        }

        // Important 3: 无尾字节 (恰好读完) 应正常通过
        [Test]
        public void Wsp1_NoTrailingBytesReadsCleanly()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-clean");
            var read = PoliticalAssetReader.ReadBytes(bytes);
            Assert.AreEqual(2, read.DeFactoCountries.Count);
            Assert.AreEqual(1, read.SovereigntyClaims.Count);
            Assert.AreEqual(1, read.DisputedAreas.Count);
            Assert.AreEqual(2, read.Cities.Count);
        }

        // ---------- 双视图聚合 ----------

        [Test]
        public void BorderView_DeFactoAggregatesByAdminId()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-test");
            string tmp = Path.Combine(Application.temporaryCachePath, "wsp1-defacto.wgeo.gz");
            WriteGzip(tmp, bytes);
            try
            {
                var init = WorldStartFactory.ReadGeoPoliticalFromAsset(tmp, 2026, BorderView.DeFactoControl);
                Assert.AreEqual(2, init.Countries.Count);
                Assert.AreEqual("Canada", init.Countries[0].Name);
                Assert.AreEqual("United States", init.Countries[1].Name);
                var usa = init.Countries.First(c => c.Name == "United States");
                Assert.AreEqual(1, usa.Cities.Count);
                Assert.AreEqual("Washington D.C.", usa.Cities[0].Name);
                var can = init.Countries.First(c => c.Name == "Canada");
                Assert.AreEqual(1, can.Cities.Count);
                Assert.AreEqual("Ottawa", can.Cities[0].Name);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Test]
        public void BorderView_SovereigntyAggregatesBySovereignId()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-test");
            string tmp = Path.Combine(Application.temporaryCachePath, "wsp1-sov.wgeo.gz");
            WriteGzip(tmp, bytes);
            try
            {
                var init = WorldStartFactory.ReadGeoPoliticalFromAsset(tmp, 2026, BorderView.SovereigntyClaims);
                Assert.AreEqual(1, init.Countries.Count);
                Assert.AreEqual("United States", init.Countries[0].Name);
                Assert.AreEqual(2, init.Countries[0].Cities.Count);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Test]
        public void BorderView_AggregationIsDeterministicAcrossRuns()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-test");
            string tmp = Path.Combine(Application.temporaryCachePath, "wsp1-det.wgeo.gz");
            WriteGzip(tmp, bytes);
            try
            {
                var a = WorldStartFactory.ReadGeoPoliticalFromAsset(tmp, 2026, BorderView.DeFactoControl);
                var b = WorldStartFactory.ReadGeoPoliticalFromAsset(tmp, 2026, BorderView.DeFactoControl);
                Assert.AreEqual(a.Countries.Count, b.Countries.Count);
                for (int i = 0; i < a.Countries.Count; i++)
                {
                    Assert.AreEqual(a.Countries[i].Name, b.Countries[i].Name);
                    Assert.AreEqual(a.Countries[i].Cities.Count, b.Countries[i].Cities.Count);
                    for (int j = 0; j < a.Countries[i].Cities.Count; j++)
                        Assert.AreEqual(a.Countries[i].Cities[j].Name, b.Countries[i].Cities[j].Name);
                }
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ---------- 争议标记保留 ----------

        [Test]
        public void DisputedMarkers_PreservedWithClaimantsAndNoAdjudication()
        {
            var asset = SampleAsset();
            byte[] bytes = Wsp1TestWriter.Serialize(asset, "geo-v1-test");
            string tmp = Path.Combine(Application.temporaryCachePath, "wsp1-disp.wgeo.gz");
            WriteGzip(tmp, bytes);
            try
            {
                var init = WorldStartFactory.ReadGeoPoliticalFromAsset(tmp, 2026, BorderView.DeFactoControl);
                Assert.AreEqual(1, init.DisputedAreas.Count);
                var d = init.DisputedAreas[0];
                Assert.AreEqual("Kashmir", d.Name);
                Assert.AreEqual("India", d.AdminClaimant);
                Assert.AreEqual("Pakistan", d.SovereignClaimant);
                Assert.AreEqual("Disputed", d.Type);
                StringAssert.Contains("Disputed", d.NoteAdm0);
                Assert.IsNull(typeof(DisputedMarker).GetField("Verdict"));
                Assert.IsNull(typeof(DisputedMarker).GetField("Status"));
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ---------- Schema 3-6 加载, Schema 7 往返 ----------

        [Test]
        public void Schema7_RoundTripsBorderViewAndMapState()
        {
            var world = new WorldState(99);
            world.Map.GeoDataBuild = "build7";
            world.Map.ManifestChecksum = "manifest7";
            world.Map.Config.BorderView = (int)BorderView.SovereigntyClaims;
            world.Map.StaticChunks.Add(new WorldMapChunkRef
                { ChunkId = "low", Lod = MapLodLevel.Low, RelativePath = "low.gz", Checksum = "abc" });
            world.Map.DynamicOverrides.Add(new WorldTileOverride
                { TileId = 3000001, HasBiome = true, Biome = BiomeType.Wetland });
            byte[] bytes = WorldStateSerializer.Save(world);
            var loaded = WorldStateSerializer.Load(bytes);
            Assert.AreEqual(8, WorldStateSerializer.SchemaVersion);
            Assert.AreEqual("build7", loaded.Map.GeoDataBuild);
            Assert.AreEqual((int)BorderView.SovereigntyClaims, loaded.Map.Config.BorderView);
            Assert.AreEqual(1, loaded.Map.StaticChunks.Count);
            Assert.AreEqual(BiomeType.Wetland, loaded.Map.DynamicOverrides[0].Biome);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Schema7_LoadsLegacyThreeFourFiveSix(int version)
        {
            var loaded = WorldStateSerializer.Load(
                WorldStateSerializer.SaveLegacy(WorldState.CreateMinimalSlice(5), version));
            Assert.AreEqual((ulong)5, loaded.worldSeed);
            Assert.IsNotNull(loaded.Map);
            Assert.AreEqual(0, loaded.Map.Config.BorderView);
            Assert.AreEqual("", loaded.Map.GeoDataBuild);
        }

        [Test]
        public void Schema7_BorderViewEntersMonthlyHash()
        {
            var a = new WorldState(7);
            a.Map.Config.BorderView = (int)BorderView.DeFactoControl;
            var b = new WorldState(7);
            b.Map.Config.BorderView = (int)BorderView.SovereigntyClaims;
            Assert.AreNotEqual(
                WorldStateSerializer.ComputeMonthlyHash(a),
                WorldStateSerializer.ComputeMonthlyHash(b),
                "BorderView 必须进入稳定月哈希");
        }

        [Test]
        public void Schema7_StaticSourceVersionEntersMonthlyHash()
        {
            var a = new WorldState(7);
            a.Map.GeoDataBuild = "geo-v1-a";
            var b = new WorldState(7);
            b.Map.GeoDataBuild = "geo-v1-b";
            Assert.AreNotEqual(
                WorldStateSerializer.ComputeMonthlyHash(a),
                WorldStateSerializer.ComputeMonthlyHash(b));
        }

        // Task 6: 替换原 Task5 占位探针 (Assert.Pass) 为有意义的断言 — 验证已提交派生包
        // 是真实重生产物 (非 simplified 占位), 且 manifest 携带 lock 派生 buildId 与 conversion 参数。
        [Test]
        public void Z_Task5_DiscoveryCheck()
        {
            string geoRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Geo", "v1");
            var manifest = WorldMapBundleReader.ReadManifest(Path.Combine(geoRoot, "manifest.txt"));
            Assert.IsTrue(manifest.BuildId.StartsWith("geo-v1-", StringComparison.Ordinal), manifest.BuildId);
            Assert.IsFalse(manifest.Fidelity.Contains("simplified"),
                "committed bundle must be real, not simplified: " + manifest.Fidelity);
            Assert.IsTrue(manifest.Conversion.Count > 0, "manifest must carry conversion parameters");
            Assert.AreEqual("2026", manifest.Conversion["borderYear"], "border year must be pinned to 2026");
        }

        // Task 5 修复 Task 4 disclosed deferral: SimulationRunner.LoadFromSnapshot 创建新
        // InterventionSystem 实例后必须重新 Bind 给 InterventionFxBridge, 否则 PlayMode 存读档
        // 后 FX bridge 仍指向旧实例, CausalChain 增长不再被消费, 干预无视觉反馈。
        [Test]
        public void FxBridge_RebindToNewSystem_ConsumesNewChainNotStale()
        {
            var go = new GameObject("FxBridge_Test");
            try
            {
                var fx = go.AddComponent<InterventionFxBridge>();
                // sysA: 旧实例 (存档前). 用反射往 _causal 塞一个节点, 模拟存档前干预.
                var worldA = new WorldState(1);
                var sysA = InterventionSystem.AttachToSlice(worldA);
                AddCausalNode(sysA, month: 1, templateId: "drop.instant.rain", actionKey: "rain", magnitude: 0.5);
                fx.Bind(sysA);
                InvokeUpdate(fx);
                Assert.AreEqual(1, fx.SeenCausalCount, "Bind 后应消费 sysA 的因果链");
                Assert.IsTrue(fx.LastFx.Contains("DROP"), "应渲染 sysA 的 drop.instant: " + fx.LastFx);

                // sysB: 新实例 (读档后重建). LoadFromSnapshot 会新建 InterventionSystem.
                var worldB = new WorldState(2);
                var sysB = InterventionSystem.AttachToSlice(worldB);
                // 关键: 重新 Bind 到新实例. 不重绑则 fx._sys 仍指向 sysA.
                fx.Bind(sysB);
                Assert.AreEqual(0, fx.SeenCausalCount, "Rebind 必须重置已消费计数");
                Assert.AreEqual("", fx.LastFx, "Rebind 必须清空旧 FX 文本");

                // sysB 产生新因果链节点; fx 应消费 sysB 的链, 而非 sysA 的残留.
                AddCausalNode(sysB, month: 2, templateId: "gradual.warm", actionKey: "warm", magnitude: 0.3);
                InvokeUpdate(fx);
                Assert.AreEqual(1, fx.SeenCausalCount, "Rebind 后应消费 sysB 的新链");
                Assert.IsTrue(fx.LastFx.Contains("gradual.warm"),
                    "应渲染 sysB 的新模板, 而非 sysA 的旧 drop.instant: " + fx.LastFx);
                // sysA 的链不应被重复消费 (SeenCausalCount 仍为 1, 不是 2)
                Assert.AreEqual(1, fx.SeenCausalCount, "不应跨实例累加消费计数");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void AddCausalNode(InterventionSystem sys, int month, string templateId, string actionKey, double magnitude)
        {
            // RecordCausal 是 private; 用反射往 _causal 列表塞节点, 模拟干预执行后的因果链增长.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var causalField = typeof(InterventionSystem).GetField("_causal", flags);
            Assert.IsNotNull(causalField, "InterventionSystem._causal field not found");
            var list = (System.Collections.IList)causalField.GetValue(sys);
            list.Add(new CausalChainNode
            {
                InterventionId = 1,
                MonthExecuted = month,
                ActionKey = actionKey,
                EventTemplateId = templateId,
                Magnitude = magnitude
            });
        }

        private static void InvokeUpdate(InterventionFxBridge fx)
        {
            // Update 是 Unity 私有回调; EditMode 不会自动调用, 用反射驱动一次消费.
            var method = typeof(InterventionFxBridge).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "InterventionFxBridge.Update not found");
            method.Invoke(fx, null);
        }
    }
}
