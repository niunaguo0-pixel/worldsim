namespace WorldSim.Presentation
{
    using UnityEngine;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;
    using WorldSim.Simulation.Time;

    /// <summary>
    /// Unity 胶水 (P1 / 可玩月循环): Update 只传 dtReal; 挂载干预系统 + HUD + 可见沙盘.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] private ulong worldSeed = 42;
        [SerializeField] private bool enablePlayableHud = true;
        [SerializeField] private bool attachInterventionSystem = true;

        private WorldState _world;
        private SimOrchestrator _orchestrator;
        private InterventionSystem _interventions;

        public WorldState World => _world;
        public SimOrchestrator Orchestrator => _orchestrator;
        public InterventionSystem Interventions => _interventions;

        private void Awake()
        {
            _world = WorldState.CreateMinimalSlice(worldSeed, speedMultiplier: 1);
            _orchestrator = new SimOrchestrator(_world);

            if (attachInterventionSystem)
                _interventions = InterventionSystem.AttachToSlice(_world);

            if (attachInterventionSystem)
            {
                var fx = gameObject.GetComponent<InterventionFxBridge>();
                if (fx == null) fx = gameObject.AddComponent<InterventionFxBridge>();
                fx.Bind(_interventions);
            }

            if (enablePlayableHud)
            {
                var hud = gameObject.GetComponent<PlayableMonthLoopHud>();
                if (hud == null) hud = gameObject.AddComponent<PlayableMonthLoopHud>();
                hud.Bind(this, _interventions);
            }

            EnsureVisibleSandbox();
            FrameCamera();
        }

        private void Update()
        {
            if (_orchestrator == null) return;
            _orchestrator.Update(Time.deltaTime);
        }

        /// <summary>地面 + 聚落色块，避免「只有天空太阳」空场景。</summary>
        private static void EnsureVisibleSandbox()
        {
            if (GameObject.Find("WorldSim_Ground") == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
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

            if (GameObject.Find("Settlement_Alpha") == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Settlement_Alpha";
                go.transform.position = new Vector3(0f, 0.75f, 0f);
                go.transform.localScale = new Vector3(1.6f, 1.5f, 1.6f);
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = new Material(Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard"));
                    r.material.color = new Color(0.85f, 0.55f, 0.22f);
                }
            }

            if (GameObject.Find("Settlement_Alpha_Label") == null)
            {
                var label = new GameObject("Settlement_Alpha_Label");
                label.transform.position = new Vector3(0f, 2.2f, 0f);
                var tm = label.AddComponent<TextMesh>();
                tm.text = "聚落 Alpha";
                tm.fontSize = 32;
                tm.characterSize = 0.08f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
            }
        }

        private static void FrameCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(7f, 5.5f, -7f);
            cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            cam.clearFlags = CameraClearFlags.Skybox;
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
