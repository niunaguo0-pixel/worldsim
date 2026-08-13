namespace WorldSim.Simulation.Core
{
    /// <summary>S1→S2 的只读参数桥，避免 Ecology 依赖 Intervention 具体实现。</summary>
    public interface IInterventionParameterSource
    {
        bool TryGetParameterValue(string key, out double value);
    }
}
