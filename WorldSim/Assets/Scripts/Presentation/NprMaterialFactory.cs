namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>P2 / NPR 打磨：创建微缩沙盘材质（手绘 detail 探针 + Water 变体）。</summary>
    public static class NprMaterialFactory
    {
        public const string ShaderName = "WorldSim/NprDiorama";
        public const string WaterShaderName = "WorldSim/NprWater";

        public static Material CreateEarthMaterial()
        {
            var shader = Shader.Find(ShaderName)
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "WorldSim_NprEarth" };
            if (mat.HasProperty("_RimColor"))
                mat.SetColor("_RimColor", NprDioramaPalette.DeepBrown);
            if (mat.HasProperty("_RimPower"))
                mat.SetFloat("_RimPower", 2.5f);
            // 全局饱和度以 Volume −20 为准（asset-spec §7.1）；shader 近 1.0 避免叠乘过灰
            if (mat.HasProperty("_Saturation"))
                mat.SetFloat("_Saturation", 0.95f);
            if (mat.HasProperty("_Brightness"))
                mat.SetFloat("_Brightness", 1.05f);
            if (mat.HasProperty("_WaterColor"))
                mat.SetColor("_WaterColor", NprDioramaPalette.WaterBlue);
            if (mat.HasProperty("_DetailMap"))
                mat.SetTexture("_DetailMap", NprDetailProbeFactory.GetOrCreate());
            if (mat.HasProperty("_DetailStrength"))
                mat.SetFloat("_DetailStrength", 0.35f);
            if (mat.HasProperty("_DetailTiling"))
                mat.SetFloat("_DetailTiling", 8f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))
                mat.color = Color.white;
            return mat;
        }

        public static Material CreateWaterMaterial()
        {
            var shader = Shader.Find(WaterShaderName)
                ?? Shader.Find(ShaderName)
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "WorldSim_NprWater" };
            Color water = NprDioramaPalette.WaterBlue;
            water.a = 0.85f;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", water);
            if (mat.HasProperty("_Color"))
                mat.color = water;
            if (mat.HasProperty("_RimColor"))
                mat.SetColor("_RimColor", NprDioramaPalette.DeepBrown);
            if (mat.HasProperty("_WaveStrength"))
                mat.SetFloat("_WaveStrength", 0.015f);
            return mat;
        }

        public static Material CreateSettlementMaterial()
        {
            var mat = CreateEarthMaterial();
            mat.name = "WorldSim_NprSettlement";
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", NprDioramaPalette.SettlementFill);
            if (mat.HasProperty("_Color"))
                mat.color = NprDioramaPalette.SettlementFill;
            if (mat.HasProperty("_DetailStrength"))
                mat.SetFloat("_DetailStrength", 0.15f);
            return mat;
        }

        public static void ApplyDetailStrength(Material mat, float cameraDistance)
        {
            if (mat == null || !mat.HasProperty("_DetailStrength")) return;
            mat.SetFloat("_DetailStrength", NprDetailProbeFactory.DetailStrengthForCameraDistance(cameraDistance));
        }
    }
}
