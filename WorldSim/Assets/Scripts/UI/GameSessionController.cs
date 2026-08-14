namespace WorldSim.UI
{
    using System.IO;
    using UnityEngine;
    using WorldSim.Presentation;
    using WorldSim.Simulation.WorldMap;

    /// <summary>
    /// S8 会话壳：先 New Game，确认后再 StartWorld + 正式 HUD。
    /// </summary>
    public sealed class GameSessionController : MonoBehaviour
    {
        private SimulationRunner _runner;
        private NewGameScreen _newGame;
        private FormalGameHud _formalHud;
        private GoalMode _goalMode = GoalMode.SandboxNoVictory;

        public GoalMode GoalMode => _goalMode;
        public bool HasStarted => _runner != null && _runner.HasStarted;

        public void BeginNewGameFlow(SimulationRunner runner)
        {
            _runner = runner;
            string presets = Path.Combine(Application.streamingAssetsPath, "Data", "region-presets.json");
            _newGame = gameObject.GetComponent<NewGameScreen>();
            if (_newGame == null) _newGame = gameObject.AddComponent<NewGameScreen>();
            _newGame.Bind(presets, OnNewGameConfirmed);

            _formalHud = gameObject.GetComponent<FormalGameHud>();
            if (_formalHud == null) _formalHud = gameObject.AddComponent<FormalGameHud>();
            _formalHud.enabled = false;
        }

        private void OnNewGameConfirmed(NewGameDraft draft, WorldInitConfig config)
        {
            _goalMode = draft.GoalMode;
            if (_runner == null) return;

            _runner.StartWorld(config, draft.WorldSeed, useFormalHud: true);

            var fx = _runner.GetComponent<InterventionFxBridge>();
            var input = _runner.GetComponent<PlayableInputController>();
            string summary = NewGameAssembler.DescribeMode(config) +
                             " · 预设 " + (config.PresetKey ?? "") +
                             " · seed " + draft.WorldSeed;

            _formalHud.Bind(
                _runner,
                _runner,
                _runner,
                fx,
                _runner.CameraLod,
                input,
                _goalMode,
                summary);
            _formalHud.enabled = true;

            var legacy = _runner.GetComponent<PlayableMonthLoopHud>();
            if (legacy != null)
                legacy.enabled = false;
        }
    }
}
