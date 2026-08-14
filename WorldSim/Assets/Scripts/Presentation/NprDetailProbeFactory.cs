namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>
    /// NPR 顺延：运行时生成可平铺手绘温度探针（TEX_DETAIL，512）。
    /// 笔触约 5–15 px@512；平均饱和度落在圣经 25%–35% 带；不写 WorldState。
    /// </summary>
    public static class NprDetailProbeFactory
    {
        public const int Resolution = 512;
        public const string TextureName = "TEX_DETAIL_TERRAIN_PROBE";

        private static Texture2D _cached;

        public static Texture2D GetOrCreate()
        {
            if (_cached != null) return _cached;
            _cached = Build(Resolution, seed: 0x4E5052); // 'NPR'
            _cached.name = TextureName;
            _cached.wrapMode = TextureWrapMode.Repeat;
            _cached.filterMode = FilterMode.Bilinear;
            _cached.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return _cached;
        }

        /// <summary>确定性生成；供单测断言笔触与饱和度。</summary>
        public static Texture2D Build(int size, uint seed)
        {
            size = Mathf.Clamp(size, 64, 1024);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false);
            var pixels = new Color[size * size];

            // 温润中性底（低饱和大地色）——目标平均饱和度约 25%–35%
            var baseCol = new Color(0.76f, 0.71f, 0.62f, 1f);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = baseCol;

            var rng = new ProbeRng(seed);
            int strokes = Mathf.Max(40, size / 5);
            for (int s = 0; s < strokes; s++)
            {
                float x0 = rng.NextFloat() * size;
                float y0 = rng.NextFloat() * size;
                float angle = rng.NextFloat() * Mathf.PI * 2f;
                float length = 18f + rng.NextFloat() * 48f; // ~5–15px 宽、较长笔触
                float width = 5f + rng.NextFloat() * 10f;
                // 笔触本身也压饱和，避免整图均值飙出圣经带
                Color ink = Color.Lerp(
                    new Color(0.62f, 0.52f, 0.40f),
                    new Color(0.72f, 0.64f, 0.48f),
                    rng.NextFloat());
                ink = Color.Lerp(ink, baseCol, 0.35f + rng.NextFloat() * 0.25f);
                ink.a = 1f;

                int steps = Mathf.CeilToInt(length);
                for (int t = 0; t < steps; t++)
                {
                    float u = t / (float)steps;
                    float px = x0 + Mathf.Cos(angle) * length * u;
                    float py = y0 + Mathf.Sin(angle) * length * u;
                    Stamp(pixels, size, px, py, width * (0.65f + 0.35f * (1f - u)), ink, 0.12f);
                }
            }

            tex.SetPixels(pixels);
            return tex;
        }

        public static float AverageSaturation(Texture2D tex)
        {
            if (tex == null) throw new System.ArgumentNullException(nameof(tex));
            Color[] pixels = tex.GetPixels();
            double sum = 0;
            for (int i = 0; i < pixels.Length; i++)
                sum += Saturation(pixels[i]);
            return (float)(sum / pixels.Length);
        }

        public static float DetailStrengthForCameraDistance(float cameraDistance)
        {
            if (cameraDistance <= CameraLodPolicy.IndividualMaxDistance)
                return 0.40f;
            if (cameraDistance <= CameraLodPolicy.SettlementMaxDistance)
                return 0.20f;
            return 0f;
        }

        private static float Saturation(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (max <= 1e-5f) return 0f;
            return (max - min) / max;
        }

        private static void Stamp(Color[] pixels, int size, float x, float y, float radius, Color ink, float alpha)
        {
            int r = Mathf.CeilToInt(radius);
            int cx = Mathf.FloorToInt(x);
            int cy = Mathf.FloorToInt(y);
            float r2 = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int px = Mod(cx + dx, size);
                int py = Mod(cy + dy, size);
                int i = py * size + px;
                pixels[i] = Color.Lerp(pixels[i], ink, alpha);
            }
        }

        private static int Mod(int v, int m)
        {
            int r = v % m;
            return r < 0 ? r + m : r;
        }

        private struct ProbeRng
        {
            private uint _state;
            public ProbeRng(uint seed) { _state = seed == 0 ? 1u : seed; }
            public float NextFloat()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (_state & 0x00FFFFFFu) / 16777216f;
            }
        }
    }
}
