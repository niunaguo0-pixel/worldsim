namespace WorldSim.Simulation.WorldMap
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// 表现层 chunk 缓存门面；底层委托 S5-3 <see cref="WorldMapLodStreamer"/>。
    /// 完成顺序不得改变任何月结算结果。
    /// </summary>
    public sealed class WorldMapChunkCache
    {
        private readonly WorldMapLodStreamer _streamer = new WorldMapLodStreamer();

        public Task<WorldMapBundle> LoadPresentationAsync(string chunkId, string path,
            CancellationToken cancellationToken = default) =>
            _streamer.LoadPresentationAsync(chunkId, path, cancellationToken);

        public bool TryGetPresentationChunk(string chunkId, out WorldMapBundle bundle) =>
            _streamer.TryGetPresentationChunk(chunkId, out bundle);

        public void ClearPresentationCache() => _streamer.ClearPresentationCache();
    }
}
