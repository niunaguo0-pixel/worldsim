namespace WorldSim.Presentation
{
    using System.Text;
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;
    using WorldSim.Simulation.Time;

    /// <summary>
    /// 可玩月循环 HUD (Sprint 2 切片): 暂停/变速 + 时间轴 + 三种干预 + 事件尾迹.
    /// UI 只驱动 pause/speed/ApplyIntervention, 不持有 WorldState (架构 §2.7).
    /// </summary>
    public sealed class PlayableMonthLoopHud : MonoBehaviour
    {
        private SimulationRunner _runner;
        private InterventionSystem _interventions;
        private Vector2 _eventScroll;
        private string _toast = "可玩月循环：暂停/变速后点干预，过月看粮储与事件。";

        public void Bind(SimulationRunner runner, InterventionSystem interventions)
        {
            _runner = runner;
            _interventions = interventions;
        }

        private void OnGUI()
        {
            if (_runner == null || _runner.World == null || _runner.Orchestrator == null) return;
            var world = _runner.World;
            var orch = _runner.Orchestrator;
            int month = world.Time.monthIndex;
            int year = month / 12 + 1;
            int season = (month % 12) / 3; // 0春1夏2秋3冬
            string seasonName = season == 0 ? "春" : season == 1 ? "夏" : season == 2 ? "秋" : "冬";

            const float pad = 12f;
            GUILayout.BeginArea(new Rect(pad, pad, 420f, Screen.height - pad * 2));
            GUILayout.BeginVertical("box");

            GUILayout.Label($"【请看 Game 窗口】WorldSim · 可玩月循环");
            GUILayout.Label($"纪年 第{year}年 · {seasonName} · 月序 {month}  |  时代 {world.EraIndex}");
            GUILayout.Label($"速度 {world.Time.speedMultiplier}×  |  {(world.Time.paused ? "已暂停" : "推进中")}");

            double pop = world.Settlements.Count > 0 ? world.Settlements[0].population : 0;
            double food = 0;
            for (int i = 0; i < world.Resources.Count; i++)
                if (world.Resources[i].name == "Food") food = world.Resources[i].currentAmount;
            GUILayout.Label($"聚落 Alpha 人口 {pop:0}  |  粮储 {food:0.###}  |  pending {_interventions?.PendingCount ?? 0}");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(world.Time.paused ? "继续" : "暂停", GUILayout.Height(28)))
                orch.SetPaused(!world.Time.paused);
            if (GUILayout.Button("1×", GUILayout.Width(40), GUILayout.Height(28))) orch.SetSpeedMultiplier(1);
            if (GUILayout.Button("2×", GUILayout.Width(40), GUILayout.Height(28))) orch.SetSpeedMultiplier(2);
            if (GUILayout.Button("5×", GUILayout.Width(40), GUILayout.Height(28))) orch.SetSpeedMultiplier(5);
            if (GUILayout.Button("20×", GUILayout.Width(48), GUILayout.Height(28))) orch.SetSpeedMultiplier(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("干预（延迟 1 游戏月生效）");
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

            var fx = _runner.GetComponent<InterventionFxBridge>();
            if (fx != null && !string.IsNullOrEmpty(fx.LastFx))
                GUILayout.Label("因果链: " + fx.LastFx);

            GUILayout.Space(4);
            GUILayout.Label(_toast);

            GUILayout.Space(6);
            GUILayout.Label("近期事件");
            _eventScroll = GUILayout.BeginScrollView(_eventScroll, GUILayout.Height(180));
            var sb = new StringBuilder();
            int start = System.Math.Max(0, world.Events.Count - 24);
            for (int i = start; i < world.Events.Count; i++)
            {
                var e = world.Events[i];
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
            if (_interventions == null || _runner?.World == null) return;
            try
            {
                _interventions.ApplyIntervention(key, delta, durationMonths: 3, delayMonths: 1, world: _runner.World);
                _toast = okMsg + $" → 生效月 {_runner.World.Time.monthIndex + 1}";
            }
            catch (System.Exception ex)
            {
                _toast = "干预失败: " + ex.Message;
            }
        }

        private void TryEmergency(EmergencyType type, string okMsg)
        {
            if (_interventions == null || _runner?.World == null) return;
            try
            {
                _interventions.ApplyEmergency(type, _runner.World, delayMonths: 0);
                _toast = okMsg + " 已预约（当月结算生效）";
            }
            catch (System.Exception ex)
            {
                _toast = "紧急干预失败: " + ex.Message;
            }
        }
    }
}
