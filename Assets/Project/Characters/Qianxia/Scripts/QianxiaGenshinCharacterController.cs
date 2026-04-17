using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class QianxiaGenshinCharacterController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private string _actionMapName = "Player";
    [SerializeField] private string _moveActionName = "Move";
    [SerializeField] private string _jumpActionName = "Jump";
    [SerializeField] private string _sprintActionName = "Sprint";
    [SerializeField] private float _moveDeadZone = 0.1f;

    [Header("Movement")]
    [SerializeField] private float _walkSpeed = 2.6f;
    [SerializeField] private float _sprintSpeed = 6.4f;
    [SerializeField] private float _groundAcceleration = 26.0f;
    [SerializeField] private float _groundDeceleration = 30.0f;
    [SerializeField] private float _airAcceleration = 9.0f;
    [SerializeField] private float _airDeceleration = 4.0f;
    [SerializeField] private float _rotationSmoothTime = 0.07f;
    [SerializeField] private float _quickTapFacingHoldTime = 0.22f;
    [SerializeField] private float _jumpHeight = 1.15f;
    [SerializeField] private float _gravity = -30.0f;
    [SerializeField] private float _terminalVelocity = -36.0f;
    [SerializeField] private float _groundedStickForce = -3.0f;
    [SerializeField] private float _coyoteTime = 0.12f;
    [SerializeField] private float _jumpBufferTime = 0.12f;
    [SerializeField] private bool _snapToGroundOnStart = true;
    [SerializeField] private float _groundSnapProbeHeight = 2.5f;
    [SerializeField] private float _groundSnapDistance = 40.0f;
    [SerializeField] private float _groundProbeDistance = 0.25f;
    [SerializeField] private LayerMask _groundLayers = ~0;

    [Header("Visual")]
    [SerializeField] private Transform _visualRoot;
    [SerializeField] private Transform _movementReferenceCamera;
    [SerializeField] private float _visualYawOffset;
    [SerializeField] private Animator _animator;
    [SerializeField] private string _speedParameter = "Speed";
    [SerializeField] private string _groundedParameter = "Grounded";
    [SerializeField] private string _moveParameter = "HasMove";
    [SerializeField] private float _animDampTime = 0.08f;
    [SerializeField] private float _walkBlendThreshold = 0.45f;

    private CharacterController _characterController;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;

    private Vector3 _horizontalVelocity;
    private float _verticalVelocity;
    private float _rotationVelocity;
    private float _lastGroundedTime = float.NegativeInfinity;
    private float _lastJumpPressedTime = float.NegativeInfinity;
    private float _lastMoveStartedTime = float.NegativeInfinity;
    private float _continueFacingUntilTime = float.NegativeInfinity;
    private float _cachedFacingYaw;
    private bool _isGrounded;
    private bool _hasMoveInput;

    private int _speedHash;
    private int _groundedHash;
    private int _moveHash;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        BindActions();
        CacheAnimatorHashes();
        EnsureVisualRoot();
        TryResolveMovementReferenceCamera();

        if (_snapToGroundOnStart)
            SnapToGroundImmediately();
    }

    private void OnEnable()
    {
        _moveAction?.Enable();
        _jumpAction?.Enable();
        _sprintAction?.Enable();
    }

    private void OnDisable()
    {
        _moveAction?.Disable();
        _jumpAction?.Disable();
        _sprintAction?.Disable();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0f)
            return;

        Vector2 moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
            _lastJumpPressedTime = Time.time;

        RefreshGroundedState();
        Vector3 desiredHorizontalVelocity = ResolveDesiredHorizontalVelocity(moveInput);

        UpdateHorizontalVelocity(desiredHorizontalVelocity, deltaTime);
        UpdateFacing(desiredHorizontalVelocity, deltaTime);
        UpdateVerticalVelocity(deltaTime);

        Vector3 motion = _horizontalVelocity;
        motion.y = _verticalVelocity;

        CollisionFlags collisionFlags = _characterController.Move(motion * deltaTime);

        ApplyPostMoveGroundedState(collisionFlags);
        if (_isGrounded && _verticalVelocity < 0.0f)
            _verticalVelocity = _groundedStickForce;

        UpdateAnimator();
        UpdateVisualRoot();
    }

    private void BindActions()
    {
        if (_inputActions == null)
            return;

        InputActionMap actionMap = _inputActions.FindActionMap(_actionMapName, false);
        if (actionMap == null)
            return;

        _moveAction = actionMap.FindAction(_moveActionName, false);
        _jumpAction = actionMap.FindAction(_jumpActionName, false);
        _sprintAction = actionMap.FindAction(_sprintActionName, false);
    }

    private void CacheAnimatorHashes()
    {
        _speedHash = string.IsNullOrWhiteSpace(_speedParameter) ? 0 : Animator.StringToHash(_speedParameter);
        _groundedHash = string.IsNullOrWhiteSpace(_groundedParameter) ? 0 : Animator.StringToHash(_groundedParameter);
        _moveHash = string.IsNullOrWhiteSpace(_moveParameter) ? 0 : Animator.StringToHash(_moveParameter);
    }

    private void EnsureVisualRoot()
    {
        if (_visualRoot == null && transform.childCount > 0)
            _visualRoot = transform.GetChild(0);
    }

    private void SnapToGroundImmediately()
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.1f, _groundSnapProbeHeight);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundSnapDistance, _groundLayers, QueryTriggerInteraction.Ignore))
            return;

        SetFeetOnGround(hit.point.y);
        _verticalVelocity = _groundedStickForce;
    }

    [ContextMenu("Snap Feet To Ground")]
    public void SnapFeetToGround()
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.1f, _groundSnapProbeHeight);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundSnapDistance, _groundLayers, QueryTriggerInteraction.Ignore))
            return;

        SetFeetOnGround(hit.point.y);
    }

    private void RefreshGroundedState()
    {
        if (_characterController.isGrounded)
        {
            _lastGroundedTime = Time.time;
            _isGrounded = true;
            return;
        }

        float probeRadius = Mathf.Max(0.05f, _characterController.radius * 0.92f);
        Vector3 origin = transform.position + Vector3.up * (probeRadius + 0.05f);
        if (Physics.SphereCast(origin, probeRadius, Vector3.down, out _, _groundProbeDistance, _groundLayers, QueryTriggerInteraction.Ignore))
        {
            _lastGroundedTime = Time.time;
            _isGrounded = true;
            return;
        }

        _isGrounded = false;
    }

    private void ApplyPostMoveGroundedState(CollisionFlags collisionFlags)
    {
        bool isGroundedAfterMove = (collisionFlags & CollisionFlags.Below) != 0 || _characterController.isGrounded;
        if (!isGroundedAfterMove)
        {
            _isGrounded = false;
            return;
        }

        _lastGroundedTime = Time.time;
        _isGrounded = true;
    }

    private Vector3 ResolveDesiredHorizontalVelocity(Vector2 moveInput)
    {
        float inputMagnitude = moveInput.magnitude;
        _hasMoveInput = inputMagnitude > _moveDeadZone;

        if (!_hasMoveInput)
            return Vector3.zero;

        Vector2 normalizedInput = moveInput.normalized * Mathf.Clamp01((inputMagnitude - _moveDeadZone) / (1.0f - _moveDeadZone));

        if (_movementReferenceCamera == null)
            TryResolveMovementReferenceCamera();

        Transform referenceTransform = _movementReferenceCamera;
        Vector3 referenceForward = referenceTransform != null ? Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 referenceRight = referenceTransform != null ? Vector3.ProjectOnPlane(referenceTransform.right, Vector3.up).normalized : Vector3.right;

        if (referenceForward.sqrMagnitude < 0.001f)
            referenceForward = Vector3.forward;

        if (referenceRight.sqrMagnitude < 0.001f)
            referenceRight = Vector3.right;

        Vector3 moveDirection = (referenceRight * normalizedInput.x) + (referenceForward * normalizedInput.y);
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1.0f);

        bool isSprinting = _sprintAction != null && _sprintAction.IsPressed() && normalizedInput.y > 0.1f;
        float targetSpeed = isSprinting ? _sprintSpeed : _walkSpeed;
        return moveDirection * targetSpeed;
    }

    private void UpdateHorizontalVelocity(Vector3 desiredHorizontalVelocity, float deltaTime)
    {
        float acceleration = _isGrounded ? _groundAcceleration : _airAcceleration;
        float deceleration = _isGrounded ? _groundDeceleration : _airDeceleration;
        float rate = desiredHorizontalVelocity.sqrMagnitude > 0.001f ? acceleration : deceleration;

        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desiredHorizontalVelocity, rate * deltaTime);
    }

    private void UpdateFacing(Vector3 desiredHorizontalVelocity, float deltaTime)
    {
        bool shouldContinueFacing = Time.time <= _continueFacingUntilTime;
        bool hasDesiredFacing = desiredHorizontalVelocity.sqrMagnitude > 0.001f;

        if (hasDesiredFacing)
        {
            _lastMoveStartedTime = Time.time;
            _cachedFacingYaw = Mathf.Atan2(desiredHorizontalVelocity.x, desiredHorizontalVelocity.z) * Mathf.Rad2Deg;
            _continueFacingUntilTime = Time.time + _quickTapFacingHoldTime;
        }
        else if (!shouldContinueFacing)
        {
            return;
        }

        float targetYaw = hasDesiredFacing ? _cachedFacingYaw : _cachedFacingYaw;
        float smoothTime = Mathf.Max(0.0001f, _rotationSmoothTime);
        float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref _rotationVelocity, smoothTime, Mathf.Infinity, deltaTime);
        transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
    }

    private void UpdateVerticalVelocity(float deltaTime)
    {
        bool canUseCoyoteJump = Time.time - _lastGroundedTime <= _coyoteTime;
        bool hasBufferedJump = Time.time - _lastJumpPressedTime <= _jumpBufferTime;

        if (_isGrounded && _verticalVelocity < 0.0f)
            _verticalVelocity = _groundedStickForce;

        if (canUseCoyoteJump && hasBufferedJump)
        {
            _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2.0f * _gravity);
            _lastJumpPressedTime = float.NegativeInfinity;
            _lastGroundedTime = float.NegativeInfinity;
            _isGrounded = false;
        }

        _verticalVelocity += _gravity * deltaTime;
        if (_verticalVelocity < _terminalVelocity)
            _verticalVelocity = _terminalVelocity;
    }

    private void UpdateAnimator()
    {
        if (_animator == null)
            return;

        float horizontalSpeed = _horizontalVelocity.magnitude;
        float normalizedSpeed = CalculateLocomotionBlend(horizontalSpeed);

        if (_speedHash != 0)
            _animator.SetFloat(_speedHash, normalizedSpeed, _animDampTime, Time.deltaTime);

        if (_groundedHash != 0)
            _animator.SetBool(_groundedHash, _isGrounded);

        if (_moveHash != 0)
            _animator.SetBool(_moveHash, _hasMoveInput);
    }

    private float CalculateLocomotionBlend(float horizontalSpeed)
    {
        if (horizontalSpeed <= 0.001f || _walkSpeed <= 0.001f)
            return 0.0f;

        float walkThreshold = Mathf.Clamp01(_walkBlendThreshold);
        if (horizontalSpeed <= _walkSpeed || _sprintSpeed <= _walkSpeed + 0.001f)
            return Mathf.Lerp(0.0f, walkThreshold, Mathf.Clamp01(horizontalSpeed / _walkSpeed));

        float sprintAlpha = Mathf.InverseLerp(_walkSpeed, _sprintSpeed, horizontalSpeed);
        return Mathf.Lerp(walkThreshold, 1.0f, sprintAlpha);
    }

    private void UpdateVisualRoot()
    {
        if (_visualRoot == null)
            return;

        _visualRoot.localRotation = Quaternion.Euler(0.0f, _visualYawOffset, 0.0f);
    }

    private void TryResolveMovementReferenceCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            _movementReferenceCamera = mainCamera.transform;
            return;
        }

        Camera anyCamera = Object.FindFirstObjectByType<Camera>();
        if (anyCamera != null)
            _movementReferenceCamera = anyCamera.transform;
    }

    private void SetFeetOnGround(float groundY)
    {
        float feetOffset = _characterController.center.y - (_characterController.height * 0.5f);
        Vector3 position = transform.position;
        position.y = groundY - feetOffset;
        transform.position = position;
    }
}
