namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>P2：微缩沙盘柔和方向光 + 环境光（不写逻辑态）。</summary>
    public static class DioramaLightingBootstrap
    {
        public const string LightName = "WorldSim_DioramaKeyLight";

        public static Light EnsureKeyLight()
        {
            var existing = GameObject.Find(LightName);
            Light light;
            if (existing != null)
            {
                light = existing.GetComponent<Light>();
                if (light != null) return Configure(light);
            }

            var go = new GameObject(LightName);
            light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            return Configure(light);
        }

        private static Light Configure(Light light)
        {
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 0.85f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.24f, 0.20f);
            RenderSettings.ambientIntensity = 0.9f;
            return light;
        }
    }
}
