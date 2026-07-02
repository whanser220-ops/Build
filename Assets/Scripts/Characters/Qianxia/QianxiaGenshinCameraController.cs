using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public class QianxiaGenshinCameraController : MonoBehaviour
{
    private const int CollisionHitBufferSize = 32;

    [Header("Input")]
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private string _actionMapName = "Player";
    [SerializeField] private string _lookActionName = "Look";
    [SerializeField] private float _lookDeadZone = 0.01f;
    [SerializeField] private float _mouseSensitivity = 0.14f;
    [SerializeField] private float _gamepadSensitivity = 150.0f;

    [Header("Follow")]
    [SerializeField] private Transform _followTarget;
    [SerializeField] private Vector3 _targetOffset = new Vector3(0.0f, 2.7431698f, 0.0f);
    [SerializeField] private float _followSharpness = 18.0f;
    [SerializeField] private float _rotationSmoothTime = 0.03f;
    [SerializeField] private float _distanceSmoothTime = 0.05f;
    [SerializeField] private float _defaultDistance = 4.6f;
    [SerializeField] private float _minDistance = 2.2f;
    [SerializeField] private float _maxDistance = 6.0f;
    [SerializeField] private float _zoomSpeed = 0.8f;
    [SerializeField] private float _minPitch = -18.0f;
    [SerializeField] private float _maxPitch = 62.0f;

    [Header("Collision")]
    [SerializeField] private float _collisionRadius = 0.18f;
    [SerializeField] private LayerMask _collisionMask = ~0;
    [SerializeField] private float _minimumCollisionDistance = 0.9f;

    [Header("Recentering")]
    [SerializeField] private bool _enableMovementDrivenCamera = true;
    [SerializeField] private float _manualLookGraceTime = 0.45f;
    [SerializeField] private float _forwardRecenterSharpness = 5.0f;
    [SerializeField] private float _sideStrafeRotateSpeed = 46.0f;
    [SerializeField] private float _topDownSideRotateMultiplier = 2.2f;
    [SerializeField] private bool _lockCursorOnPlay = true;

    private InputAction _lookAction;
    private InputAction _moveAction;
    private Camera _controlledCamera;

    private Vector3 _smoothedTarget;
    private float _targetYaw;
    private float _targetPitch;
    private float _currentYaw;
    private float _currentPitch;
    private float _yawVelocity;
    private float _pitchVelocity;
    private float _currentDistance;
    private float _targetDistance;
    private float _distanceVelocity;
    private float _lastManualLookTime = float.NegativeInfinity;
    private bool _isMouseLookCaptured;
    private readonly RaycastHit[] _collisionHits = new RaycastHit[CollisionHitBufferSize];

    public Transform FollowTarget => _followTarget;
    public Camera ControlledCamera => _controlledCamera;

    private void Awake()
    {
        if (_followTarget == null)
            _followTarget = transform;

        BindActions();
        EnsureCamera();
        InitializeCameraState();
    }

    private void OnEnable()
    {
        _lookAction?.Enable();
        _moveAction?.Enable();

        if (_lockCursorOnPlay)
            SetMouseLookCaptured(true);
    }

    private void OnDisable()
    {
        _lookAction?.Disable();
        _moveAction?.Disable();

        if (_lockCursorOnPlay)
            SetMouseLookCaptured(false);
    }

    private void LateUpdate()
    {
        UpdateCursorCaptureState();

        if (_controlledCamera == null || _followTarget == null)
            return;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0f)
            return;

        Vector3 desiredTarget = _followTarget.position + _targetOffset;
        float followLerp = 1.0f - Mathf.Exp(-Mathf.Max(0.0f, _followSharpness) * deltaTime);
        _smoothedTarget = Vector3.Lerp(_smoothedTarget, desiredTarget, followLerp);

        UpdateRotationTargets(deltaTime);
        UpdateZoom();

        float smoothTime = Mathf.Max(0.0001f, _rotationSmoothTime);
        _currentYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVelocity, smoothTime, Mathf.Infinity, deltaTime);
        _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, smoothTime, Mathf.Infinity, deltaTime);

        Quaternion orbitRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0.0f);
        float collisionFreeDistance = ResolveCollisionDistance(orbitRotation);
        float distanceSmoothTime = Mathf.Max(0.0001f, _distanceSmoothTime);
        _currentDistance = Mathf.SmoothDamp(_currentDistance, collisionFreeDistance, ref _distanceVelocity, distanceSmoothTime, Mathf.Infinity, deltaTime);

        Vector3 cameraPosition = _smoothedTarget + (orbitRotation * Vector3.back * _currentDistance);
        _controlledCamera.transform.SetPositionAndRotation(cameraPosition, orbitRotation);
    }

    private void UpdateCursorCaptureState()
    {
        if (!_lockCursorOnPlay)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetMouseLookCaptured(false);
            return;
        }

        if (_isMouseLookCaptured && !IsCursorLockApplied())
        {
            SetMouseLookCaptured(false);
            return;
        }

        if (_isMouseLookCaptured || Mouse.current == null)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            SetMouseLookCaptured(true);
    }

    private void BindActions()
    {
        if (_inputActions == null)
            return;

        InputActionMap actionMap = _inputActions.FindActionMap(_actionMapName, false);
        if (actionMap == null)
            return;

        _lookAction = actionMap.FindAction(_lookActionName, false);
        _moveAction = actionMap.FindAction("Move", false);
    }

    private void EnsureCamera()
    {
        _controlledCamera = Camera.main;
        if (_controlledCamera == null)
            _controlledCamera = Object.FindFirstObjectByType<Camera>();

        if (_controlledCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            _controlledCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
        }

        Transform cameraTransform = _controlledCamera.transform;
        if (cameraTransform.parent != null)
            cameraTransform.SetParent(null, true);
    }

    private void InitializeCameraState()
    {
        Vector3 desiredTarget = _followTarget.position + _targetOffset;
        _smoothedTarget = desiredTarget;

        Vector3 offset = _controlledCamera.transform.position - desiredTarget;
        if (offset.sqrMagnitude < 0.01f)
            offset = Quaternion.Euler(18.0f, _followTarget.eulerAngles.y, 0.0f) * (Vector3.back * _defaultDistance);

        _targetDistance = Mathf.Clamp(offset.magnitude, _minDistance, _maxDistance);
        _currentDistance = _targetDistance;

        Quaternion lookRotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        Vector3 euler = lookRotation.eulerAngles;
        _targetYaw = euler.y;
        _currentYaw = _targetYaw;
        _targetPitch = ClampPitch(NormalizeAngle(euler.x));
        _currentPitch = _targetPitch;

        Quaternion orbitRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0.0f);
        Vector3 cameraPosition = _smoothedTarget + (orbitRotation * Vector3.back * _currentDistance);
        _controlledCamera.transform.SetPositionAndRotation(cameraPosition, orbitRotation);
    }

    private void UpdateRotationTargets(float deltaTime)
    {
        bool usingGamepad = IsUsingGamepad();
        Vector2 lookInput = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
        if (!usingGamepad && !CanConsumeMouseCameraInput())
            lookInput = Vector2.zero;

        if (lookInput.magnitude > _lookDeadZone)
        {
            float lookScale = usingGamepad ? _gamepadSensitivity * deltaTime : _mouseSensitivity;
            _targetYaw += lookInput.x * lookScale;
            _targetPitch = ClampPitch(_targetPitch - (lookInput.y * lookScale));
            _lastManualLookTime = Time.time;
        }

        if (!_enableMovementDrivenCamera)
            return;

        if (Time.time - _lastManualLookTime <= _manualLookGraceTime)
            return;

        Vector2 moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        if (moveInput.sqrMagnitude <= 0.01f)
            return;

        float pitchFactor = Mathf.InverseLerp(35.0f, _maxPitch, Mathf.Abs(_targetPitch));
        float sideSpeed = Mathf.Abs(moveInput.x) * _sideStrafeRotateSpeed * Mathf.Lerp(1.0f, _topDownSideRotateMultiplier, pitchFactor);
        float recenterSpeed = _forwardRecenterSharpness + sideSpeed;
        float blend = 1.0f - Mathf.Exp(-Mathf.Max(0.0f, recenterSpeed) * deltaTime);
        _targetYaw = Mathf.LerpAngle(_targetYaw, _followTarget.eulerAngles.y, blend);
    }

    private void UpdateZoom()
    {
        if (!CanConsumeMouseCameraInput() || Mouse.current == null)
            return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) <= 0.01f)
            return;

        _targetDistance = Mathf.Clamp(_targetDistance - (scroll * 0.01f * _zoomSpeed), _minDistance, _maxDistance);
    }

    private float ResolveCollisionDistance(Quaternion orbitRotation)
    {
        float desiredDistance = Mathf.Clamp(_targetDistance, _minDistance, _maxDistance);
        Vector3 direction = orbitRotation * Vector3.back;

        int hitCount = Physics.SphereCastNonAlloc(
            _smoothedTarget,
            _collisionRadius,
            direction,
            _collisionHits,
            desiredDistance,
            _collisionMask,
            QueryTriggerInteraction.Ignore);

        float resolvedDistance = desiredDistance;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];
            if (hit.collider == null)
                continue;

            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance < nearestDistance)
                nearestDistance = hit.distance;
        }

        if (!float.IsPositiveInfinity(nearestDistance))
            resolvedDistance = Mathf.Clamp(nearestDistance - _collisionRadius, _minimumCollisionDistance, desiredDistance);

        return resolvedDistance;
    }

    private bool IsUsingGamepad()
    {
        return Gamepad.current != null
               && _lookAction != null
               && _lookAction.activeControl != null
               && _lookAction.activeControl.device == Gamepad.current;
    }

    private bool CanConsumeMouseCameraInput()
    {
        return !_lockCursorOnPlay || _isMouseLookCaptured;
    }

    private void SetMouseLookCaptured(bool isCaptured)
    {
        _isMouseLookCaptured = isCaptured;
        Cursor.lockState = isCaptured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isCaptured;
    }

    private static bool IsCursorLockApplied()
    {
        return Cursor.lockState == CursorLockMode.Locked && !Cursor.visible;
    }

    private float ClampPitch(float pitch)
    {
        return Mathf.Clamp(pitch, _minPitch, _maxPitch);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180.0f)
            angle -= 360.0f;

        while (angle < -180.0f)
            angle += 360.0f;

        return angle;
    }
}
