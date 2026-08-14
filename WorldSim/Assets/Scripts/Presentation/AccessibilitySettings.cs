namespace WorldSim.Presentation
{
    using System;
    using UnityEngine;

    /// <summary>
    /// 可访问性运行时配置（art-bible §8.4 / asset-spec §7.6 / Sprint 02 VS-8）。
    /// 游戏内自实现；OS 仅作首次 Prefs 缺失时的建议源（桌面恒 false）。
    /// </summary>
    public static class AccessibilitySettings
    {
        public const string ReduceMotionPrefsKey = "worldsim.access.reduce_motion";
        public const string HighContrastPrefsKey = "worldsim.access.high_contrast";
        public const string CvdModePrefsKey = "worldsim.access.cvd_mode";
        public const string FontScalePrefsKey = "worldsim.access.font_scale";

        public const int ParticleCapBalanced = 5000;
        public const int ParticleCapReduceMotion = 1500;
        public const float FontScaleMin = 0.75f;
        public const float FontScaleMax = 1.5f;
        public const float FontScaleDefault = 1f;
        public const float LodCrossFadeNormalSeconds = 0.25f;
        public const float LodCrossFadeReduceMotionSeconds = 0.5f;

        private static bool _loaded;
        private static bool _reduceMotion;
        private static bool _highContrast;
        private static bool _cvdMode;
        private static float _fontScale = FontScaleDefault;

        public static bool ReduceMotion
        {
            get
            {
                EnsureLoaded();
                return _reduceMotion;
            }
        }

        public static bool HighContrast
        {
            get
            {
                EnsureLoaded();
                return _highContrast;
            }
        }

        public static bool CvdMode
        {
            get
            {
                EnsureLoaded();
                return _cvdMode;
            }
        }

        public static float FontScale
        {
            get
            {
                EnsureLoaded();
                return _fontScale;
            }
        }

        public static int EffectiveParticleCap =>
            ReduceMotion ? ParticleCapReduceMotion : ParticleCapBalanced;

        /// <summary>AX-1：减少动态 ON 时危机脉冲幅度强制 0。</summary>
        public static float CrisisPulseAmplitude => ReduceMotion ? 0f : 1f;

        /// <summary>asset-spec §7.4 ⑤：减少动态 ON → LOD cross-fade 0.5s；OFF 立即提交（无 pop 延迟）。</summary>
        public static float LodCrossFadeSeconds =>
            ReduceMotion ? LodCrossFadeReduceMotionSeconds : 0f;

        /// <summary>减少动态 ON：相机缓动 ×1.5。</summary>
        public static float CameraSmoothMultiplier => ReduceMotion ? 1.5f : 1f;

        /// <summary>CVD / 减少动态均要求图标+文字冗余强制开启。</summary>
        public static bool ForceIconTextRedundancy => CvdMode || ReduceMotion;

        /// <summary>桌面 PC 不依赖 OS reduce-motion；恒返回 false 作建议。</summary>
        public static bool SuggestReduceMotionFromOs() => false;

        public static void Load()
        {
            if (PlayerPrefs.HasKey(ReduceMotionPrefsKey))
                _reduceMotion = PlayerPrefs.GetInt(ReduceMotionPrefsKey, 0) != 0;
            else
                _reduceMotion = SuggestReduceMotionFromOs();

            _highContrast = PlayerPrefs.GetInt(HighContrastPrefsKey, 0) != 0;
            _cvdMode = PlayerPrefs.GetInt(CvdModePrefsKey, 0) != 0;
            _fontScale = PlayerPrefs.HasKey(FontScalePrefsKey)
                ? Mathf.Clamp(PlayerPrefs.GetFloat(FontScalePrefsKey, FontScaleDefault), FontScaleMin, FontScaleMax)
                : FontScaleDefault;
            _loaded = true;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(ReduceMotionPrefsKey, _reduceMotion ? 1 : 0);
            PlayerPrefs.SetInt(HighContrastPrefsKey, _highContrast ? 1 : 0);
            PlayerPrefs.SetInt(CvdModePrefsKey, _cvdMode ? 1 : 0);
            PlayerPrefs.SetFloat(FontScalePrefsKey, _fontScale);
            PlayerPrefs.Save();
            _loaded = true;
        }

        public static void SetReduceMotion(bool enabled)
        {
            _reduceMotion = enabled;
            Save();
        }

        public static void SetHighContrast(bool enabled)
        {
            _highContrast = enabled;
            Save();
        }

        public static void SetCvdMode(bool enabled)
        {
            _cvdMode = enabled;
            Save();
        }

        public static void SetFontScale(float scale)
        {
            _fontScale = Mathf.Clamp(scale, FontScaleMin, FontScaleMax);
            Save();
        }

        /// <summary>单测用：清内存态并删 Prefs。</summary>
        public static void ResetForTests()
        {
            if (PlayerPrefs.HasKey(ReduceMotionPrefsKey))
                PlayerPrefs.DeleteKey(ReduceMotionPrefsKey);
            if (PlayerPrefs.HasKey(HighContrastPrefsKey))
                PlayerPrefs.DeleteKey(HighContrastPrefsKey);
            if (PlayerPrefs.HasKey(CvdModePrefsKey))
                PlayerPrefs.DeleteKey(CvdModePrefsKey);
            if (PlayerPrefs.HasKey(FontScalePrefsKey))
                PlayerPrefs.DeleteKey(FontScalePrefsKey);
            _reduceMotion = false;
            _highContrast = false;
            _cvdMode = false;
            _fontScale = FontScaleDefault;
            _loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
                Load();
        }
    }

    /// <summary>CVD 图案层薄钩子（Sprint 02：标志位可测；图案资产后续 VS-11）。</summary>
    public static class CvdPatternHook
    {
        public static bool IsActive => AccessibilitySettings.CvdMode;

        public static bool ShouldForceIconText => AccessibilitySettings.ForceIconTextRedundancy;

        /// <summary>占位：返回图案层应叠加的 alpha（无资产时为 0）。</summary>
        public static float PatternOverlayAlpha => IsActive ? 0.18f : 0f;
    }
}
