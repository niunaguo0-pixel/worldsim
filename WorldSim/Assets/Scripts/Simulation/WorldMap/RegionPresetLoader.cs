namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// region-presets 消费入口 (V0-6 / B4): 读 JSON → 映射 WorldInitConfig → MVP High 区域网格.
    /// B5 红线: 只产偏置/种子, 绝不写 Polity 级 lawFamily/ethnicGroup.
    /// </summary>
    public static class RegionPresetLoader
    {
        public static RegionPresetCatalog LoadFromFile(string path)
        {
            string json = File.ReadAllText(path);
            return RegionPresetJson.Parse(json);
        }

        public static RegionPresetCatalog LoadFromJson(string json) => RegionPresetJson.Parse(json);

        public static WorldInitConfig Consume(RegionPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            var cfg = new WorldInitConfig
            {
                PresetKey = preset.Key,
                StartRegionCenterLat = preset.CenterLat,
                StartRegionCenterLon = preset.CenterLon,
                StartRegionRadiusDeg = preset.RadiusDeg,
                EthnicDistribution = new RealEthnicDistribution(),
                LegalTraditionSeed = null,
            };

            for (int i = 0; i < preset.EthnicSeed.Count; i++)
                cfg.EthnicDistribution.Groups.Add(preset.EthnicSeed[i]);

            if (Enum.TryParse(preset.LegalFamilyDefault, ignoreCase: true, out LegalFamilyBias bias))
                cfg.LegalTraditionSeed = new LegalTraditionSeed { Bias = bias };

            RegionPresetRedLines.ValidateInitConfig(cfg);
            return cfg;
        }

        public static WorldInitConfig ConsumeKey(RegionPresetCatalog catalog, string key)
        {
            var p = catalog.Get(key);
            if (p == null) throw new KeyNotFoundException("region preset not found: " + key);
            return Consume(p);
        }

        /// <summary>一站式: 文件 → 指定预设 → WorldInitConfig → MVP High 区域.</summary>
        public static MvpRegionMap LoadAndBuildRegion(string presetsPath, string presetKey, double degPerTile = 0.5)
        {
            var catalog = LoadFromFile(presetsPath);
            var cfg = ConsumeKey(catalog, presetKey);
            cfg.DegPerTile = degPerTile;
            return MvpRegionInitializer.BuildHighPrecisionRegion(cfg);
        }
    }

    /// <summary>B5 红线守卫.</summary>
    public static class RegionPresetRedLines
    {
        public static void ValidateInitConfig(WorldInitConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            // 类型层: WorldInitConfig 不得持有 per-polity 字段 (编译期结构 + 运行时反射双保险)
            var t = typeof(WorldInitConfig);
            foreach (var f in t.GetFields())
            {
                string n = f.Name;
                if (ContainsForbidden(n))
                    throw new InvalidOperationException("B5 红线违例: WorldInitConfig 字段 " + n);
            }
            foreach (var p in t.GetProperties())
            {
                string n = p.Name;
                if (ContainsForbidden(n))
                    throw new InvalidOperationException("B5 红线违例: WorldInitConfig 属性 " + n);
            }

            if (cfg.EthnicDistribution != null)
            {
                for (int i = 0; i < cfg.EthnicDistribution.Groups.Count; i++)
                {
                    var g = cfg.EthnicDistribution.Groups[i];
                    if (string.IsNullOrEmpty(g.LanguageFamily))
                        throw new InvalidOperationException("ethnicSeed 缺少 languageFamily");
                    // 份额必须是比例种子, 不是国家代码
                    if (g.Share < 0 || g.Share > 1.0)
                        throw new InvalidOperationException("ethnicSeed.share 越界: " + g.Share);
                }
            }
        }

        public static bool HasPerPolityLawOrEthnicAssignment(WorldInitConfig cfg)
        {
            // 当前类型故意不含此类字段 → 恒 false; 若未来误加字段, Validate 会先炸.
            var t = typeof(WorldInitConfig);
            foreach (var f in t.GetFields())
                if (ContainsForbidden(f.Name)) return true;
            foreach (var p in t.GetProperties())
                if (ContainsForbidden(p.Name)) return true;
            return false;
        }

        private static bool ContainsForbidden(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("perpolity") || n.Contains("politylaw") || n.Contains("nationlaw")
                || n.Contains("assignedlawfamily") || n.Contains("fixedethnicgroup")
                || n == "lawfamilybycountry" || n == "ethnicgroupbycountry";
        }
    }

    /// <summary>MVP 高精度起始区域: 简化地形/气候/生物群系 (切片级, 非完整 Natural Earth 管线).</summary>
    public static class MvpRegionInitializer
    {
        public static MvpRegionMap BuildHighPrecisionRegion(WorldInitConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            double r = cfg.StartRegionRadiusDeg;
            double dpt = cfg.DegPerTile > 0 ? cfg.DegPerTile : 0.5;
            double minLat = cfg.StartRegionCenterLat - r;
            double maxLat = cfg.StartRegionCenterLat + r;
            double minLon = cfg.StartRegionCenterLon - r;
            double maxLon = cfg.StartRegionCenterLon + r;

            int h = Math.Max(1, (int)Math.Ceiling((maxLat - minLat) / dpt));
            int w = Math.Max(1, (int)Math.Ceiling((maxLon - minLon) / dpt));
            var tiles = new WorldTile[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double lat = minLat + (y + 0.5) * dpt;
                    double lon = minLon + (x + 0.5) * dpt;
                    double dist = HaversineApproxDeg(cfg.StartRegionCenterLat, cfg.StartRegionCenterLon, lat, lon);
                    // 简化高程: 距中心越近越高 (河谷盆地反相用 lat 调制)
                    double elev = Math.Max(0, (r - dist) / r) * 0.6 + (1.0 - Math.Abs(lat) / 90.0) * 0.2;
                    byte biome = PickBiome(lat, elev, dist, r);
                    tiles[y, x] = new WorldTile
                    {
                        LatIdx = y,
                        LonIdx = x,
                        Lat = lat,
                        Lon = lon,
                        Elevation = elev,
                        BiomeId = biome,
                        Lod = 0, // High
                        HasCoast = biome == 3 || dist > r * 0.85,
                    };
                }
            }

            return new MvpRegionMap
            {
                Config = cfg,
                Tiles = tiles,
                Width = w,
                Height = h,
                MinLat = minLat,
                MaxLat = maxLat,
                MinLon = minLon,
                MaxLon = maxLon,
            };
        }

        private static byte PickBiome(double lat, double elev, double dist, double r)
        {
            if (dist > r * 0.95) return 3; // 边缘视为水域邻域
            if (Math.Abs(lat) < 20 && elev < 0.25) return 0; // 低纬低地荒漠
            if (elev > 0.55) return 2; // 高地森林
            return 1; // 草原
        }

        // 平面近似 (度空间), MVP 足够; 完整测地线在 S5-1
        private static double HaversineApproxDeg(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = lat2 - lat1;
            double dLon = (lon2 - lon1) * Math.Cos((lat1 + lat2) * 0.5 * Math.PI / 180.0);
            return Math.Sqrt(dLat * dLat + dLon * dLon);
        }
    }
}
