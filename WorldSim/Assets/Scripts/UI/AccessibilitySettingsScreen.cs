namespace WorldSim.UI
{
    using UnityEngine;
    using WorldSim.Presentation;

    /// <summary>可访问性设置壳（IMGUI）：VS-8 Standard 开关。</summary>
    public sealed class AccessibilitySettingsScreen : MonoBehaviour
    {
        public bool IsVisible { get; private set; }

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Toggle() => IsVisible = !IsVisible;

        private void OnGUI()
        {
            if (!IsVisible) return;

            float scale = AccessibilitySettings.FontScale;
            float w = Mathf.Min(440f, Screen.width - 40f) * Mathf.Max(1f, scale);
            float h = 360f * Mathf.Max(1f, scale * 0.85f);
            Rect area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(area.x, area.y, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f)) *
                Matrix4x4.TRS(new Vector3(-area.x / scale, -area.y / scale, 0f), Quaternion.identity, Vector3.one);

            GUILayout.BeginArea(area, "WorldSim · 可访问性", GUI.skin.window);
            GUILayout.Label("标准档开关（art-bible §8.4 / VS-8）");

            ToggleBool("减少动态 / 无闪烁", AccessibilitySettings.ReduceMotion, AccessibilitySettings.SetReduceMotion);
            ToggleBool("高对比主题", AccessibilitySettings.HighContrast, AccessibilitySettings.SetHighContrast);
            ToggleBool("CVD 模式（图案层钩子）", AccessibilitySettings.CvdMode, AccessibilitySettings.SetCvdMode);

            GUILayout.Space(6);
            GUILayout.Label("字体缩放  " + Mathf.RoundToInt(AccessibilitySettings.FontScale * 100f) + "%");
            float nextScale = GUILayout.HorizontalSlider(
                AccessibilitySettings.FontScale,
                AccessibilitySettings.FontScaleMin,
                AccessibilitySettings.FontScaleMax);
            if (Mathf.Abs(nextScale - AccessibilitySettings.FontScale) > 0.001f)
                AccessibilitySettings.SetFontScale(nextScale);

            GUILayout.Space(8);
            GUILayout.Label("减少动态 ON：调色 ≥2.5s · Bloom×0.5 · 脉冲=0 · LOD 0.5s");
            GUILayout.Label("高对比 ON：Contrast+12 · 饱和≤−30 · 关暗角 · Bloom×0.4");
            GUILayout.Label("粒子上限（预留）: " + AccessibilitySettings.EffectiveParticleCap);
            GUILayout.Label("CVD 图案 alpha 钩子: " + CvdPatternHook.PatternOverlayAlpha.ToString("0.00"));

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("关闭", GUILayout.Height(28)))
                Hide();
            GUILayout.EndArea();
            GUI.matrix = prev;
        }

        private static void ToggleBool(string label, bool current, System.Action<bool> set)
        {
            bool next = GUILayout.Toggle(current, label);
            if (next != current)
                set(next);
        }
    }
}
