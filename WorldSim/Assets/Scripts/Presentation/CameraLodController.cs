namespace WorldSim.Presentation
{
    using UnityEngine;

    /// <summary>处理沙盘相机缩放、平移与表现对象切换；不持有 WorldState。</summary>
    public sealed class CameraLodController : MonoBehaviour
    {
        private const float MinDistance = 3f;
        private const float MaxDistance = 28f;
        private const float InitialDistance = 10f;
        private const float ZoomStep = 1.5f;
        private const float PanExtent = 12f;
        private const float PositionSmoothTime = 0.12f;
        private static readonly Vector3 ViewDirection = new Vector3(0.64f, 0.5f, -0.64f).normalized;

        private Camera _camera;
        private Transform _sandboxRoot;
        private Renderer _entityDetailRenderer;
        private GameObject _settlementLabel;
        private GameObject _aggregateStatistics;
        private Vector3 _focus;
        private Vector3 _targetFocus;
        private Vector3 _focusVelocity;
        private float _distance = InitialDistance;
        private float _targetDistance = InitialDistance;
        private float _distanceVelocity;
        private bool _initialized;
        private CameraLodDecision _decision;

        public CameraLodLevel CurrentLod => _decision.Level;
        public string CurrentLodLabel => _decision.Label;
        public bool ReduceMotion => _decision.ReduceMotion;
        public float TargetDistance => _targetDistance;

        public void Bind(
            Camera targetCamera,
            Transform sandboxRoot,
            Renderer entityDetailRenderer,
            GameObject settlementLabel,
            GameObject aggregateStatistics)
        {
            _camera = targetCamera;
            _sandboxRoot = sandboxRoot;
            _entityDetailRenderer = entityDetailRenderer;
            _settlementLabel = settlementLabel;
            _aggregateStatistics = aggregateStatistics;
            _initialized = false;
            TryInitialize();
        }

        /// <summary>正值拉远、负值拉近，供输入层和测试直接驱动。</summary>
        public void Zoom(float steps)
        {
            _targetDistance = Mathf.Clamp(_targetDistance + steps * ZoomStep, MinDistance, MaxDistance);
            ApplyLod(CameraLodPolicy.Evaluate(_targetDistance));
        }

        /// <summary>以沙盘平面世界坐标平移焦点。</summary>
        public void Pan(Vector2 worldDelta)
        {
            _targetFocus.x = Mathf.Clamp(_targetFocus.x + worldDelta.x, -PanExtent, PanExtent);
            _targetFocus.z = Mathf.Clamp(_targetFocus.z + worldDelta.y, -PanExtent, PanExtent);
        }

        private void Update()
        {
            if (!TryInitialize()) return;

            _focus = Vector3.SmoothDamp(_focus, _targetFocus, ref _focusVelocity, PositionSmoothTime);
            _distance = Mathf.SmoothDamp(_distance, _targetDistance, ref _distanceVelocity, PositionSmoothTime);

            Vector3 desiredPosition = _focus + ViewDirection * _distance;
            _camera.transform.position = desiredPosition;
            _camera.transform.rotation = Quaternion.Slerp(
                _camera.transform.rotation,
                Quaternion.LookRotation(_focus - desiredPosition, Vector3.up),
                1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

            if (_camera.orthographic)
                _camera.orthographicSize = Mathf.Max(1.5f, _distance * 0.55f);
        }

        private void OnGUI()
        {
            if (!TryInitialize()) return;

            Event input = Event.current;
            if (input.type == EventType.ScrollWheel)
            {
                Zoom(input.delta.y);
                input.Use();
            }
            else if (input.type == EventType.MouseDrag && input.button == 2)
            {
                float worldPerPixel = Mathf.Max(0.002f, _targetDistance / Mathf.Max(1f, Screen.height));
                Vector3 right = _camera.transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
                Vector3 delta = (-right * input.delta.x - forward * input.delta.y) * worldPerPixel;
                Pan(new Vector2(delta.x, delta.z));
                input.Use();
            }
        }

        private bool TryInitialize()
        {
            if (_initialized && _camera != null) return true;
            if (_camera == null) _initialized = false;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;

            Vector3 rootPosition = _sandboxRoot != null ? _sandboxRoot.position : Vector3.zero;
            _focus = _targetFocus = rootPosition + Vector3.up * 0.5f;
            _distance = _targetDistance = InitialDistance;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.transform.position = _focus + ViewDirection * _distance;
            _camera.transform.LookAt(_focus);
            _initialized = true;
            ApplyLod(CameraLodPolicy.Evaluate(_targetDistance));
            return true;
        }

        private void ApplyLod(CameraLodDecision decision)
        {
            _decision = decision;
            if (_entityDetailRenderer != null)
                _entityDetailRenderer.enabled = decision.ShowEntityDetails;
            if (_settlementLabel != null)
                _settlementLabel.SetActive(decision.ShowSettlementLabel);
            if (_aggregateStatistics != null)
            {
                _aggregateStatistics.SetActive(decision.ShowAggregateStatistics);
                var text = _aggregateStatistics.GetComponent<TextMesh>();
                if (text != null)
                    text.text = decision.Level == CameraLodLevel.GenerationOverview
                        ? "世代概览 · 聚合统计"
                        : "文明聚合统计";
            }
        }
    }
}
