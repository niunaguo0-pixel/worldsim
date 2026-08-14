namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Civilization;
    using WorldSim.Simulation.Core.WorldGeography;

    public sealed class CountryInit
    {
        public string Name = "";
        public double MinLat, MinLon, MaxLat, MaxLon;
        public readonly List<CityInit> Cities = new List<CityInit>();
    }

    public sealed class CityInit
    {
        public string Name = "";
        public GeoCoordinate Location;
        public int Population;
    }

    public sealed class GeoPoliticalInit
    {
        public int BorderYear;
        public BorderView BorderView = BorderView.DeFactoControl;
        public readonly List<CountryInit> Countries = new List<CountryInit>();
        /// <summary>争议区标记 (claimant admin/sovereign + 源 TYPE/NOTE), 不编造裁决.</summary>
        public readonly List<DisputedMarker> DisputedAreas = new List<DisputedMarker>();
    }

    public sealed class DisputedMarker
    {
        public string Name = "";
        public string AdminClaimant = "";
        public string SovereignClaimant = "";
        public string Type = "";
        public string NoteAdm0 = "";
        public string NoteBrk = "";
        public double MinLat, MinLon, MaxLat, MaxLon;
    }

    public sealed class WorldStartResult
    {
        public WorldState World;
        public WorldInitConfig Config;
        public GeoPoliticalInit GeoPolitical;
    }

    public static class WorldStartFactory
    {
        public static WorldStartResult Create(ulong seed, WorldInitConfig config, string geoRoot)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.NormalizeDerivedMode();
            RegionPresetRedLines.ValidateInitConfig(config);
            var world = new WorldState(seed);
            var map = WorldMapFactory.Build(geoRoot, config, world);
            world.EraIndex = (int)config.StartEra;

            if (config.StartMode == StartMode.PrimordialSandbox)
            {
                InitializePrimordial(world, config);
                return new WorldStartResult { World = world, Config = config, GeoPolitical = null };
            }

            var political = ReadGeoPolitical(geoRoot, config.BorderYear, config.BorderView);
            InitializeModern(world, config, political);
            return new WorldStartResult { World = world, Config = config, GeoPolitical = political };
        }

        private static void InitializePrimordial(WorldState world, WorldInitConfig config)
        {
            int tileId = FindHabitable(world.Geography,
                new GeoCoordinate(config.StartRegionCenterLat, config.StartRegionCenterLon));
            world.Civilization.Settlements.Add(new CivilizationSettlementState
            {
                stableId = 1, worldTileId = tileId, polityId = 100, population = 80,
                housingCapacity = 240, foodCapacity = 220, spaceCapacity = 400, prosperity = 0.4
            });
            world.Civilization.Polities.Add(new CivilizationPolityState
            {
                stableId = 100, techTier = 0, stability = 0.5, legitimacy = 0.4,
                governance = GovernanceType.Chiefdom, lawFamily = LawFamily.CustomaryLaw,
                LawFamilyLocked = false,
                Ethnicity = EthnicComposition.CreateSingletonDominant("Band", "Unclassified"),
                LegitimacySources = new LegitimacySource(),
                Military = new MilitaryState()
            });
            AddSupportState(world.Civilization, 1, 100);
        }

        private static void InitializeModern(WorldState world, WorldInitConfig config, GeoPoliticalInit political)
        {
            var countries = new List<CountryInit>(political.Countries);
            countries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            int polityId = 1000;
            int settlementId = 10000;
            LawFamily law = config.LegalTraditionSeed?.ToLawFamily() ?? LawFamily.CustomaryLaw;
            if (law == LawFamily.ReligiousLaw) law = LawFamily.CustomaryLaw; // 种子路径不产出宗教法
            var ethnicity = ResolveDominantEthnicity(config.EthnicDistribution);
            foreach (var country in countries)
            {
                var polity = new CivilizationPolityState
                {
                    stableId = polityId, techTier = config.StartEra == StartEra.Modern ? 7 : 5,
                    lawStage = 4, stability = 0.6, legitimacy = 0.6, governance = GovernanceType.Kingdom,
                    lawFamily = law, LawFamilyLocked = true,
                    titleTier = TitleTier.King, scaleTier = ScaleTier.Regional,
                    Ethnicity = EthnicComposition.CreateSingletonDominant(
                        ethnicity.Name, ethnicity.LanguageFamily),
                    LegitimacySources = new LegitimacySource(),
                    Military = new MilitaryState { Status = WarStatus.Idle }
                };
                world.Civilization.Polities.Add(polity);
                var cities = new List<CityInit>(country.Cities);
                cities.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var city in cities)
                {
                    int tileId = world.Geography.GetTileId(city.Location, MapLodLevel.High);
                    var actual = world.Geography.GetTile(city.Location, MapLodLevel.High);
                    tileId = actual.TileId;
                    world.Civilization.Settlements.Add(new CivilizationSettlementState
                    {
                        stableId = settlementId, worldTileId = tileId, polityId = polityId,
                        population = Math.Max(1000, city.Population), housingCapacity = city.Population * 1.2,
                        foodCapacity = city.Population * 1.1, spaceCapacity = city.Population * 1.5,
                        prosperity = 0.6, tier = SettlementTier.City
                    });
                    AddSupportState(world.Civilization, settlementId, polityId);
                    settlementId++;
                }
                polityId++;
            }
        }

        /// <summary>地缘种子取最大份额作唯一主导；无种子则默认单游群。</summary>
        private static EthnicSeedEntry ResolveDominantEthnicity(RealEthnicDistribution distribution)
        {
            if (distribution == null || distribution.Groups == null || distribution.Groups.Count == 0)
                return new EthnicSeedEntry("Unclassified", "Band", 1.0);
            EthnicSeedEntry best = distribution.Groups[0];
            for (int i = 1; i < distribution.Groups.Count; i++)
            {
                var g = distribution.Groups[i];
                if (g.Share > best.Share
                    || (g.Share == best.Share
                        && string.CompareOrdinal(g.Name ?? "", best.Name ?? "") < 0))
                    best = g;
            }
            return best;
        }

        private static void AddSupportState(CivilizationState state, int settlementId, int polityId)
        {
            state.Economies.Add(new CivilizationEconomyState
                { stableId = settlementId, settlementId = settlementId, food = 30, wood = 10 });
            state.Tech.Add(new TechProgressState
                { stableId = settlementId, polityId = polityId });
            state.Individuals.Add(new IndividualState
                { stableId = settlementId, settlementId = settlementId, alive = true, health = 1 });
        }

        private static int FindHabitable(IWorldGeography geography, GeoCoordinate center)
        {
            for (int ring = 0; ring <= 16; ring++)
                for (int y = -ring; y <= ring; y++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        var c = new GeoCoordinate(center.Latitude + y * 0.5, center.Longitude + x * 0.5);
                        int id = geography.GetTile(c, MapLodLevel.High).TileId;
                        if (SettlementSiteEvaluator.Evaluate(geography, id).IsHabitable) return id;
                    }
            return geography.GetTile(center, MapLodLevel.Low).TileId;
        }

        /// <summary>
        /// 优先读取 WSP1 二进制政治资产 (political-2026.wgeo.gz); 缺失时回退到旧
        /// political-2026.tsv (StreamingAssets 在 Task 5 重生前仍是旧 TSV)。
        /// 按 BorderView 选择国家视图 (de-facto 258 / sovereignty 209), 保留争议区标记。
        /// </summary>
        public static GeoPoliticalInit ReadGeoPolitical(string geoRoot, int borderYear, BorderView borderView)
        {
            if (borderYear != 2026)
                throw new NotSupportedException(
                    "Committed geo-v1 contains only the explicitly labelled 2026 border snapshot.");
            string wsp1Path = Path.Combine(geoRoot, "political-2026.wgeo.gz");
            if (File.Exists(wsp1Path))
                return ReadGeoPoliticalFromAsset(wsp1Path, borderYear, borderView);
            return ReadGeoPoliticalFromTsv(Path.Combine(geoRoot, "political-2026.tsv"), borderYear);
        }

        /// <summary>从 WSP1 资产聚合 GeoPoliticalInit: 按 BorderView 选国家, 保留争议标记, 过滤主要城市.</summary>
        public static GeoPoliticalInit ReadGeoPoliticalFromAsset(string path, int borderYear, BorderView borderView)
        {
            if (borderYear != 2026)
                throw new NotSupportedException(
                    "Committed geo-v1 contains only the explicitly labelled 2026 border snapshot.");
            var asset = PoliticalAssetReader.Read(path);
            var result = new GeoPoliticalInit { BorderYear = borderYear, BorderView = borderView };

            // 国家视图: WSP1 记录已按 (stableId, name) 排序; 直接消费, 不重排以保持确定性
            foreach (var rec in asset.CountriesByView(borderView))
            {
                var country = new CountryInit
                {
                    Name = rec.Name,
                    MinLat = double.PositiveInfinity, MinLon = double.PositiveInfinity,
                    MaxLat = double.NegativeInfinity, MaxLon = double.NegativeInfinity
                };
                foreach (var ring in rec.Rings)
                    foreach (var pt in ring.Points)
                    {
                        if (pt.Latitude < country.MinLat) country.MinLat = pt.Latitude;
                        if (pt.Latitude > country.MaxLat) country.MaxLat = pt.Latitude;
                        if (pt.Longitude < country.MinLon) country.MinLon = pt.Longitude;
                        if (pt.Longitude > country.MaxLon) country.MaxLon = pt.Longitude;
                    }
                if (double.IsInfinity(country.MinLat)) { country.MinLat = 0; country.MinLon = 0; country.MaxLat = 0; country.MaxLon = 0; }
                result.Countries.Add(country);
            }

            // 争议区标记: 按源 TYPE/NOTE_ADM0/NOTE_BRK + claimant 原样保留, 不编造裁决
            foreach (var d in asset.DisputedAreas)
            {
                var marker = new DisputedMarker
                {
                    Name = d.Name,
                    AdminClaimant = d.AdminName,
                    SovereignClaimant = d.SovereignName,
                    Type = d.Type,
                    NoteAdm0 = d.NoteAdm0,
                    NoteBrk = d.NoteBrk,
                    MinLat = double.PositiveInfinity, MinLon = double.PositiveInfinity,
                    MaxLat = double.NegativeInfinity, MaxLon = double.NegativeInfinity
                };
                foreach (var ring in d.Rings)
                    foreach (var pt in ring.Points)
                    {
                        if (pt.Latitude < marker.MinLat) marker.MinLat = pt.Latitude;
                        if (pt.Latitude > marker.MaxLat) marker.MaxLat = pt.Latitude;
                        if (pt.Longitude < marker.MinLon) marker.MinLon = pt.Longitude;
                        if (pt.Longitude > marker.MaxLon) marker.MaxLon = pt.Longitude;
                    }
                if (double.IsInfinity(marker.MinLat)) { marker.MinLat = 0; marker.MinLon = 0; marker.MaxLat = 0; marker.MaxLon = 0; }
                result.DisputedAreas.Add(marker);
            }

            // 主要城市: 首都 + 世界城市 + 巨型城市 (Task 3 concern 3: 7342 全量按需过滤)
            // 城市的 adminId 指向 de-facto 单元; de-facto 视图直接匹配, sovereignty 视图
            // 通过 de-facto 记录的 sovereignId 聚合到主权。
            var citiesByAdmin = new Dictionary<string, List<PoliticalCityRecord>>(StringComparer.Ordinal);
            foreach (var c in asset.Cities)
            {
                if (c.IsCapital == 0 && c.IsWorldCity == 0 && c.IsMegaCity == 0) continue;
                if (!citiesByAdmin.TryGetValue(c.AdminId, out var list))
                {
                    list = new List<PoliticalCityRecord>();
                    citiesByAdmin[c.AdminId] = list;
                }
                list.Add(c);
            }
            var viewRecords = asset.CountriesByView(borderView);
            if (borderView == BorderView.DeFactoControl)
            {
                for (int i = 0; i < viewRecords.Count; i++)
                {
                    if (citiesByAdmin.TryGetValue(viewRecords[i].AdminId, out var list))
                        AddCities(result.Countries[i], list);
                }
            }
            else // SovereigntyClaims: 收集所有 de-facto 单元 (sovereignId == 该主权 stableId) 的城市
            {
                var adminsBySovereign = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var d in asset.DeFactoCountries)
                {
                    if (!adminsBySovereign.TryGetValue(d.SovereignId, out var admins))
                    {
                        admins = new List<string>();
                        adminsBySovereign[d.SovereignId] = admins;
                    }
                    admins.Add(d.AdminId);
                }
                for (int i = 0; i < viewRecords.Count; i++)
                {
                    if (!adminsBySovereign.TryGetValue(viewRecords[i].StableId, out var admins)) continue;
                    // admins 已是 de-facto 记录按 (stableId, name) 排序后的 adminId 序; 稳定
                    foreach (var adminId in admins)
                        if (citiesByAdmin.TryGetValue(adminId, out var list))
                            AddCities(result.Countries[i], list);
                }
            }

            return result;
        }

        private static void AddCities(CountryInit country, List<PoliticalCityRecord> list)
        {
            // 同一国家内城市按 (name, stableId) 稳定序
            var sorted = new List<PoliticalCityRecord>(list);
            sorted.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Name, b.Name);
                if (c != 0) return c;
                return a.StableId.CompareTo(b.StableId);
            });
            foreach (var c in sorted)
                country.Cities.Add(new CityInit
                {
                    Name = c.Name,
                    Location = new GeoCoordinate(c.Latitude, c.Longitude),
                    Population = c.PopMax > 0 ? (int)Math.Min(int.MaxValue, c.PopMax) : 1000
                });
        }

        /// <summary>旧 TSV 回退路径 (StreamingAssets 在 Task 5 重生前仍是旧 TSV).</summary>
        public static GeoPoliticalInit ReadGeoPoliticalFromTsv(string path, int borderYear)
        {
            if (borderYear != 2026)
                throw new NotSupportedException("Committed geo-v1 contains only the explicitly labelled coarse 2026 border snapshot.");
            if (!File.Exists(path)) throw new FileNotFoundException("Geo-political seed missing", path);
            var result = new GeoPoliticalInit { BorderYear = borderYear };
            var byName = new Dictionary<string, CountryInit>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] p = raw.Split('\t');
                if (p.Length != 9) throw new InvalidDataException("Malformed political seed: " + raw);
                if (!byName.TryGetValue(p[0], out CountryInit country))
                {
                    country = new CountryInit
                    {
                        Name = p[0], MinLat = D(p[4]), MinLon = D(p[5]), MaxLat = D(p[6]), MaxLon = D(p[7])
                    };
                    byName[p[0]] = country;
                    result.Countries.Add(country);
                }
                country.Cities.Add(new CityInit
                {
                    Name = p[1], Location = new GeoCoordinate(D(p[2]), D(p[3])),
                    Population = int.Parse(p[8], CultureInfo.InvariantCulture)
                });
            }
            result.Countries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>旧签名保留: 仅 TSV 回退, 不支持 BorderView 切换.</summary>
        public static GeoPoliticalInit ReadGeoPolitical(string path, int borderYear)
            => ReadGeoPoliticalFromTsv(path, borderYear);

        private static double D(string text) => double.Parse(text, CultureInfo.InvariantCulture);
    }
}
