namespace WorldSim.Simulation.WorldMap
{
    using System;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Serialization;

    /// <summary>
    /// Epic 7 完整存档外观：Save / LoadDeferred（SV2 异步远域）/ LoadComplete（Replay 齐远域）。
    /// </summary>
    public static class SaveGameService
    {
        public static byte[] Save(WorldState world) => WorldStateSerializer.Save(world);

        /// <summary>读档：反序列化 + High/焦点 Mid 同步；远域 Low 异步，不阻塞返回。</summary>
        public static WorldMapBuildResult LoadDeferred(byte[] snapshot, string geoRoot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            WorldState world = WorldStateSerializer.Load(snapshot);
            return WorldMapFactory.RebuildGeographyDeferred(world, geoRoot);
        }

        /// <summary>读档并阻塞至远域 Low 就绪（Gate-0 Replay 路径④）。</summary>
        public static WorldState LoadComplete(byte[] snapshot, string geoRoot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            WorldState world = WorldStateSerializer.Load(snapshot);
            WorldMapFactory.RebuildGeography(world, geoRoot);
            return world;
        }
    }
}
