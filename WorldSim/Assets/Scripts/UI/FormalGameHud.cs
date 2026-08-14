namespace WorldSim.UI
{
    using System.Collections.Generic;
    using UnityEngine;
    using WorldSim.Narrative;
    using WorldSim.Presentation;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;

    /// <summary>
    /// S8 正式 HUD：时间轴 / 生态·文明·威胁三面板 / 干预 / 编年史。
    /// 只读快照 + 命令接口；不持有 WorldState。
    /// </summary>
    public sealed class FormalGameHud : MonoBehaviour
    {
        private enum HudTab : byte { Overview = 0, Ecology = 1, Civilization = 2, Threat = 3, Chronicle = 4 }

        private ITimePresentationSource _timeSource;
        private ITimeControlSink _timeControls;
        private IPlayableInterventionSink _interventions;
        private InterventionFxBridge _fx;
        private CameraLodController _cameraLod;
        private PlayableInputController _input;
        private EmergentNarrativeEngine _narrative = new EmergentNarrativeEngine();
        private GenerationTimelinePresenter _generation = new GenerationTimelinePresenter();
        private IReadOnlyList<SimEvent> _lastSlice;
        private HudTab _tab = HudTab.Overview;
        private GoalMode _goalMode = GoalMode.SandboxNoVictory;
        private string _startSummary = "";
        private Vector2 _scroll;
        private string _toast = "正式 HUD 已就绪。";

        public EmergentNarrativeEngine Narrative => _narrative;

        public void Bind(
            ITimePresentationSource timeSource,
            ITimeControlSink timeControls,
            IPlayableInterventionSink interventions,
            InterventionFxBridge fx,
            CameraLodController cameraLod,
            PlayableInputController input,
            GoalMode goalMode,
            string startSummary)
        {
            _timeSource = timeSource;
            _timeControls = timeControls;
            _interventions = interventions;
            _fx = fx;
            _cameraLod = cameraLod;
            _input = input;
            _goalMode = goalMode;
            _startSummary = startSummary ?? "";
            _narrative = new EmergentNarrativeEngine();
            _generation = new GenerationTimelinePresenter();
            _lastSlice = null;
            enabled = true;
        }

        private void Update()
        {
            if (_timeSource == null) return;
            Consume(_timeSource.TimeSnapshot.Events);
        }

        private void OnGUI()
        {
            if (_timeSource == null || _timeControls == null) return;
            var snap = _timeSource.TimeSnapshot;
            Consume(snap.Events);

            GUILayout.BeginArea(new Rect(12f, 12f, 460f, Screen.height - 24f));
            GUILayout.BeginVertical("box");
            GUILayout.Label("WorldSim · 正式 HUD（S8）");
            GUILayout.Label(_startSummary);
            GUILayout.Label("目标 · " + NewGameAssembler.GoalModeLabel(_goalMode));
            GUILayout.Label(
                $"第 {snap.GameYear} 年 / {SeasonName(snap.Season)} / 第 {snap.MonthOfYear} 月 / 时代 {snap.EraIndex}");
            GUILayout.Label(
                $"速度 {snap.SpeedMultiplier}× | " +
                (snap.IsPaused ? "已暂停" : "推进中") +
                $" | 人口 {snap.Population:0} | 粮储 {snap.FoodReserve:0.###}");
            if (_cameraLod != null)
                GUILayout.Label($"视图 {_cameraLod.CurrentLodLabel}");

            GUILayout.BeginHorizontal();
            TabButton(HudTab.Overview, "总览");
            TabButton(HudTab.Ecology, "生态");
            TabButton(HudTab.Civilization, "文明");
            TabButton(HudTab.Threat, "威胁");
            TabButton(HudTab.Chronicle, "编年史");
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Screen.height - 220f));
            switch (_tab)
            {
                case HudTab.Overview: DrawOverview(snap); break;
                case HudTab.Ecology: DrawEcology(snap); break;
                case HudTab.Civilization: DrawCivilization(snap); break;
                case HudTab.Threat: DrawThreat(snap); break;
                case HudTab.Chronicle: DrawChronicle(); break;
            }
            GUILayout.EndScrollView();

            GUILayout.Label(_toast);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawOverview(TimeViewSnapshot snap)
        {
            GUILayout.Label("时间控制");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(snap.IsPaused ? "继续" : "暂停", GUILayout.Height(28)))
                _timeControls.SetPaused(!snap.IsPaused);
            if (GUILayout.Button("1×", GUILayout.Height(28))) _timeControls.SetSpeedMultiplier(1);
            if (GUILayout.Button("2×", GUILayout.Height(28))) _timeControls.SetSpeedMultiplier(2);
            if (GUILayout.Button("5×", GUILayout.Height(28))) _timeControls.SetSpeedMultiplier(5);
            if (GUILayout.Button("20×", GUILayout.Height(28))) _timeControls.SetSpeedMultiplier(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("干预（延迟 1 月）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("降雨+10", GUILayout.Height(26)))
                TryIntervene("rainfall_0", 10.0, "降雨");
            if (GUILayout.Button("人口倾向", GUILayout.Height(26)))
                TryIntervene("population_1", 5.0, "人口倾向");
            if (GUILayout.Button("农耕倾向", GUILayout.Height(26)))
                TryIntervene("devBias_agriculture_1", 0.2, "农耕倾向");
            GUILayout.EndHorizontal();

            GUILayout.Label("紧急干预（24 月冷却）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Cd(EmergencyType.DivineRain, "甘霖"), GUILayout.Height(26)))
                TryEmergency(EmergencyType.DivineRain, "甘霖");
            if (GUILayout.Button(Cd(EmergencyType.DivineShield, "护盾"), GUILayout.Height(26)))
                TryEmergency(EmergencyType.DivineShield, "护盾");
            if (GUILayout.Button(Cd(EmergencyType.LifeSpring, "生命泉"), GUILayout.Height(26)))
                TryEmergency(EmergencyType.LifeSpring, "生命泉");
            GUILayout.EndHorizontal();

            if (_fx != null && !string.IsNullOrEmpty(_fx.LastFx))
                GUILayout.Label("因果链: " + _fx.LastFx);
            if (_input != null)
                GUILayout.Label("输入: " + _input.StatusMessage);
        }

        private void DrawEcology(TimeViewSnapshot snap)
        {
            GUILayout.Label("生态面板");
            GUILayout.Label($"粮储（表现投影） {snap.FoodReserve:0.###}");
            GUILayout.Label("灾害/前兆详见编年史中的生态条目。");
            DrawRecentByCategory(SimEventCategory.Ecology, SimEventCategory.Disaster);
        }

        private void DrawCivilization(TimeViewSnapshot snap)
        {
            GUILayout.Label("文明面板");
            GUILayout.Label($"人口（表现投影） {snap.Population:0}");
            GUILayout.Label($"时代指数 {snap.EraIndex}");
            DrawRecentByCategory(SimEventCategory.Civ, SimEventCategory.Era, SimEventCategory.Chronicle);
            var nodes = _generation.Nodes;
            if (nodes.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label("世代时间轴");
                int start = Mathf.Max(0, nodes.Count - 6);
                for (int i = start; i < nodes.Count; i++)
                    GUILayout.Label($"M{nodes[i].GameMonth} · {nodes[i].TemplateId} · ID {nodes[i].SourceId}");
            }
        }

        private void DrawThreat(TimeViewSnapshot snap)
        {
            GUILayout.Label("威胁面板");
            GUILayout.Label($"pending 干预 {snap.PendingCount}");
            DrawRecentByCategory(SimEventCategory.War, SimEventCategory.Disaster);
        }

        private void DrawChronicle()
        {
            GUILayout.Label($"编年史 · {_narrative.EntryCount} 条");
            var recent = _narrative.GetRecentEntries(16);
            if (recent.Count == 0)
            {
                GUILayout.Label("（尚无编年）");
                return;
            }
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var e = recent[i];
                GUILayout.Label($"{(e.IsComposite ? "◆" : "·")} M{e.GameMonth} {e.Title}");
                GUILayout.Label("    " + e.Body);
            }

            var actors = _narrative.GetTopNotableActors(4);
            if (actors.Count == 0) return;
            GUILayout.Space(6);
            GUILayout.Label("关键个体 / 政体");
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                GUILayout.Label($"#{a.SourceId} 分 {a.Score:0.#} · 事件 {a.EventCount} · M{a.LastMonth}");
            }
        }

        private void DrawRecentByCategory(params SimEventCategory[] cats)
        {
            var recent = _narrative.GetRecentEntries(24);
            int shown = 0;
            for (int i = recent.Count - 1; i >= 0 && shown < 8; i--)
            {
                var e = recent[i];
                if (!CategoryMatch(e.Category, cats)) continue;
                GUILayout.Label($"M{e.GameMonth} {e.Title}");
                shown++;
            }
            if (shown == 0)
                GUILayout.Label("（本类暂无叙事条目）");
        }

        private static bool CategoryMatch(SimEventCategory category, SimEventCategory[] cats)
        {
            for (int i = 0; i < cats.Length; i++)
                if (cats[i] == category) return true;
            return false;
        }

        private void Consume(IReadOnlyList<SimEvent> slice)
        {
            if (slice == null || ReferenceEquals(slice, _lastSlice)) return;
            _lastSlice = slice;
            _generation.Consume(slice);
            _narrative.Consume(slice);
        }

        private void TabButton(HudTab tab, string label)
        {
            string text = _tab == tab ? "▶" + label : label;
            if (GUILayout.Button(text, GUILayout.Height(24)))
                _tab = tab;
        }

        private string Cd(EmergencyType type, string name)
        {
            if (_interventions == null) return name;
            int cd = _interventions.GetEmergencyCooldownRemaining(type);
            return cd > 0 ? $"{name}({cd})" : name;
        }

        private void TryIntervene(string key, double delta, string ok)
        {
            if (_interventions == null || _timeSource == null) return;
            try
            {
                _interventions.ApplyIntervention(key, delta, 3, 1);
                _toast = ok + " → 生效月 " + (_timeSource.TimeSnapshot.MonthIndex + 1);
            }
            catch (System.Exception ex)
            {
                _toast = "干预失败: " + ex.Message;
            }
        }

        private void TryEmergency(EmergencyType type, string ok)
        {
            if (_interventions == null) return;
            try
            {
                _interventions.ApplyEmergency(type, 0);
                _toast = ok + " 已预约";
            }
            catch (System.Exception ex)
            {
                _toast = "紧急干预失败: " + ex.Message;
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
    }
}
