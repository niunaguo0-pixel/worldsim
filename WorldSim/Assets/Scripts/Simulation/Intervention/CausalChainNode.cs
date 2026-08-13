namespace WorldSim.Simulation.Intervention
{
    /// <summary>因果链节点 (S1-4) — 以游戏月锚定，供 S6/S8 呈现.</summary>
    public struct CausalChainNode
    {
        public int InterventionId;
        public int MonthExecuted;
        public string ActionKey;
        public string EventTemplateId;
        public double Magnitude;
    }
}
