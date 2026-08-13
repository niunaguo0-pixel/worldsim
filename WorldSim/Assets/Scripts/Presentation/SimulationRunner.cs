namespace WorldSim.Presentation
{
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;
    using WorldSim.Simulation.Time;

    /// <summary>
    /// Unity 胶水 (P1 / 可玩月循环): Update 只传 dtReal; 挂载干预系统 + HUD + 可见沙盘.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour, ITimePresentationSource, ITimeControlSink,
        IPlayableInterventionSink
    {
        [SerializeField] private ulong worldSeed = 42;
        [SerializeField] private bool enablePlayableHud = true;
        [SerializeField] private bool attachInterventionSystem = true;

        private WorldState _world;
        private SimOrchestrator _orchestrator;
        private InterventionSystem _interventions;
        private readonly TimePresentationModel _timePresentation = new TimePresentationModel();
        private TimeViewSnapshot _timeSnapshot;
        private CameraLodController _cameraLod;

        public WorldState World => _world;
        public SimOrchestrator Orchestrator => _orchestrator;
        public InterventionSystem Interventions => _interventions;
        public TimeViewSnapshot TimeSnapshot => _timeSnapshot;
        public CameraLodController CameraLod => _cameraLod;

        private void Awake()
        {
            _world = WorldState.CreateMinimalSlice(worldSeed, speedMultiplier: 1);
            _orchestrator = new SimOrchestrator(_world);

            if (attachInterventionSystem)
                _interventions = InterventionSystem.AttachToSlice(_world);

            RefreshTimeSnapshot();

            InterventionFxBridge fx = null;
            if (attachInterventionSystem)
            {
                fx = gameObject.GetComponent<InterventionFxBridge>();
                if (fx == null) fx = gameObject.AddComponent<InterventionFxBridge>();
                fx.Bind(_interventions);
            }

            SandboxBindings sandbox = EnsureVisibleSandbox();
            _cameraLod = gameObject.GetComponent<CameraLodController>();
            if (_cameraLod == null) _cameraLod = gameObject.AddComponent<CameraLodController>();
            _cameraLod.Bind(
                Camera.main,
                sandbox.Root.transform,
                sandbox.Settlement.GetComponent<Renderer>(),
                sandbox.SettlementLabel,
                sandbox.AggregateStatistics);

            if (enablePlayableHud)
            {
                var hud = gameObject.GetComponent<PlayableMonthLoopHud>();
                if (hud == null) hud = gameObject.AddComponent<PlayableMonthLoopHud>();
                hud.Bind(this, this, this, fx, _cameraLod);
            }
        }

        private void Update()
        {
            if (_orchestrator == null) return;
            _orchestrator.Update(Time.deltaTime);
            RefreshTimeSnapshot();
        }

        public void SetPaused(bool paused)
        {
            if (_orchestrator == null) return;
            _orchestrator.SetPaused(paused);
            RefreshTimeSnapshot();
        }

        public void SetSpeedMultiplier(int speedMultiplier)
        {
            if (_orchestrator == null) return;
            _orchestrator.SetSpeedMultiplier(speedMultiplier);
            RefreshTimeSnapshot();
        }

        public int GetEmergencyCooldownRemaining(EmergencyType type)
        {
            return _interventions != null
                ? _interventions.GetEmergencyCooldownRemaining(type)
                : 0;
        }

        public void ApplyIntervention(string key, double delta, int durationMonths, int delayMonths)
        {
            if (_interventions == null || _world == null) return;
            _interventions.ApplyIntervention(key, delta, durationMonths, delayMonths, _world);
            RefreshTimeSnapshot();
        }

        public void ApplyEmergency(EmergencyType type, int delayMonths)
        {
            if (_interventions == null || _world == null) return;
            _interventions.ApplyEmergency(type, _world, delayMonths);
            RefreshTimeSnapshot();
        }

        private void RefreshTimeSnapshot()
        {
            if (_world == null) return;
            int pendingCount = _interventions != null ? _interventions.PendingCount : 0;
            _timeSnapshot = _timePresentation.Capture(_world, pendingCount);
        }

        /// <summary>地面 + 聚落色块，避免「只有天空太阳」空场景。</summary>
        private static SandboxBindings EnsureVisibleSandbox()
        {
            GameObject root = GameObject.Find("WorldSim_Sandbox");
            if (root == null)
                root = new GameObject("WorldSim_Sandbox");

            GameObject ground = GameObject.Find("WorldSim_Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "WorldSim_Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
                var gr = ground.GetComponent<Renderer>();
                if (gr != null)
                {
                    gr.material = new Material(Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard"));
                    gr.material.color = new Color(0.35f, 0.55f, 0.28f);
                }
            }
            ground.transform.SetParent(root.transform, true);

            GameObject settlement = GameObject.Find("Settlement_Alpha");
            if (settlement == null)
            {
                settlement = GameObject.CreatePrimitive(PrimitiveType.Cube);
                settlement.name = "Settlement_Alpha";
                settlement.transform.position = new Vector3(0f, 0.75f, 0f);
                settlement.transform.localScale = new Vector3(1.6f, 1.5f, 1.6f);
                var r = settlement.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard"));
                    r.material.color = new Color(0.85f, 0.55f, 0.22f);
                }
            }
            settlement.transform.SetParent(root.transform, true);

            GameObject settlementLabel = GameObject.Find("Settlement_Alpha_Label");
            if (settlementLabel == null)
            {
                settlementLabel = new GameObject("Settlement_Alpha_Label");
                settlementLabel.transform.position = new Vector3(0f, 2.2f, 0f);
                var tm = settlementLabel.AddComponent<TextMesh>();
                tm.text = "聚落 Alpha";
                tm.fontSize = 32;
                tm.characterSize = 0.08f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
            }
            settlementLabel.transform.SetParent(root.transform, true);

            GameObject aggregateStatistics = GameObject.Find("WorldSim_AggregateStatistics");
            if (aggregateStatistics == null)
            {
                aggregateStatistics = new GameObject("WorldSim_AggregateStatistics");
                aggregateStatistics.transform.position = new Vector3(0f, 3.2f, 0f);
                var tm = aggregateStatistics.AddComponent<TextMesh>();
                tm.text = "文明聚合统计";
                tm.fontSize = 32;
                tm.characterSize = 0.12f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.85f, 0.95f, 1f);
            }
            aggregateStatistics.transform.SetParent(root.transform, true);

            return new SandboxBindings(root, settlement, settlementLabel, aggregateStatistics);
        }

        private sealed class SandboxBindings
        {
            public SandboxBindings(
                GameObject root,
                GameObject settlement,
                GameObject settlementLabel,
                GameObject aggregateStatistics)
            {
                Root = root;
                Settlement = settlement;
                SettlementLabel = settlementLabel;
                AggregateStatistics = aggregateStatistics;
            }

            public GameObject Root { get; }
            public GameObject Settlement { get; }
            public GameObject SettlementLabel { get; }
            public GameObject AggregateStatistics { get; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapIfMissing()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<SimulationRunner>() != null) return;
#else
            if (Object.FindObjectOfType<SimulationRunner>() != null) return;
#endif
            var go = new GameObject("WorldSim_PlayableLoop");
            go.AddComponent<SimulationRunner>();
        }
    }
}
