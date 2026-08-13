namespace WorldSim.Presentation
{
    using System;
    using UnityEngine;
    using WorldSim.Simulation.Core.WorldGeography;

    public sealed class WorldMapViewSnapshot
    {
        public const int Width = 180;
        public const int Height = 90;
        public WorldTileData[] Tiles;
        public string BuildId;
        public bool BundleAvailable;
        public string Error;

        public static WorldMapViewSnapshot Capture(IWorldGeography geography, string buildId)
        {
            if (geography == null)
                return new WorldMapViewSnapshot { BundleAvailable = false, Error = "Geo bundle unavailable" };
            var tiles = new WorldTileData[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    double lon = -180 + (x + 0.5) * 360.0 / Width;
                    double lat = 90 - (y + 0.5) * 180.0 / Height;
                    tiles[y * Width + x] = geography.GetTile(
                        new GeoCoordinate(lat, lon), MapLodLevel.High);
                }
            return new WorldMapViewSnapshot
                { Tiles = tiles, BuildId = buildId ?? "", BundleAvailable = true, Error = "" };
        }
    }

    public sealed class WorldMapPresenter
    {
        public GameObject Build(WorldMapViewSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.BundleAvailable)
                return BuildErrorPlaceholder(snapshot?.Error ?? "Geo bundle unavailable");

            var root = new GameObject("WorldSim_RealEarthMap");
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard"));
            filter.sharedMesh = BuildMesh(snapshot);
            return root;
        }

        public Mesh BuildMesh(WorldMapViewSnapshot snapshot)
        {
            const int width = WorldMapViewSnapshot.Width;
            const int height = WorldMapViewSnapshot.Height;
            var vertices = new Vector3[(width + 1) * (height + 1)];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (int y = 0; y <= height; y++)
                for (int x = 0; x <= width; x++)
                {
                    int vertex = y * (width + 1) + x;
                    int sx = x == width ? 0 : x;
                    int sy = Math.Min(height - 1, y);
                    var tile = snapshot.Tiles[sy * width + sx];
                    float px = (x / (float)width - 0.5f) * 36f;
                    float pz = (0.5f - y / (float)height) * 18f;
                    float py = tile.IsLand ? (float)Math.Max(0.02, tile.ElevationMeters / 12000.0) : 0f;
                    vertices[vertex] = new Vector3(px, py, pz);
                    colors[vertex] = ColorFor(tile);
                    uv[vertex] = new Vector2(x / (float)width, y / (float)height);
                }
            var triangles = new int[width * height * 6];
            int t = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int a = y * (width + 1) + x;
                    int b = a + 1;
                    int c = a + width + 1;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            var mesh = new Mesh { name = "WorldSim_180x90_RealEarth" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Color ColorFor(WorldTileData tile)
        {
            if (!tile.IsLand) return new Color(0.08f, 0.25f, 0.46f);
            switch (tile.Biome)
            {
                case BiomeType.Ice: return new Color(0.85f, 0.92f, 0.95f);
                case BiomeType.Tundra: return new Color(0.60f, 0.66f, 0.58f);
                case BiomeType.BorealForest: return new Color(0.16f, 0.35f, 0.22f);
                case BiomeType.TemperateForest: return new Color(0.24f, 0.48f, 0.25f);
                case BiomeType.Grassland: return new Color(0.52f, 0.64f, 0.28f);
                case BiomeType.Desert: return new Color(0.78f, 0.64f, 0.34f);
                case BiomeType.Savanna: return new Color(0.66f, 0.58f, 0.24f);
                case BiomeType.TropicalRainforest: return new Color(0.08f, 0.38f, 0.18f);
                case BiomeType.Alpine: return new Color(0.48f, 0.45f, 0.42f);
                case BiomeType.Wetland: return new Color(0.18f, 0.48f, 0.43f);
                default: return Color.magenta;
            }
        }

        private static GameObject BuildErrorPlaceholder(string error)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            root.name = "WorldSim_GeoBundleError";
            root.transform.rotation = Quaternion.Euler(90, 0, 0);
            root.transform.localScale = new Vector3(8, 4, 1);
            var label = new GameObject("WorldSim_GeoBundleError_Label");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0, 0, -0.1f);
            label.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var text = label.AddComponent<TextMesh>();
            text.text = "地图数据缺失\n" + error;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.red;
            text.characterSize = 0.15f;
            return root;
        }
    }
}
