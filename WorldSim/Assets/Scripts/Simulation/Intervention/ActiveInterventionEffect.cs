namespace WorldSim.Simulation.Intervention
{
    /// <summary>运行时持续效果 (S1-2 pendingDelta / 持续月).</summary>
    public struct ActiveInterventionEffect
    {
        public string Key;
        public double AppliedDelta;
        public double DecayPerMonth;
        public int RemainingMonths;
        public int SourceMonth;
        public int InterventionId;
    }
}
