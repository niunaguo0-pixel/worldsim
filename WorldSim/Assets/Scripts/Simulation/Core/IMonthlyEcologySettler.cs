namespace WorldSim.Simulation.Core
{
    /// <summary>S2 月结钩子；Time 只依赖 Core，正式逻辑由 Ecology 程序集实现。</summary>
    public interface IMonthlyEcologySettler
    {
        void SettleMonth(WorldState world, int month);
    }
}
