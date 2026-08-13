namespace WorldSim.Simulation.Intervention
{
    using System;

    /// <summary>可干预参数定义 (S1-1). 红线派生态不得注册.</summary>
    public readonly struct InterventionParamDef
    {
        public readonly string Key;
        public readonly double DefaultValue;
        public readonly double Min;
        public readonly double Max;

        public InterventionParamDef(string key, double defaultValue, double min, double max)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            DefaultValue = defaultValue;
            Min = min;
            Max = max;
        }
    }

    /// <summary>干预唯一写入目标 (架构 §2.7 / S1).</summary>
    public interface IInterventionTarget
    {
        bool CanRegister(string key);
        void RegisterInterventionParameter(string key, double defaultValue, double min, double max);
        void ApplyIntervention(string key, double delta, int durationMonths);
        double GetParameterValue(string key);
    }
}
