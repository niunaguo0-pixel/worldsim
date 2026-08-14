namespace WorldSim.Presentation
{
    using System;
    using UnityEngine;

    /// <summary>
    /// NPR 打磨：四季/灾害调色纯函数（asset-spec §7.1–7.2 / 圣经 AS-4）。
    /// 不写 WorldState；Volume 应用层只消费本结构。
    /// </summary>
    public static class DioramaGradeMath
    {
        public const float BaseSaturation = -20f;
        public const float BaseContrast = 0f;
        public const float BloomIntensity = 0.35f;
        public const float BloomThreshold = 1.1f;
        public const float BloomScatter = 0.6f;
        public const float VignetteIntensity = 0.18f;
        public const float VignetteSmoothness = 0.4f;

        public const float DroughtFilterR = 0x8B / 255f;
        public const float DroughtFilterG = 0x7D / 255f;
        public const float DroughtFilterB = 0x3C / 255f;
        public const float DroughtFilterWeight = 0.22f;
        public const float DroughtSaturation = -35f;
        public const float DroughtExposure = -0.15f;

        public const float MinTransitionSeconds = 1.5f;
        public const float ReduceMotionMinTransitionSeconds = 2.5f;

        public struct GradeSample
        {
            public float Temperature;
            public float Tint;
            public float Saturation;
            public float Contrast;
            public float PostExposure;
            public Color ColorFilter;
            public float ColorFilterBlend;
            public float BloomIntensity;
            public float BloomThreshold;
            public float BloomScatter;
            public float VignetteIntensity;
            public float VignetteSmoothness;
        }

        public static float TemperatureForSeason(TimeSeason season)
        {
            switch (season)
            {
                case TimeSeason.Spring: return 8.1f;
                case TimeSeason.Summer: return 2.7f;
                case TimeSeason.Autumn: return -5.4f;
                case TimeSeason.Winter: return -10.8f;
                default: return 2.7f;
            }
        }

        /// <summary>
        /// 游戏月实时换算 = MONTH_SECONDS / max(1, speed)；再与 AS-4 下限取 max。
        /// </summary>
        public static float TransitionSeconds(
            double monthSeconds,
            int speedMultiplier,
            bool reduceMotion)
        {
            if (monthSeconds <= 0.0) throw new ArgumentOutOfRangeException(nameof(monthSeconds));
            int speed = Math.Max(1, speedMultiplier);
            float monthReal = (float)(monthSeconds / speed);
            float floor = reduceMotion ? ReduceMotionMinTransitionSeconds : MinTransitionSeconds;
            return Math.Max(monthReal, floor);
        }

        public static GradeSample SampleSeason(TimeSeason season)
        {
            return new GradeSample
            {
                Temperature = TemperatureForSeason(season),
                Tint = 0f,
                Saturation = BaseSaturation,
                Contrast = BaseContrast,
                PostExposure = 0f,
                ColorFilter = Color.white,
                ColorFilterBlend = 0f,
                BloomIntensity = BloomIntensity,
                BloomThreshold = BloomThreshold,
                BloomScatter = BloomScatter,
                VignetteIntensity = VignetteIntensity,
                VignetteSmoothness = VignetteSmoothness
            };
        }

        public static GradeSample SampleDrought()
        {
            var s = SampleSeason(TimeSeason.Summer);
            s.ColorFilter = new Color(DroughtFilterR, DroughtFilterG, DroughtFilterB, 1f);
            s.ColorFilterBlend = DroughtFilterWeight;
            s.Saturation = DroughtSaturation;
            s.PostExposure = DroughtExposure;
            return s;
        }

        /// <summary>灾害权重 ∈[0,1]：在季节基座上叠加旱灾偏色（参数化，非独立 LUT）。</summary>
        public static GradeSample Lerp(GradeSample from, GradeSample to, float t)
        {
            t = Mathf.Clamp01(t);
            return new GradeSample
            {
                Temperature = Mathf.Lerp(from.Temperature, to.Temperature, t),
                Tint = Mathf.Lerp(from.Tint, to.Tint, t),
                Saturation = Mathf.Lerp(from.Saturation, to.Saturation, t),
                Contrast = Mathf.Lerp(from.Contrast, to.Contrast, t),
                PostExposure = Mathf.Lerp(from.PostExposure, to.PostExposure, t),
                ColorFilter = Color.Lerp(from.ColorFilter, to.ColorFilter, t),
                ColorFilterBlend = Mathf.Lerp(from.ColorFilterBlend, to.ColorFilterBlend, t),
                BloomIntensity = Mathf.Lerp(from.BloomIntensity, to.BloomIntensity, t),
                BloomThreshold = Mathf.Lerp(from.BloomThreshold, to.BloomThreshold, t),
                BloomScatter = Mathf.Lerp(from.BloomScatter, to.BloomScatter, t),
                VignetteIntensity = Mathf.Lerp(from.VignetteIntensity, to.VignetteIntensity, t),
                VignetteSmoothness = Mathf.Lerp(from.VignetteSmoothness, to.VignetteSmoothness, t)
            };
        }

        public static GradeSample Compose(TimeSeason season, float droughtWeight)
        {
            var seasonGrade = SampleSeason(season);
            if (droughtWeight <= 0f) return seasonGrade;
            return Lerp(seasonGrade, SampleDrought(), Mathf.Clamp01(droughtWeight));
        }
    }
}
