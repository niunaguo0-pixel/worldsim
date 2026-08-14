namespace WorldSim.Presentation
{
    /// <summary>UX §6.1 Basic 档可玩动作（重映射前的默认语义）。</summary>
    public enum PlayableAction : byte
    {
        TogglePause = 0,
        Speed1x = 1,
        Speed2x = 2,
        Speed5x = 3,
        Speed20x = 4,
        ToggleInterveneMode = 5,
        CycleIntervenePrev = 6,
        CycleInterveneNext = 7,
        ConfirmIntervene = 8,
        Cancel = 9,
        ToggleHelp = 10,
        ResetCamera = 11
    }

    /// <summary>干预工具栏条目（键盘循环 + Enter 施放）。</summary>
    public readonly struct IntervenePreset
    {
        public IntervenePreset(string id, string label, string key, double delta, bool emergency)
        {
            Id = id;
            Label = label;
            Key = key;
            Delta = delta;
            IsEmergency = emergency;
        }

        public string Id { get; }
        public string Label { get; }
        public string Key { get; }
        public double Delta { get; }
        public bool IsEmergency { get; }
    }

    /// <summary>默认键位与干预预设表；纯数据，便于单测。</summary>
    public static class PlayableControlMap
    {
        public static readonly IntervenePreset[] IntervenePresets =
        {
            new IntervenePreset("rain", "降雨 +10", "rainfall_0", 10.0, false),
            new IntervenePreset("pop", "人口倾向", "population_1", 5.0, false),
            new IntervenePreset("agri", "农耕偏向", "devBias_agriculture_1", 0.2, false),
            new IntervenePreset("divine_rain", "天降甘霖", "DivineRain", 0, true),
            new IntervenePreset("divine_shield", "神佑护盾", "DivineShield", 0, true),
            new IntervenePreset("life_spring", "生命之泉", "LifeSpring", 0, true)
        };

        public static int CycleInterveneIndex(int current, int delta, int count)
        {
            if (count <= 0) return 0;
            int next = current + delta;
            while (next < 0) next += count;
            return next % count;
        }

        public static string HelpText()
        {
            return
                "【相机】滚轮缩放 · 左键/中键拖拽平移 · WASD/方向键平移 · R 复位视角\n" +
                "【时间】Space 暂停 · 1/2/5/0(或4) 设速\n" +
                "【干预】I 开关模式 · Q/E 或 [/] 切换类型 · Enter 施放 · Esc/右键 取消\n" +
                "【帮助】H 开关本说明";
        }
    }
}
