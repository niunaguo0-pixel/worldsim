namespace WorldSim.Presentation
{
    using System;
    using System.IO;
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Core.Serialization;
    using WorldSim.Simulation.Intervention;
    using WorldSim.Simulation.Time;
    using WorldSim.Simulation.WorldMap;

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
            WorldMapViewSnapshot mapSnapshot;
            try
            {
                var mapConfig = new WorldInitConfig
                {
                    PresetKey = "fertile_crescent",
                    StartEra = StartEra.Modern,
                    StartRegionCenterLat = 33,
                    StartRegionCenterLon = 44,
                    StartRegionRadiusDeg = 8
                };
                string geoRoot = Path.Combine(Application.streamingAssetsPath, "Geo", "v1");
                WorldMapFactory.Build(geoRoot, mapConfig, _world);
                mapSnapshot = WorldMapViewSnapshot.Capture(_world.Geography, _world.Map.GeoDataBuild);
            }
            catch (Exception ex)
            {
                Debug.LogError("WorldSim geo bundle load failed: " + ex);
                mapSnapshot = new WorldMapViewSnapshot
                    { BundleAvailable = false, Error = ex.Message };
            }
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

            SandboxBindings sandbox = EnsureVisibleSandbox(mapSnapshot);
            DioramaLightingBootstrap.EnsureKeyLight();
            _cameraLod = gameObject.GetComponent<CameraLodController>();
            if (_cameraLod == null) _cameraLod = gameObject.AddComponent<CameraLodController>();
            _cameraLod.Bind(
                Camera.main,
                sandbox.Root.transform,
                sandbox.Settlement.GetComponent<Renderer>(),
                sandbox.SettlementLabel,
                sandbox.AggregateStatistics);

            var input = gameObject.GetComponent<PlayableInputController>();
            if (input == null) input = gameObject.AddComponent<PlayableInputController>();
            input.Bind(this, this, this, _cameraLod);

            if (enablePlayableHud)
            {
                var hud = gameObject.GetComponent<PlayableMonthLoopHud>();
                if (hud == null) hud = gameObject.AddComponent<PlayableMonthLoopHud>();
                hud.Bind(this, this, this, fx, _cameraLod, input);
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

        /// <summary>
        /// 生产存读档路径 (Task 4 Important 2): 反序列化快照后 Geography 为 null (transient),
        /// 必须显式重建才能让依赖系统 (如 CivilizationSimEngine 水邻增长) 正常工作。
        /// 此方法把 Load + WorldMapFactory.RebuildGeography 接线成一步, 供端到端存读档调用。
        /// 重建后重新挂载 SimOrchestrator 与 InterventionSystem, 保证运行时 settler 接回。
        /// Task 5 修复: 新 InterventionSystem 实例必须重新 Bind 给 InterventionFxBridge,
        /// 否则 PlayMode 存读档后 FX bridge 仍指向旧实例, 干预不再触发落点/渐变提示。
        /// </summary>
        public void LoadFromSnapshot(byte[] snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _world = WorldStateSerializer.Load(snapshot);
            string geoRoot = Path.Combine(Application.streamingAssetsPath, "Geo", "v1");
            // Important 2: RebuildGeography 从已持久化的 StaticChunks + Config 重读 Low 全量与
            // 起始区域 High, 重建只读 Geography, 防止依赖系统 NRE 或静默回退 (水邻增长被跳过)。
            if (_world.Map != null && _world.Map.StaticChunks != null && _world.Map.StaticChunks.Count > 0)
                WorldMapFactory.RebuildGeography(_world, geoRoot);
            _orchestrator = new SimOrchestrator(_world);
            if (attachInterventionSystem)
                _interventions = InterventionSystem.AttachToSlice(_world);
            // Task 5: 把 FX bridge 重新绑定到新 InterventionSystem 实例, 否则存读档后
            // _fx._sys 仍指向旧对象, CausalChain 增长不再被消费, 干预无视觉反馈。
            if (attachInterventionSystem)
            {
                var fx = gameObject.GetComponent<InterventionFxBridge>();
                if (fx == null) fx = gameObject.AddComponent<InterventionFxBridge>();
                fx.Bind(_interventions);
            }
            var input = gameObject.GetComponent<PlayableInputController>();
            if (input != null)
                input.Bind(this, this, this, _cameraLod);
            var hud = gameObject.GetComponent<PlayableMonthLoopHud>();
            if (hud != null)
                hud.Bind(this, this, this, gameObject.GetComponent<InterventionFxBridge>(), _cameraLod, input);
            RefreshTimeSnapshot();
        }

        private void RefreshTimeSnapshot()
        {
            if (_world == null) return;
            int pendingCount = _interventions != null ? _interventions.PendingCount : 0;
            _timeSnapshot = _timePresentation.Capture(_world, pendingCount);
        }

        /// <summary>真实地球低模 mesh + 聚落；仅 bundle 缺失时出现明确错误占位。</summary>
        private static SandboxBindings EnsureVisibleSandbox(WorldMapViewSnapshot mapSnapshot)
        {
            GameObject root = GameObject.Find("WorldSim_Sandbox");
            if (root == null)
                root = new GameObject("WorldSim_Sandbox");

            GameObject ground = GameObject.Find("WorldSim_RealEarthMap")
                ?? GameObject.Find("WorldSim_GeoBundleError");
            if (ground == null)
            {
                ground = WorldMapPresenter.Build(mapSnapshot);
                ground.transform.position = Vector3.zero;
            }
            ground.transform.SetParent(root.transform, true);

            GameObject settlement = GameObject.Find("Settlement_Alpha");
            if (settlement == null)
            {
                settlement = GameObject.CreatePrimitive(PrimitiveType.Cube);
                settlement.name = "Settlement_Alpha";
                float surfaceY = WorldMapPresenter.SphereRadius + 0.4f;
                settlement.transform.position = new Vector3(0f, surfaceY, 0f);
                settlement.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                var r = settlement.GetComponent<Renderer>();
                if (r != null)
                    r.sharedMaterial = NprMaterialFactory.CreateSettlementMaterial();
            }
            settlement.transform.SetParent(root.transform, true);

            GameObject settlementLabel = GameObject.Find("Settlement_Alpha_Label");
            if (settlementLabel == null)
            {
                settlementLabel = new GameObject("Settlement_Alpha_Label");
                settlementLabel.transform.position = new Vector3(0f, WorldMapPresenter.SphereRadius + 1.2f, 0f);
                var tm = settlementLabel.AddComponent<TextMesh>();
                tm.text = "聚落 Alpha";
                tm.fontSize = 32;
                tm.characterSize = 0.06f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
            }
            settlementLabel.transform.SetParent(root.transform, true);

            GameObject aggregateStatistics = GameObject.Find("WorldSim_AggregateStatistics");
            if (aggregateStatistics == null)
            {
                aggregateStatistics = new GameObject("WorldSim_AggregateStatistics");
                aggregateStatistics.transform.position = new Vector3(0f, WorldMapPresenter.SphereRadius + 2.5f, 0f);
                var tm = aggregateStatistics.AddComponent<TextMesh>();
                tm.text = "文明聚合统计";
                tm.fontSize = 28;
                tm.characterSize = 0.10f;
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
            if (UnityEngine.Object.FindAnyObjectByType<SimulationRunner>() != null) return;
#else
            if (UnityEngine.Object.FindObjectOfType<SimulationRunner>() != null) return;
#endif
            var go = new GameObject("WorldSim_PlayableLoop");
            go.AddComponent<SimulationRunner>();
        }
    }
}
