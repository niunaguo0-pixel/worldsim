namespace WorldSim.UI
{
    using UnityEngine;
    using WorldSim.Presentation;

    /// <summary>可访问性设置壳（IMGUI）：减少动态 ↔ AS-4 / Bloom。</summary>
    public sealed class AccessibilitySettingsScreen : MonoBehaviour
    {
        public bool IsVisible { get; private set; }

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Toggle() => IsVisible = !IsVisible;

        private void OnGUI()
        {
            if (!IsVisible) return;

            float w = Mathf.Min(420f, Screen.width - 40f);
            float h = 220f;
            Rect area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUILayout.BeginArea(area, "WorldSim · 可访问性", GUI.skin.window);
            GUILayout.Label("标准档开关（art-bible §8.4）");

            bool current = AccessibilitySettings.ReduceMotion;
            bool next = GUILayout.Toggle(current, "减少动态 / 无闪烁");
            if (next != current)
                AccessibilitySettings.SetReduceMotion(next);

            GUILayout.Space(8);
            GUILayout.Label("开启后：灾害调色过渡 ≥2.5s · Bloom 减半");
            GUILayout.Label("粒子上限（预留）: " + AccessibilitySettings.EffectiveParticleCap);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("关闭", GUILayout.Height(28)))
                Hide();
            GUILayout.EndArea();
        }
    }
}
