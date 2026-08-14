namespace WorldSim.Presentation
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    using WorldSim.Simulation.Core;
    using WorldSim.Simulation.Intervention;

    /// <summary>
    /// UX §6.1 可玩输入路由：时间、干预模式、帮助；相机由 CameraLodController 自行轮询。
    /// 只经命令接口改时间/干预，不直接写 WorldState。
    /// </summary>
    public sealed class PlayableInputController : MonoBehaviour
    {
        private ITimePresentationSource _timeSource;
        private ITimeControlSink _timeControls;
        private IPlayableInterventionSink _interventions;
        private CameraLodController _cameraLod;
        private bool _interveneMode;
        private int _interveneIndex;
        private bool _showHelp = true;
        private string _status = "键鼠已启用：H 查看帮助";

        public bool InterveneMode => _interveneMode;
        public bool ShowHelp => _showHelp;
        public int InterveneIndex => _interveneIndex;
        public string StatusMessage => _status;
        public IntervenePreset CurrentPreset =>
            PlayableControlMap.IntervenePresets[
                PlayableControlMap.CycleInterveneIndex(_interveneIndex, 0, PlayableControlMap.IntervenePresets.Length)];

        public void Bind(
            ITimePresentationSource timeSource,
            ITimeControlSink timeControls,
            IPlayableInterventionSink interventions,
            CameraLodController cameraLod)
        {
            _timeSource = timeSource;
            _timeControls = timeControls;
            _interventions = interventions;
            _cameraLod = cameraLod;
            _interveneMode = false;
            _interveneIndex = 0;
            _showHelp = true;
            _status = "键鼠已启用：H 查看帮助";
        }

        private void Update()
        {
            if (_timeSource == null || _timeControls == null) return;
            PollKeyboard();
            PollMouseCancel();
        }

        private void PollKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var snapshot = _timeSource.TimeSnapshot;

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _timeControls.SetPaused(!snapshot.IsPaused);
                _status = snapshot.IsPaused ? "已继续" : "已暂停";
            }
            else if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            {
                _timeControls.SetSpeedMultiplier(1);
                _status = "速度 1×";
            }
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            {
                _timeControls.SetSpeedMultiplier(2);
                _status = "速度 2×";
            }
            else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame)
            {
                _timeControls.SetSpeedMultiplier(5);
                _status = "速度 5×";
            }
            else if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame ||
                     keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
            {
                _timeControls.SetSpeedMultiplier(20);
                _status = "速度 20×";
            }
            else if (keyboard.iKey.wasPressedThisFrame)
            {
                _interveneMode = !_interveneMode;
                _status = _interveneMode
                    ? "干预模式 ON · " + CurrentPreset.Label + " · Enter 施放"
                    : "干预模式 OFF";
            }
            else if (_interveneMode && (keyboard.eKey.wasPressedThisFrame || keyboard.rightBracketKey.wasPressedThisFrame))
            {
                _interveneIndex = PlayableControlMap.CycleInterveneIndex(
                    _interveneIndex, 1, PlayableControlMap.IntervenePresets.Length);
                _status = "干预 → " + CurrentPreset.Label;
            }
            else if (_interveneMode && (keyboard.qKey.wasPressedThisFrame || keyboard.leftBracketKey.wasPressedThisFrame))
            {
                _interveneIndex = PlayableControlMap.CycleInterveneIndex(
                    _interveneIndex, -1, PlayableControlMap.IntervenePresets.Length);
                _status = "干预 → " + CurrentPreset.Label;
            }
            else if (_interveneMode && keyboard.enterKey.wasPressedThisFrame)
            {
                ConfirmIntervene();
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelModes();
            }
            else if (keyboard.hKey.wasPressedThisFrame)
            {
                _showHelp = !_showHelp;
                _status = _showHelp ? "帮助已打开" : "帮助已关闭";
            }
        }

        private void PollMouseCancel()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame)
                CancelModes();
        }

        public void ConfirmIntervene()
        {
            if (_interventions == null || _timeSource == null)
            {
                _status = "干预系统未挂载";
                return;
            }

            var preset = CurrentPreset;
            try
            {
                if (preset.IsEmergency)
                {
                    var type = ParseEmergency(preset.Key);
                    _interventions.ApplyEmergency(type, delayMonths: 0);
                    _status = preset.Label + " 已预约（当月结算）";
                }
                else
                {
                    _interventions.ApplyIntervention(preset.Key, preset.Delta, durationMonths: 3, delayMonths: 1);
                    _status = preset.Label + $" → 生效月 {_timeSource.TimeSnapshot.MonthIndex + 1}";
                }
            }
            catch (System.Exception ex)
            {
                _status = "干预失败: " + ex.Message;
            }
        }

        public void CancelModes()
        {
            if (_interveneMode)
            {
                _interveneMode = false;
                _status = "已取消干预模式";
            }
        }

        /// <summary>供 HUD/测试直接选预设。</summary>
        public void SelectInterveneIndex(int index)
        {
            _interveneIndex = PlayableControlMap.CycleInterveneIndex(
                index, 0, PlayableControlMap.IntervenePresets.Length);
        }

        private static EmergencyType ParseEmergency(string key)
        {
            switch (key)
            {
                case "DivineShield": return EmergencyType.DivineShield;
                case "LifeSpring": return EmergencyType.LifeSpring;
                default: return EmergencyType.DivineRain;
            }
        }
    }
}
