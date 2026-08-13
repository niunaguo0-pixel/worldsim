namespace WorldSim.Simulation.Core
{
    /// <summary>
    /// 月级干预结算钩子 (S1). Intervention 程序集实现.
    /// </summary>
    public interface IMonthlyInterventionSettler
    {
        void SettleDue(WorldState world, int month);

        /// <summary>神佑护盾等: 吸收一次灾害则返回 true（并消耗护盾）.</summary>
        bool TryAbsorbDisaster(WorldState world, int settlementStableId, int month);
    }
}
