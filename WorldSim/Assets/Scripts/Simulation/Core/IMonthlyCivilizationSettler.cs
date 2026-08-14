namespace WorldSim.Simulation.Core
{
    /// <summary>S3 月结钩子，Time 通过 Core 接口保持与 Civilization 程序集解耦。</summary>
    public interface IMonthlyCivilizationSettler
    {
        void SettleMonth(WorldState world, int month);
    }
}
