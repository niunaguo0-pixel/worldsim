namespace WorldSim.Presentation
{
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;

    /// <summary>HUD 可调用的干预命令边界；可变 WorldState 保留在运行器内部。</summary>
    public interface IPlayableInterventionSink
    {
        int GetEmergencyCooldownRemaining(EmergencyType type);
        void ApplyIntervention(string key, double delta, int durationMonths, int delayMonths);
        void ApplyEmergency(EmergencyType type, int delayMonths);
    }

    /// <summary>
    /// 可玩月循环 HUD：只消费时间快照，并通过命令接口暂停、变速和干预。
    /// </summary>
    public sealed class PlayableMonthLoopHud : MonoBehaviour
    {
        private const int MaxRecentEvents = 24;

        private readonly List<SimEvent> _recentEvents = new List<SimEvent>(MaxRecentEvents);
        private ITimePresentationSource _timeSource;
        private ITimeControlSink _timeControls;
        private IPlayableInterventionSink _interventions;
        private InterventionFxBridge _interventionFx;
        private CameraLodController _cameraLod;
        private PlayableInputController _input;
        private GenerationTimelinePresenter _generationTimeline = new GenerationTimelinePresenter();
        private IReadOnlyList<SimEvent> _lastConsumedEventSlice;
        private Vector2 _eventScroll;
        private string _toast = "可玩月循环：键鼠操控相机与时间，H 看帮助。";

        public void Bind(
            ITimePresentationSource timeSource,
            ITimeControlSink timeControls,
            IPlayableInterventionSink interventions,
            InterventionFxBridge interventionFx,
            CameraLodController cameraLod,
            PlayableInputController input = null)
        {
            _timeSource = timeSource;
            _timeControls = timeControls;
            _interventions = interventions;
            _interventionFx = interventionFx;
            _cameraLod = cameraLod;
            _input = input;
            _generationTimeline = new GenerationTimelinePresenter();
            _recentEvents.Clear();
            _lastConsumedEventSlice = null;
        }

        private void Update()
        {
            if (_timeSource == null || _timeControls == null) return;
            var snapshot = _timeSource.TimeSnapshot;
            ConsumeIncrementalEvents(snapshot.Events);
            // 键鼠路由由 PlayableInputController 统一处理，避免与 HUD 双重响应
        }

        private void OnGUI()
        {
            if (_timeSource == null || _timeControls == null) return;
            var snapshot = _timeSource.TimeSnapshot;
            ConsumeIncrementalEvents(snapshot.Events);

            const float pad = 12f;
            GUILayout.BeginArea(new Rect(pad, pad, 440f, Screen.height - pad * 2));
            GUILayout.BeginVertical("box");

            GUILayout.Label($"【请看 Game 窗口】WorldSim · 可玩月循环");
            GUILayout.Label(
                $"时间轴 第 {snapshot.GameYear} 年 / {SeasonName(snapshot.Season)} / " +
                $"第 {snapshot.MonthOfYear} 月 / 时代 {snapshot.EraIndex}");
            GUILayout.Label(
                $"速度 {snapshot.SpeedMultiplier}×  |  " +
                (snapshot.IsPaused ? "世界已暂停 · 世界冻结" : "世界推进中"));
            GUILayout.Label(
                $"人口 {snapshot.Population:0}  |  粮储 {snapshot.FoodReserve:0.###}  |  " +
                $"pending {snapshot.PendingCount}");
            if (_cameraLod != null)
            {
                GUILayout.Label(
                    $"视图 {_cameraLod.CurrentLodLabel}  |  " +
                    (_cameraLod.ReduceMotion ? "动效已降级" : "完整动效"));
            }

            if (_input != null)
            {
                string mode = _input.InterveneMode
                    ? $"干预模式 · {_input.CurrentPreset.Label}"
                    : "观察模式";
                GUILayout.Label($"输入 {mode}  |  {_input.StatusMessage}");
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(snapshot.IsPaused ? "继续 [Space]" : "暂停 [Space]", GUILayout.Height(28)))
                SetPaused(!snapshot.IsPaused);
            if (GUILayout.Button(SpeedLabel(snapshot, 1), GUILayout.Width(48), GUILayout.Height(28))) SetSpeed(1);
            if (GUILayout.Button(SpeedLabel(snapshot, 2), GUILayout.Width(48), GUILayout.Height(28))) SetSpeed(2);
            if (GUILayout.Button(SpeedLabel(snapshot, 5), GUILayout.Width(48), GUILayout.Height(28))) SetSpeed(5);
            if (GUILayout.Button(SpeedLabel(snapshot, 20), GUILayout.Width(56), GUILayout.Height(28))) SetSpeed(20);
            GUILayout.EndHorizontal();

            if (_input != null && _input.ShowHelp)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label(PlayableControlMap.HelpText());
                GUILayout.EndVertical();
            }
            else
            {
                GUILayout.Label("快捷键摘要：H 帮助 · WASD 平移 · 滚轮缩放 · I 干预 · Space 暂停");
            }

            if (snapshot.ShowHighSpeedHint)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label("⚠ 高速推进中：建议减速至 1× 响应应力窗口");
                if (GUILayout.Button("一键回到 1×", GUILayout.Height(28)))
                    SetSpeed(1);
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label("干预（延迟 1 游戏月生效）· 亦可 I→Q/E→Enter");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("降雨 +10", GUILayout.Height(28)))
                TryIntervene("rainfall_0", 10.0, "已预约降雨");
            if (GUILayout.Button("人口倾向", GUILayout.Height(28)))
                TryIntervene("population_1", 5.0, "已预约人口倾向");
            if (GUILayout.Button("农耕偏向", GUILayout.Height(28)))
                TryIntervene("devBias_agriculture_1", 0.2, "已预约农耕偏向");
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("紧急干预（24 月冷却）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(EmergencyLabel(EmergencyType.DivineRain, "甘霖"), GUILayout.Height(28)))
                TryEmergency(EmergencyType.DivineRain, "天降甘霖");
            if (GUILayout.Button(EmergencyLabel(EmergencyType.DivineShield, "护盾"), GUILayout.Height(28)))
                TryEmergency(EmergencyType.DivineShield, "神佑护盾");
            if (GUILayout.Button(EmergencyLabel(EmergencyType.LifeSpring, "生命泉"), GUILayout.Height(28)))
                TryEmergency(EmergencyType.LifeSpring, "生命之泉");
            GUILayout.EndHorizontal();

            if (_interventionFx != null && !string.IsNullOrEmpty(_interventionFx.LastFx))
                GUILayout.Label("因果链: " + _interventionFx.LastFx);

            GUILayout.Space(4);
            GUILayout.Label(_toast);

            DrawGenerationTimeline();

            GUILayout.Space(6);
            GUILayout.Label("近期事件");
            _eventScroll = GUILayout.BeginScrollView(_eventScroll, GUILayout.Height(180));
            var sb = new StringBuilder();
            for (int i = 0; i < _recentEvents.Count; i++)
            {
                var e = _recentEvents[i];
                sb.Append('M').Append(e.gameMonth).Append(' ')
                  .Append(e.category).Append(' ')
                  .Append(e.templateId).Append(" (")
                  .Append(e.magnitude.ToString("0.###")).Append(")\n");
            }
            GUILayout.TextArea(sb.Length == 0 ? "（尚无事件）" : sb.ToString(), GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private string EmergencyLabel(EmergencyType type, string name)
        {
            if (_interventions == null) return name;
            int cd = _interventions.GetEmergencyCooldownRemaining(type);
            return cd > 0 ? $"{name}({cd})" : name;
        }

        private void TryIntervene(string key, double delta, string okMsg)
        {
            if (_interventions == null || _timeSource == null) return;
            try
            {
                _interventions.ApplyIntervention(key, delta, durationMonths: 3, delayMonths: 1);
                _toast = okMsg + $" → 生效月 {_timeSource.TimeSnapshot.MonthIndex + 1}";
            }
            catch (System.Exception ex)
            {
                _toast = "干预失败: " + ex.Message;
            }
        }

        private void TryEmergency(EmergencyType type, string okMsg)
        {
            if (_interventions == null) return;
            try
            {
                _interventions.ApplyEmergency(type, delayMonths: 0);
                _toast = okMsg + " 已预约（当月结算生效）";
            }
            catch (System.Exception ex)
            {
                _toast = "紧急干预失败: " + ex.Message;
            }
        }

        private void SetPaused(bool paused)
        {
            _timeControls?.SetPaused(paused);
        }

        private void SetSpeed(int speedMultiplier)
        {
            _timeControls?.SetSpeedMultiplier(speedMultiplier);
        }

        private void ConsumeIncrementalEvents(IReadOnlyList<SimEvent> eventSlice)
        {
            if (eventSlice == null || ReferenceEquals(eventSlice, _lastConsumedEventSlice)) return;
            _lastConsumedEventSlice = eventSlice;
            _generationTimeline.Consume(eventSlice);

            for (int i = 0; i < eventSlice.Count; i++)
            {
                _recentEvents.Add(eventSlice[i]);
                if (_recentEvents.Count > MaxRecentEvents)
                    _recentEvents.RemoveAt(0);
            }
        }

        private void DrawGenerationTimeline()
        {
            var nodes = _generationTimeline.Nodes;
            if (nodes.Count == 0) return;

            GUILayout.Space(6);
            GUILayout.Label("世代时间轴");
            int start = System.Math.Max(0, nodes.Count - 6);
            for (int i = start; i < nodes.Count; i++)
            {
                GenerationTimelineNode node = nodes[i];
                GUILayout.Label(
                    $"M{node.GameMonth} · {GenerationEventName(node.Kind)} · ID {node.SourceId}");
            }
        }

        private static string GenerationEventName(GenerationTimelineKind kind)
        {
            switch (kind)
            {
                case GenerationTimelineKind.Death: return "死亡";
                case GenerationTimelineKind.Inheritance: return "继承";
                default: return "世代里程碑";
            }
        }

        private static string SeasonName(TimeSeason season)
        {
            switch (season)
            {
                case TimeSeason.Spring: return "春";
                case TimeSeason.Summer: return "夏";
                case TimeSeason.Autumn: return "秋";
                default: return "冬";
            }
        }

        private static string SpeedLabel(TimeViewSnapshot snapshot, int speedMultiplier)
        {
            return snapshot.SpeedMultiplier == speedMultiplier
                ? $"▶{speedMultiplier}×"
                : $"{speedMultiplier}×";
        }
    }
}
