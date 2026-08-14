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

    /// <summary>
    /// 真实地球球形表现：生成 UV 球面 mesh，按经纬度采样真实生物群系顶点色与高程位移；
    /// 缩远时自动自转，缩近时停止自转以便观察地表细节。
    /// </summary>
    public sealed class WorldMapPresenter : MonoBehaviour
    {
        public const float SphereRadius = 5f;
        public const float ElevationScale = 0.18f;
        public const float RotationDegreesPerSecond = 6f;
        public const float RotationStopDistance = 9f;

        private float _currentRotationSpeed;
        private bool _autoRotate = true;

        /// <summary>当前相机距离，由 CameraLodController 写入；距离小则停转。</summary>
        public float CameraDistance { get; set; } = RotationStopDistance + 1f;

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
            filter.sharedMesh = BuildSphereMesh(snapshot);
            var presenter = root.AddComponent<WorldMapPresenter>();
            presenter._autoRotate = true;
            return root;
        }

        /// <summary>生成 UV 球面 mesh：纬度环 × 经度段，顶点色来自真实生物群系，Y 位移来自高程。</summary>
        public Mesh BuildSphereMesh(WorldMapViewSnapshot snapshot)
        {
            const int width = WorldMapViewSnapshot.Width;
            const int height = WorldMapViewSnapshot.Height;
            int latSegments = height;
            int lonSegments = width;
            int vertexCount = (latSegments + 1) * (lonSegments + 1);
            var vertices = new Vector3[vertexCount];
            var colors = new Color[vertexCount];
            var uv = new Vector2[vertexCount];

            for (int row = 0; row <= latSegments; row++)
            {
                double lat = 90.0 - (double)row / latSegments * 180.0;
                double latRad = lat * Math.PI / 180.0;
                float cosLat = (float)Math.Cos(latRad);
                float sinLat = (float)Math.Sin(latRad);
                int sy = Math.Min(height - 1, row == 0 ? 0 : row - 1);
                if (row == latSegments) sy = height - 1;

                for (int col = 0; col <= lonSegments; col++)
                {
                    int sx = col == lonSegments ? 0 : col;
                    double lon = -180.0 + (double)sx / width * 360.0;
                    double lonRad = lon * Math.PI / 180.0;
                    float cosLon = (float)Math.Cos(lonRad);
                    float sinLon = (float)Math.Sin(lonRad);

                    var tile = snapshot.Tiles[sy * width + sx];
                    float elev = tile.IsLand
                        ? (float)Math.Max(0.0, tile.ElevationMeters / 9000.0) * ElevationScale
                        : 0f;
                    float r = SphereRadius + elev;

                    vertices[row * (lonSegments + 1) + col] = new Vector3(
                        r * cosLat * cosLon,
                        r * sinLat,
                        r * cosLat * sinLon);
                    colors[row * (lonSegments + 1) + col] = ColorFor(tile);
                    uv[row * (lonSegments + 1) + col] = new Vector2(
                        (float)col / lonSegments, (float)row / latSegments);
                }
            }

            int triangleCount = latSegments * lonSegments * 6;
            var triangles = new int[triangleCount];
            int t = 0;
            for (int row = 0; row < latSegments; row++)
            {
                for (int col = 0; col < lonSegments; col++)
                {
                    int a = row * (lonSegments + 1) + col;
                    int b = a + 1;
                    int c = a + (lonSegments + 1);
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "WorldSim_RealEarthSphere" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Update()
        {
            bool shouldRotate = _autoRotate && CameraDistance > RotationStopDistance;
            float targetSpeed = shouldRotate ? RotationDegreesPerSecond : 0f;
            _currentRotationSpeed = Mathf.Lerp(
                _currentRotationSpeed, targetSpeed,
                1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            if (Mathf.Abs(_currentRotationSpeed) > 0.001f)
                transform.Rotate(Vector3.up, _currentRotationSpeed * Time.unscaledDeltaTime, Space.World);
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
