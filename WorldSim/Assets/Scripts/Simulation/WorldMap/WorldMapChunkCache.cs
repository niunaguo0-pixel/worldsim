namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// 仅表现缓存。模拟只读取 WorldGeography 的同步确定性快照，
    /// 因此 Task 完成顺序不能改变任何月结算结果。
    /// </summary>
    public sealed class WorldMapChunkCache
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, WorldMapBundle> _loaded =
            new Dictionary<string, WorldMapBundle>(StringComparer.Ordinal);

        public Task<WorldMapBundle> LoadPresentationAsync(string chunkId, string path,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
                if (_loaded.TryGetValue(chunkId, out WorldMapBundle hit))
                    return Task.FromResult(hit);

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bundle = WorldMapBundleReader.ReadBundle(path);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate) _loaded[chunkId] = bundle;
                return bundle;
            }, cancellationToken);
        }

        public bool TryGetPresentationChunk(string chunkId, out WorldMapBundle bundle)
        {
            lock (_gate) return _loaded.TryGetValue(chunkId, out bundle);
        }

        public void ClearPresentationCache()
        {
            lock (_gate) _loaded.Clear();
        }
    }
}
