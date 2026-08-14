namespace WorldSim.UI
{
    using System.IO;
    using UnityEngine;
    using WorldSim.Simulation.WorldMap;

    /// <summary>S8 New Game 设置面板（IMGUI 正式壳；地理 4 + 目标 1）。</summary>
    public sealed class NewGameScreen : MonoBehaviour
    {
        private NewGameDraft _draft = NewGameDraft.CreateDefaults();
        private RegionPresetCatalog _catalog;
        private string _status = "选择开局参数后开始。";
        private string _presetsPath;
        private System.Action<NewGameDraft, WorldInitConfig> _onConfirm;

        public NewGameDraft Draft => _draft;
        public bool IsVisible { get; private set; } = true;

        public void Bind(string presetsPath, System.Action<NewGameDraft, WorldInitConfig> onConfirm)
        {
            _presetsPath = presetsPath;
            _onConfirm = onConfirm;
            _draft = NewGameDraft.CreateDefaults();
            IsVisible = true;
            TryLoadCatalog();
        }

        private void TryLoadCatalog()
        {
            try
            {
                string path = _presetsPath;
                if (string.IsNullOrEmpty(path))
                    path = Path.Combine(Application.streamingAssetsPath, "Data", "region-presets.json");
                _catalog = RegionPresetLoader.LoadFromFile(path);
                _status = "已加载 " + _catalog.Presets.Count + " 个起始区域预设。";
            }
            catch (System.Exception ex)
            {
                _catalog = null;
                _status = "预设加载失败: " + ex.Message;
            }
        }

        private void OnGUI()
        {
            if (!IsVisible) return;

            float w = Mathf.Min(520f, Screen.width - 40f);
            float h = Mathf.Min(560f, Screen.height - 40f);
            Rect area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUILayout.BeginArea(area, "WorldSim · New Game", GUI.skin.window);
            GUILayout.Label("开局设置（地理 4 项 + 目标 1 项）");

            GUILayout.Space(6);
            GUILayout.Label("① 起始时代");
            GUILayout.BeginHorizontal();
            EraButton(StartEra.Primordial, "远古沙盒");
            EraButton(StartEra.EarlyModern, "近代");
            EraButton(StartEra.Modern, "现代");
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("② 起始区域预设（可改，不锁定）");
            if (_catalog != null)
            {
                for (int i = 0; i < _catalog.Presets.Count; i++)
                {
                    RegionPreset p = _catalog.Presets[i];
                    string label = (_draft.PresetKey == p.Key ? "▶ " : "  ") + p.Name;
                    if (GUILayout.Button(label, GUILayout.Height(24)))
                        _draft.PresetKey = p.Key;
                }
            }
            else
            {
                GUILayout.Label("（无预设）");
            }

            GUILayout.Space(4);
            GUILayout.Label("③ 国界年份（远古沙盒时忽略）");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < NewGameAssembler.SuggestedBorderYears.Length; i++)
            {
                int y = NewGameAssembler.SuggestedBorderYears[i];
                string label = _draft.BorderYear == y ? $"▶{y}" : y.ToString();
                if (GUILayout.Button(label, GUILayout.Height(24)))
                    _draft.BorderYear = y;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("④ 法律传统偏好（仅地缘模式）");
            _draft.UsePresetLegalBias = GUILayout.Toggle(_draft.UsePresetLegalBias, "由起始区域自动映射（推荐）");
            if (!_draft.UsePresetLegalBias)
            {
                GUILayout.BeginHorizontal();
                LegalButton(LegalFamilyBias.CivilLaw, "大陆法");
                LegalButton(LegalFamilyBias.CommonLaw, "英美法");
                LegalButton(LegalFamilyBias.SocialistLaw, "社会主义法");
                LegalButton(LegalFamilyBias.CustomaryLaw, "习惯法");
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.Label("⑤ 目标模式（不进地理配置）");
            GUILayout.BeginHorizontal();
            GoalButton(GoalMode.SandboxNoVictory, "沙盒");
            GoalButton(GoalMode.MilestonePolity, "里程碑");
            GoalButton(GoalMode.Custom, "自定义");
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label(_status);
            if (GUILayout.Button("开始世界", GUILayout.Height(36)))
                Confirm();

            GUILayout.EndArea();
        }

        private void Confirm()
        {
            if (_catalog == null)
            {
                TryLoadCatalog();
                if (_catalog == null)
                {
                    _status = "无法开始：region-presets 未加载。";
                    return;
                }
            }

            try
            {
                WorldInitConfig cfg = NewGameAssembler.Assemble(_draft, _catalog);
                _status = NewGameAssembler.DescribeMode(cfg) + " · " +
                          NewGameAssembler.GoalModeLabel(_draft.GoalMode);
                IsVisible = false;
                _onConfirm?.Invoke(_draft, cfg);
            }
            catch (System.Exception ex)
            {
                _status = "装配失败: " + ex.Message;
            }
        }

        private void EraButton(StartEra era, string label)
        {
            string text = _draft.StartEra == era ? "▶" + label : label;
            if (GUILayout.Button(text, GUILayout.Height(26)))
                _draft.StartEra = era;
        }

        private void LegalButton(LegalFamilyBias bias, string label)
        {
            string text = _draft.LegalBiasOverride == bias ? "▶" + label : label;
            if (GUILayout.Button(text, GUILayout.Height(24)))
                _draft.LegalBiasOverride = bias;
        }

        private void GoalButton(GoalMode mode, string label)
        {
            string text = _draft.GoalMode == mode ? "▶" + label : label;
            if (GUILayout.Button(text, GUILayout.Height(24)))
                _draft.GoalMode = mode;
        }
    }
}
