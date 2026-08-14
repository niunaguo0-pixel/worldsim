namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using WorldSim.Simulation.Core.WorldGeography;

    /// <summary>
    /// S5-3 LOD 分块异步延迟装载：同步 High（起始区逐 tile）+ 焦点 Mid；
    /// 远域 Low 后台装载并合并，不阻塞逻辑月结。表现缓存仍不进月哈希。
    /// </summary>
    public sealed class WorldMapLodStreamer
    {
        public const double DefaultMidFocusRadiusDeg = 20.0;

        private readonly object _gate = new object();
        private readonly Dictionary<string, WorldMapBundle> _presentation =
            new Dictionary<string, WorldMapBundle>(StringComparer.Ordinal);
        private Task _farFieldTask = Task.CompletedTask;
        private int _farFieldReady; // 0=pending/running, 1=ready
        private Exception _farFieldError;

        public bool IsFarFieldReady => Volatile.Read(ref _farFieldReady) == 1;
        public Exception FarFieldError => _farFieldError;

        /// <summary>同步装载逻辑关键路径：High 起始区 + Mid 焦点带。</summary>
        public static List<WorldMapBundle> LoadCriticalBundles(
            GeoBundleManifest manifest, string geoRoot, WorldInitConfig config)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (config == null) throw new ArgumentNullException(nameof(config));

            double cLat = config.StartRegionCenterLat;
            double cLon = config.StartRegionCenterLon;
            double highRadius = Math.Max(0.5, config.StartRegionRadiusDeg);
            double midRadius = Math.Max(highRadius * 2.0, DefaultMidFocusRadiusDeg);

            var loaded = new List<WorldMapBundle>();
            // 稳定序：先 High 再 Mid（Low 走异步）
            foreach (var chunk in SortedChunks(manifest, MapLodLevel.High))
            {
                string path = ResolvePath(geoRoot, chunk.RelativePath);
                WorldMapFactory.VerifyChecksum(path, chunk.Checksum);
                loaded.Add(WorldMapBundleReader.ReadBundle(path,
                    c => WithinRadius(c, cLat, cLon, highRadius)));
            }
            foreach (var chunk in SortedChunks(manifest, MapLodLevel.Mid))
            {
                string path = ResolvePath(geoRoot, chunk.RelativePath);
                WorldMapFactory.VerifyChecksum(path, chunk.Checksum);
                loaded.Add(WorldMapBundleReader.ReadBundle(path,
                    c => WithinRadius(c, cLat, cLon, midRadius)));
            }
            return loaded;
        }

        /// <summary>启动远域 Low 异步装载；完成后 Merge 进 geography（不覆盖 High/Mid）。</summary>
        public void BeginDeferredFarField(
            GeoBundleManifest manifest,
            string geoRoot,
            WorldGeography geography,
            CancellationToken cancellationToken = default)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (geography == null) throw new ArgumentNullException(nameof(geography));
            if (IsFarFieldReady) return;

            Volatile.Write(ref _farFieldReady, 0);
            _farFieldError = null;
            var lowChunks = SortedChunks(manifest, MapLodLevel.Low);
            _farFieldTask = Task.Run(() =>
            {
                try
                {
                    foreach (var chunk in lowChunks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string path = ResolvePath(geoRoot, chunk.RelativePath);
                        WorldMapFactory.VerifyChecksum(path, chunk.Checksum);
                        var bundle = WorldMapBundleReader.ReadBundle(path);
                        cancellationToken.ThrowIfCancellationRequested();
                        geography.MergeBundle(bundle, preferExisting: true);
                        lock (_gate) _presentation[chunk.ChunkId] = bundle;
                    }
                    Volatile.Write(ref _farFieldReady, 1);
                }
                catch (Exception ex)
                {
                    _farFieldError = ex;
                    Volatile.Write(ref _farFieldReady, 0);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>测试/读档重建：阻塞直至远域 Low 就绪（或失败抛出）。</summary>
        public void EnsureFarFieldLoaded()
        {
            try
            {
                _farFieldTask.GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                throw ae.InnerException ?? ae;
            }
            if (_farFieldError != null) throw _farFieldError;
            if (!IsFarFieldReady)
                throw new InvalidOperationException("Far-field Low LOD did not become ready");
        }

        public Task EnsureFarFieldLoadedAsync() => _farFieldTask;

        public Task<WorldMapBundle> LoadPresentationAsync(string chunkId, string path,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
                if (_presentation.TryGetValue(chunkId, out WorldMapBundle hit))
                    return Task.FromResult(hit);

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bundle = WorldMapBundleReader.ReadBundle(path);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate) _presentation[chunkId] = bundle;
                return bundle;
            }, cancellationToken);
        }

        public bool TryGetPresentationChunk(string chunkId, out WorldMapBundle bundle)
        {
            lock (_gate) return _presentation.TryGetValue(chunkId, out bundle);
        }

        public void ClearPresentationCache()
        {
            lock (_gate) _presentation.Clear();
        }

        public static bool WithinRadius(GeoCoordinate c, double centerLat, double centerLon, double radius)
        {
            double dLat = c.Latitude - centerLat;
            double dLon = EquirectangularProjection.WrappedLongitudeDistance(c.Longitude, centerLon);
            dLon *= Math.Cos((c.Latitude + centerLat) * 0.5 * Math.PI / 180.0);
            return Math.Sqrt(dLat * dLat + dLon * dLon) <= radius;
        }

        private static List<WorldMapChunkRef> SortedChunks(GeoBundleManifest manifest, MapLodLevel lod)
        {
            var list = new List<WorldMapChunkRef>();
            foreach (var chunk in manifest.Chunks)
                if (chunk.Lod == lod) list.Add(chunk);
            list.Sort((a, b) => string.CompareOrdinal(a.ChunkId, b.ChunkId));
            return list;
        }

        private static string ResolvePath(string geoRoot, string relativePath) =>
            Path.Combine(geoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
