namespace WorldSim.Presentation
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// 沙盘相机：完整键鼠控制（Input System）。
    /// 滚轮缩放；WASD/方向键与左/中键拖拽平移；R 复位。不持有 WorldState。
    /// </summary>
    public sealed class CameraLodController : MonoBehaviour
    {
        private const float MinDistance = 6f;
        private const float MaxDistance = 30f;
        private const float InitialDistance = 14f;
        private const float ZoomStep = 2.8f;
        private const float PanExtent = 12f;
        private const float KeyboardPanSpeed = 6f;
        private const float PositionSmoothTime = 0.12f;
        private const float ZoomSmoothTime = 0.04f;
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
        private CameraLodLevel _lodLevel = CameraLodLevel.Civilization;
        private Vector2 _lastPointer;
        private float _userDriveUntilUnscaled;
        private bool _dragging;

        public CameraLodLevel CurrentLod => _decision.Level;
        public string CurrentLodLabel => _decision.Label;
        public bool ReduceMotion => _decision.ReduceMotion;
        public int MeshLonSegments => _decision.MeshLonSegments;
        public int MeshLatSegments => _decision.MeshLatSegments;
        public float TargetDistance => _targetDistance;
        public Vector3 TargetFocus => _targetFocus;
        /// <summary>用户正在/刚操作过相机时为 true；表现层提示应让路。</summary>
        public bool IsUserDrivingCamera => Time.unscaledTime < _userDriveUntilUnscaled;

        /// <summary>
        /// P3：把表现层相机提示柔和混入目标焦点/距离（不读不写 WorldState）。
        /// 用户操作期间忽略，避免盖掉键鼠控制。
        /// </summary>
        public void ApplyPresentationCameraHint(Vector3 focusHint, float distanceHint, float blend)
        {
            if (IsUserDrivingCamera) return;
            blend = Mathf.Clamp01(blend);
            if (blend <= 0f) return;
            _targetFocus = Vector3.Lerp(_targetFocus, focusHint, blend);
            _targetDistance = Mathf.Lerp(
                _targetDistance,
                Mathf.Clamp(distanceHint, MinDistance, MaxDistance),
                blend);
            ApplyLod(CameraLodPolicy.EvaluateWithHysteresis(_targetDistance, _lodLevel));
        }

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
            MarkUserCameraInput();
            _targetDistance = Mathf.Clamp(_targetDistance + steps * ZoomStep, MinDistance, MaxDistance);
            // 滚轮缩放跟手：立刻贴近目标，避免「慢慢放到」
            _distance = Mathf.Lerp(_distance, _targetDistance, 0.85f);
            _distanceVelocity = 0f;
            ApplyLod(CameraLodPolicy.EvaluateWithHysteresis(_targetDistance, _lodLevel));
        }

        /// <summary>以沙盘平面世界坐标平移焦点。</summary>
        public void Pan(Vector2 worldDelta)
        {
            MarkUserCameraInput();
            _targetFocus.x = Mathf.Clamp(_targetFocus.x + worldDelta.x, -PanExtent, PanExtent);
            _targetFocus.z = Mathf.Clamp(_targetFocus.z + worldDelta.y, -PanExtent, PanExtent);
        }

        public void ResetView()
        {
            MarkUserCameraInput();
            Vector3 rootPosition = _sandboxRoot != null ? _sandboxRoot.position : Vector3.zero;
            _targetFocus = rootPosition + Vector3.up * 0.5f;
            _targetDistance = InitialDistance;
            ApplyLod(CameraLodPolicy.EvaluateWithHysteresis(_targetDistance, _lodLevel));
        }

        private void Update()
        {
            if (!TryInitialize()) return;
            PollInput(Time.unscaledDeltaTime);

            _focus = Vector3.SmoothDamp(_focus, _targetFocus, ref _focusVelocity, PositionSmoothTime);
            float distanceSmooth = IsUserDrivingCamera ? ZoomSmoothTime : PositionSmoothTime;
            _distance = Mathf.SmoothDamp(_distance, _targetDistance, ref _distanceVelocity, distanceSmooth);

            Vector3 desiredPosition = _focus + ViewDirection * _distance;
            _camera.transform.position = desiredPosition;
            _camera.transform.rotation = Quaternion.Slerp(
                _camera.transform.rotation,
                Quaternion.LookRotation(_focus - desiredPosition, Vector3.up),
                1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

            if (_camera.orthographic)
                _camera.orthographicSize = Mathf.Max(1.5f, _distance * 0.55f);

            var earth = _sandboxRoot != null
                ? _sandboxRoot.GetComponentInChildren<WorldMapPresenter>()
                : null;
            if (earth != null)
            {
                earth.CameraDistance = _distance;
                // 平滑距离跨过迟滞边界时同步 mesh 精度
                ApplyLod(CameraLodPolicy.EvaluateWithHysteresis(_distance, _lodLevel));
            }
        }

        private void PollInput(float dt)
        {
            PollKeyboard(dt);
            PollMouse(dt);
        }

        private void PollKeyboard(float dt)
        {
            Vector2 keyPan = Vector2.zero;
            bool reset = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) keyPan.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) keyPan.y -= 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) keyPan.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) keyPan.x += 1f;
                if (keyboard.rKey.wasPressedThisFrame) reset = true;
            }
            else
            {
                // Active Input Handling = Both 时的旧输入兜底
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keyPan.y += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keyPan.y -= 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keyPan.x -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keyPan.x += 1f;
                if (Input.GetKeyDown(KeyCode.R)) reset = true;
            }

            if (keyPan.sqrMagnitude > 0f)
            {
                keyPan.Normalize();
                float scale = Mathf.Max(0.35f, _targetDistance * 0.08f);
                Pan(keyPan * (KeyboardPanSpeed * scale * dt));
            }

            if (reset)
                ResetView();
        }

        private void PollMouse(float dt)
        {
            float scrollY = 0f;
            Vector2 pointer = Vector2.zero;
            bool leftDown = false;
            bool middleDown = false;
            bool leftPressed = false;
            bool middlePressed = false;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                scrollY = mouse.scroll.ReadValue().y;
                float steps = ScrollToZoomSteps(scrollY);
                if (Mathf.Abs(steps) > 0.001f)
                    Zoom(-steps);

                pointer = mouse.position.ReadValue();
                leftDown = mouse.leftButton.isPressed;
                middleDown = mouse.middleButton.isPressed;
                leftPressed = mouse.leftButton.wasPressedThisFrame;
                middlePressed = mouse.middleButton.wasPressedThisFrame;
            }
            else
            {
                scrollY = Input.mouseScrollDelta.y;
                float steps = ScrollToZoomSteps(scrollY);
                if (Mathf.Abs(steps) > 0.001f)
                    Zoom(-steps);

                pointer = Input.mousePosition;
                leftDown = Input.GetMouseButton(0);
                middleDown = Input.GetMouseButton(2);
                leftPressed = Input.GetMouseButtonDown(0);
                middlePressed = Input.GetMouseButtonDown(2);
            }

            bool overHud = pointer.x < 460f;
            bool wantDrag = middleDown || (leftDown && !overHud);
            if (!wantDrag)
            {
                _dragging = false;
                return;
            }

            if (leftPressed || middlePressed || !_dragging)
            {
                _lastPointer = pointer;
                _dragging = true;
                MarkUserCameraInput();
                return;
            }

            Vector2 deltaPx = pointer - _lastPointer;
            _lastPointer = pointer;
            if (deltaPx.sqrMagnitude < 0.01f) return;

            MarkUserCameraInput();
            float worldPerPixel = Mathf.Max(0.002f, _targetDistance / Mathf.Max(1f, Screen.height));
            Vector3 right = _camera.transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            Vector3 delta = (-right * deltaPx.x - forward * deltaPx.y) * worldPerPixel;
            Pan(new Vector2(delta.x, delta.z));
        }

        private void MarkUserCameraInput()
        {
            _userDriveUntilUnscaled = Time.unscaledTime + 2.5f;
        }

        /// <summary>
        /// 把滚轮原始值换成「格数」。Windows/Input System 常见 ±120/格；触控板多为小数。
        /// </summary>
        private static float ScrollToZoomSteps(float scrollY)
        {
            if (Mathf.Abs(scrollY) < 0.01f) return 0f;
            float notches = Mathf.Abs(scrollY) >= 20f ? scrollY / 120f : scrollY;
            return notches * 1.35f;
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
            _lodLevel = CameraLodPolicy.Evaluate(_targetDistance).Level;
            ApplyLod(CameraLodPolicy.ForLevel(_lodLevel));
            return true;
        }

        private void ApplyLod(CameraLodDecision decision)
        {
            _decision = decision;
            _lodLevel = decision.Level;
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

            var earth = _sandboxRoot != null
                ? _sandboxRoot.GetComponentInChildren<WorldMapPresenter>()
                : null;
            if (earth != null)
                earth.ApplyRenderLod(decision);
        }
    }
}
