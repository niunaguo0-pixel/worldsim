namespace WorldSim.Presentation
{
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// NPR 打磨：运行时 Global Volume（VOL_GLOBAL_BASE + 旱灾叠加）。
    /// 饱和度权威在 Volume（−20）；不回写 WorldState。
    /// </summary>
    public sealed class DioramaVolumeController : MonoBehaviour
    {
        public const string VolumeObjectName = "WorldSim_VOL_GLOBAL_BASE";

        private Volume _volume;
        private ColorAdjustments _colorAdjustments;
        private WhiteBalance _whiteBalance;
        private Bloom _bloom;
        private Vignette _vignette;
        private Tonemapping _tonemapping;

        private TimeSeason _displayedSeason = TimeSeason.Summer;
        private float _droughtWeight;
        private float _droughtTarget;
        private float _transitionSeconds = DioramaGradeMath.MinTransitionSeconds;

        public float DroughtWeight => _droughtWeight;
        public TimeSeason DisplayedSeason => _displayedSeason;

        public DioramaGradeMath.GradeSample CurrentSample =>
            DioramaGradeMath.Compose(_displayedSeason, _droughtWeight);

        public static DioramaVolumeController EnsureOn(GameObject host)
        {
            if (host == null) throw new System.ArgumentNullException(nameof(host));
            var existing = host.GetComponent<DioramaVolumeController>();
            if (existing != null)
            {
                existing.EnsureVolume();
                return existing;
            }

            var c = host.AddComponent<DioramaVolumeController>();
            c.EnsureVolume();
            return c;
        }

        public void EnsureVolume()
        {
            if (_volume != null) return;

            var go = GameObject.Find(VolumeObjectName);
            if (go == null)
                go = new GameObject(VolumeObjectName);

            _volume = go.GetComponent<Volume>();
            if (_volume == null)
                _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 0f;
            if (_volume.profile == null)
                _volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

            _colorAdjustments = GetOrAdd<ColorAdjustments>(_volume.profile);
            _whiteBalance = GetOrAdd<WhiteBalance>(_volume.profile);
            _bloom = GetOrAdd<Bloom>(_volume.profile);
            _vignette = GetOrAdd<Vignette>(_volume.profile);
            _tonemapping = GetOrAdd<Tonemapping>(_volume.profile);

            _tonemapping.mode.Override(TonemappingMode.Neutral);
            Apply(DioramaGradeMath.SampleSeason(TimeSeason.Summer));
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
                component = profile.Add<T>(true);
            component.active = true;
            return component;
        }

        public void Tick(
            TimeSeason season,
            bool anySettlementUnderDisaster,
            int speedMultiplier,
            bool reduceMotion,
            float unscaledDeltaTime)
        {
            EnsureVolume();
            _displayedSeason = season;
            _droughtTarget = anySettlementUnderDisaster ? 1f : 0f;
            _transitionSeconds = DioramaGradeMath.TransitionSeconds(
                TimeDriver.MONTH_SECONDS, speedMultiplier, reduceMotion);

            float step = unscaledDeltaTime <= 0f
                ? 1f
                : Mathf.Clamp01(unscaledDeltaTime / Mathf.Max(0.0001f, _transitionSeconds));
            _droughtWeight = Mathf.MoveTowards(_droughtWeight, _droughtTarget, step);
            var sample = DioramaGradeMath.Compose(season, _droughtWeight);
            if (reduceMotion)
                sample = DioramaGradeMath.ApplyReduceMotion(sample);
            if (AccessibilitySettings.HighContrast)
                sample = DioramaGradeMath.ApplyHighContrast(sample);
            Apply(sample);
        }

        /// <summary>单测/调试：瞬时设置旱灾权重（仍不写逻辑态）。</summary>
        public void SetDroughtWeightImmediate(float weight)
        {
            EnsureVolume();
            _droughtWeight = Mathf.Clamp01(weight);
            _droughtTarget = _droughtWeight;
            Apply(DioramaGradeMath.Compose(_displayedSeason, _droughtWeight));
        }

        private void Apply(DioramaGradeMath.GradeSample g)
        {
            if (_colorAdjustments != null)
            {
                _colorAdjustments.saturation.Override(g.Saturation);
                _colorAdjustments.contrast.Override(g.Contrast);
                _colorAdjustments.postExposure.Override(g.PostExposure);
                Color filter = Color.Lerp(Color.white, g.ColorFilter, g.ColorFilterBlend);
                _colorAdjustments.colorFilter.Override(filter);
            }

            if (_whiteBalance != null)
            {
                _whiteBalance.temperature.Override(g.Temperature);
                _whiteBalance.tint.Override(g.Tint);
            }

            if (_bloom != null)
            {
                _bloom.intensity.Override(g.BloomIntensity);
                _bloom.threshold.Override(g.BloomThreshold);
                _bloom.scatter.Override(g.BloomScatter);
            }

            if (_vignette != null)
            {
                _vignette.intensity.Override(g.VignetteIntensity);
                _vignette.smoothness.Override(g.VignetteSmoothness);
            }

            if (_tonemapping != null)
                _tonemapping.mode.Override(TonemappingMode.Neutral);
        }
    }
}
