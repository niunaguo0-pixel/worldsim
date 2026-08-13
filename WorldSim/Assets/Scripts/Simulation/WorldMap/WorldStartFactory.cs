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
        public readonly List<CountryInit> Countries = new List<CountryInit>();
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

            var political = ReadGeoPolitical(Path.Combine(geoRoot, "political-2026.tsv"), config.BorderYear);
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
                governance = GovernanceType.Chiefdom, lawFamily = LawFamily.CustomaryLaw
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
            foreach (var country in countries)
            {
                var polity = new CivilizationPolityState
                {
                    stableId = polityId, techTier = config.StartEra == StartEra.Modern ? 7 : 5,
                    lawStage = 4, stability = 0.6, legitimacy = 0.6, governance = GovernanceType.Kingdom,
                    lawFamily = law, titleTier = TitleTier.King, scaleTier = ScaleTier.Regional
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

        public static GeoPoliticalInit ReadGeoPolitical(string path, int borderYear)
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

        private static double D(string text) => double.Parse(text, CultureInfo.InvariantCulture);
    }
}
