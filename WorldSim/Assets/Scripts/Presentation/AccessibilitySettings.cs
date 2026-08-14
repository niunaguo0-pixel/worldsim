namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>
    /// 可访问性运行时配置壳（art-bible §8.4 / asset-spec §7.6）。
    /// 游戏内自实现；OS 仅作首次 Prefs 缺失时的建议源（桌面恒 false）。
    /// </summary>
    public static class AccessibilitySettings
    {
        public const string ReduceMotionPrefsKey = "worldsim.access.reduce_motion";
        public const int ParticleCapBalanced = 5000;
        public const int ParticleCapReduceMotion = 1500;

        private static bool _loaded;
        private static bool _reduceMotion;

        public static bool ReduceMotion
        {
            get
            {
                EnsureLoaded();
                return _reduceMotion;
            }
        }

        public static int EffectiveParticleCap =>
            ReduceMotion ? ParticleCapReduceMotion : ParticleCapBalanced;

        /// <summary>桌面 PC 不依赖 OS reduce-motion；恒返回 false 作建议。</summary>
        public static bool SuggestReduceMotionFromOs() => false;

        public static void Load()
        {
            if (PlayerPrefs.HasKey(ReduceMotionPrefsKey))
                _reduceMotion = PlayerPrefs.GetInt(ReduceMotionPrefsKey, 0) != 0;
            else
                _reduceMotion = SuggestReduceMotionFromOs();
            _loaded = true;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(ReduceMotionPrefsKey, _reduceMotion ? 1 : 0);
            PlayerPrefs.Save();
            _loaded = true;
        }

        public static void SetReduceMotion(bool enabled)
        {
            _reduceMotion = enabled;
            Save();
        }

        /// <summary>单测用：清内存态并删 Prefs，不写盘建议值。</summary>
        public static void ResetForTests()
        {
            if (PlayerPrefs.HasKey(ReduceMotionPrefsKey))
                PlayerPrefs.DeleteKey(ReduceMotionPrefsKey);
            _reduceMotion = false;
            _loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (!_loaded)
                Load();
        }
    }
}
