namespace WorldSim.Presentation
{
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Time;

    /// <summary>
    /// Unity 胶水 (V0-1/V0-3): 仅从 Update 取真实帧时间交给 SimOrchestrator.
    /// 零逻辑计算在 MonoBehaviour 内; 确定性计算全在 WorldSim.Simulation.* (架构 §2.2).
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] private ulong worldSeed = 42;

        private WorldState _world;
        private SimOrchestrator _orchestrator;

        public WorldState World => _world;
        public SimOrchestrator Orchestrator => _orchestrator;

        private void Awake()
        {
            _world = WorldState.CreateMinimalSlice(worldSeed, speedMultiplier: 1);
            _orchestrator = new SimOrchestrator(_world);
        }

        private void Update()
        {
            if (_orchestrator == null) return;
            _orchestrator.Update(Time.deltaTime);
        }
    }
}
