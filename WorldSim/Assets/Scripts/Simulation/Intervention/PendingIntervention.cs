namespace WorldSim.Simulation.Intervention
{
    /// <summary>pending 队列条目: 到期游戏月生效.</summary>
    public struct PendingIntervention
    {
        public int EffectiveMonth;
        public string Key;
        public double Delta;
        public int DurationMonths;
        public bool Applied;
        public int InterventionId;
    }
}
