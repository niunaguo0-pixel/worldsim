namespace WorldSim.Simulation.Intervention
{
    /// <summary>紧急干预类型 (S1 §2.3) — 各 24 游戏月冷却.</summary>
    public enum EmergencyType : byte
    {
        DivineRain = 0,   // 天降甘霖
        DivineShield = 1, // 神佑护盾
        LifeSpring = 2,   // 生命之泉
    }
}
