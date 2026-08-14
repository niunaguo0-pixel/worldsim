namespace WorldSim.Presentation
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// AS-2 最小切片：Screen Space Overlay（后处理之后）旱灾/前兆标记。
    /// 暖白底板 + 深褐描边 + 图标/文字冗余（AX-2）；不写 WorldState。
    /// </summary>
    public sealed class As2HazardOverlay : MonoBehaviour
    {
        public const string RootName = "WorldSim_AS2_HazardOverlay";
        public const string DefaultLabel = "旱灾前兆";

        private static readonly Color Plate = new Color(0xF5 / 255f, 0xF0 / 255f, 0xE8 / 255f, 1f);
        private static readonly Color Stroke = new Color(0x3A / 255f, 0x2A / 255f, 0x1A / 255f, 1f);
        private static readonly Color Hazard = new Color(0xC0 / 255f, 0x39 / 255f, 0x2B / 255f, 1f);

        private Canvas _canvas;
        private RectTransform _card;
        private Text _label;
        private Image _plate;
        private Outline _outline;
        private bool _visible;
        private Transform _worldAnchor;
        private Camera _camera;

        public bool IsVisible => _visible;
        public string CurrentLabel => _label != null ? _label.text : "";

        public static As2HazardOverlay EnsureOn(GameObject host)
        {
            if (host == null) throw new System.ArgumentNullException(nameof(host));
            var existing = host.GetComponent<As2HazardOverlay>();
            if (existing != null)
            {
                existing.EnsureUi();
                return existing;
            }

            var c = host.AddComponent<As2HazardOverlay>();
            c.EnsureUi();
            return c;
        }

        public void Bind(Camera camera, Transform worldAnchor)
        {
            _camera = camera;
            _worldAnchor = worldAnchor;
            EnsureUi();
        }

        public void EnsureUi()
        {
            if (_canvas != null) return;

            var root = GameObject.Find(RootName);
            if (root == null)
                root = new GameObject(RootName);

            _canvas = root.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            if (root.GetComponent<CanvasScaler>() == null)
            {
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }

            if (root.GetComponent<GraphicRaycaster>() == null)
                root.AddComponent<GraphicRaycaster>();

            var cardGo = root.transform.Find("Card");
            if (cardGo == null)
            {
                cardGo = new GameObject("Card", typeof(RectTransform)).transform;
                cardGo.SetParent(root.transform, false);
            }

            _card = cardGo.GetComponent<RectTransform>();
            _card.sizeDelta = new Vector2(220, 56);
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);

            _plate = cardGo.GetComponent<Image>();
            if (_plate == null)
                _plate = cardGo.gameObject.AddComponent<Image>();
            _plate.color = Plate;
            _plate.raycastTarget = false;

            _outline = cardGo.GetComponent<Outline>();
            if (_outline == null)
                _outline = cardGo.gameObject.AddComponent<Outline>();
            _outline.effectColor = Stroke;
            // ~2px 屏幕等效描边（双极兜底的深褐极）
            _outline.effectDistance = new Vector2(2.5f, -2.5f);
            _outline.useGraphicAlpha = true;

            var textTf = cardGo.Find("Label");
            if (textTf == null)
            {
                var textGo = new GameObject("Label", typeof(RectTransform));
                textTf = textGo.transform;
                textTf.SetParent(cardGo, false);
            }

            var textRt = textTf.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10, 6);
            textRt.offsetMax = new Vector2(-10, -6);

            _label = textTf.GetComponent<Text>();
            if (_label == null)
                _label = textTf.gameObject.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_label.font == null)
                _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.fontSize = 22;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = Hazard;
            _label.text = "⚠ " + DefaultLabel;
            _label.raycastTarget = false;

            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            EnsureUi();
            _visible = visible;
            if (_card != null)
                _card.gameObject.SetActive(visible);
        }

        public void SetLabel(string text)
        {
            EnsureUi();
            if (_label != null)
                _label.text = string.IsNullOrEmpty(text) ? ("⚠ " + DefaultLabel) : text;
        }

        private void LateUpdate()
        {
            if (!_visible || _card == null) return;
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null || _worldAnchor == null) return;

            Vector3 screen = _camera.WorldToScreenPoint(_worldAnchor.position + Vector3.up * 0.8f);
            if (screen.z < 0f)
            {
                _card.gameObject.SetActive(false);
                return;
            }

            _card.gameObject.SetActive(true);
            _card.position = screen + new Vector3(0f, 48f, 0f);
        }
    }
}
