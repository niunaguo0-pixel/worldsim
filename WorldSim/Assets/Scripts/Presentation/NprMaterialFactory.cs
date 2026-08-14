namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>P2：创建 NPR 微缩沙盘材质；找不到自定义 Shader 时回退 URP Unlit。</summary>
    public static class NprMaterialFactory
    {
        public const string ShaderName = "WorldSim/NprDiorama";

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
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))
                mat.color = Color.white;
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
            return mat;
        }
    }
}
