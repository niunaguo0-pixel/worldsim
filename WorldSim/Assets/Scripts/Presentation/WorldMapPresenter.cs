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
    /// P2/P4：NPR 微缩沙盘地球（美术圣经色板 + URP NPR Shader）；
    /// 相机 LOD 驱动 mesh 精度切换（只读快照，不写 WorldState）。
    /// </summary>
    public sealed class WorldMapPresenter : MonoBehaviour
    {
        public const float SphereRadius = 5f;
        public const float ElevationScale = 0.18f;
        public const float RotationDegreesPerSecond = 6f;
        public const float RotationStopDistance = 9f;

        private WorldMapViewSnapshot _snapshot;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private CameraLodLevel _appliedLod = (CameraLodLevel)(-1);
        private float _currentRotationSpeed;
        private bool _autoRotate = true;
        private bool _allowAutoRotate = true;

        /// <summary>当前相机距离，由 CameraLodController 写入；距离小则停转。</summary>
        public float CameraDistance { get; set; } = RotationStopDistance + 1f;

        public CameraLodLevel AppliedRenderLod => _appliedLod;

        public static GameObject Build(WorldMapViewSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.BundleAvailable)
                return BuildErrorPlaceholder(snapshot?.Error ?? "Geo bundle unavailable");

            var root = new GameObject("WorldSim_RealEarthMap");
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = NprMaterialFactory.CreateEarthMaterial();
            var presenter = root.AddComponent<WorldMapPresenter>();
            presenter._snapshot = snapshot;
            presenter._meshFilter = filter;
            presenter._meshRenderer = renderer;
            presenter._autoRotate = true;
            var decision = CameraLodPolicy.ForLevel(CameraLodLevel.Individual);
            filter.sharedMesh = BuildSphereMesh(
                snapshot, decision.MeshLonSegments, decision.MeshLatSegments, decision.ElevationScale);
            presenter._appliedLod = decision.Level;
            presenter._allowAutoRotate = decision.AllowAutoRotate;
            return root;
        }

        /// <summary>P4：按相机 LOD 决策重建 mesh 精度；同档不重建。</summary>
        public void ApplyRenderLod(CameraLodDecision decision)
        {
            if (_snapshot == null || !_snapshot.BundleAvailable) return;
            if (_meshFilter == null)
                _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null) return;
            if (_appliedLod == decision.Level) return;

            var old = _meshFilter.sharedMesh;
            _meshFilter.sharedMesh = BuildSphereMesh(
                _snapshot, decision.MeshLonSegments, decision.MeshLatSegments, decision.ElevationScale);
            if (old != null)
                DestroyImmediate(old);
            _appliedLod = decision.Level;
            _allowAutoRotate = decision.AllowAutoRotate;
        }

        public static Mesh BuildSphereMesh(WorldMapViewSnapshot snapshot) =>
            BuildSphereMesh(
                snapshot,
                WorldMapViewSnapshot.Width,
                WorldMapViewSnapshot.Height,
                ElevationScale);

        /// <summary>从全分辨率快照降采样生成 UV 球面；顶点色来自 NPR 色板。</summary>
        public static Mesh BuildSphereMesh(
            WorldMapViewSnapshot snapshot,
            int lonSegments,
            int latSegments,
            float elevationScale)
        {
            if (snapshot == null || snapshot.Tiles == null)
                throw new ArgumentNullException(nameof(snapshot));
            lonSegments = Math.Max(4, lonSegments);
            latSegments = Math.Max(2, latSegments);

            const int width = WorldMapViewSnapshot.Width;
            const int height = WorldMapViewSnapshot.Height;
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
                int sy = SampleY(row, latSegments, height);

                for (int col = 0; col <= lonSegments; col++)
                {
                    int sx = col == lonSegments ? 0 : SampleX(col, lonSegments, width);
                    double lon = -180.0 + (double)sx / width * 360.0;
                    double lonRad = lon * Math.PI / 180.0;
                    float cosLon = (float)Math.Cos(lonRad);
                    float sinLon = (float)Math.Sin(lonRad);

                    var tile = snapshot.Tiles[sy * width + sx];
                    float elev = tile.IsLand
                        ? (float)Math.Max(0.0, tile.ElevationMeters / 9000.0) * elevationScale
                        : 0f;
                    float r = SphereRadius + elev;

                    vertices[row * (lonSegments + 1) + col] = new Vector3(
                        r * cosLat * cosLon,
                        r * sinLat,
                        r * cosLat * sinLon);
                    colors[row * (lonSegments + 1) + col] = NprDioramaPalette.ColorForTile(tile);
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

            var mesh = new Mesh { name = "WorldSim_NprEarthSphere" };
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
            if (_meshRenderer != null && _meshRenderer.sharedMaterial != null)
                NprMaterialFactory.ApplyDetailStrength(_meshRenderer.sharedMaterial, CameraDistance);

            bool shouldRotate = _autoRotate && _allowAutoRotate && CameraDistance > RotationStopDistance;
            float targetSpeed = shouldRotate ? RotationDegreesPerSecond : 0f;
            _currentRotationSpeed = Mathf.Lerp(
                _currentRotationSpeed, targetSpeed,
                1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            if (Mathf.Abs(_currentRotationSpeed) > 0.001f)
                transform.Rotate(Vector3.up, _currentRotationSpeed * Time.unscaledDeltaTime, Space.World);
        }

        private static int SampleX(int col, int lonSegments, int width) =>
            Math.Min(width - 1, (int)Math.Round((double)col / lonSegments * width));

        private static int SampleY(int row, int latSegments, int height)
        {
            if (row <= 0) return 0;
            if (row >= latSegments) return height - 1;
            return Math.Min(height - 1, (int)Math.Round((double)row / latSegments * (height - 1)));
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
