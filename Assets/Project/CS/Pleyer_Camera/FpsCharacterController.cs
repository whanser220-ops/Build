using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FpsCharacterController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Project input actions asset. Defaults to Assets/InputSystem_Actions.inputactions.")]
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private string _actionMapName = "Player";
    [SerializeField] private string _moveActionName = "Move";
    [SerializeField] private string _lookActionName = "Look";
    [SerializeField] private string _jumpActionName = "Jump";
    [SerializeField] private string _sprintActionName = "Sprint";

    [Header("Movement")]
    [SerializeField] private float _walkSpeed = 4.5f;
    [SerializeField] private float _sprintSpeed = 7.0f;
    [SerializeField] private float _jumpHeight = 1.2f;
    [SerializeField] private float _gravity = -25.0f;
    [SerializeField] private float _groundedStickForce = -2.0f;

    [Header("Look")]
    [SerializeField] private Transform _cameraPivot;
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private float _lookSensitivity = 0.12f;
    [SerializeField] private float _gamepadLookSensitivity = 120.0f;
    [SerializeField] private float _minPitch = -85.0f;
    [SerializeField] private float _maxPitch = 85.0f;
    [SerializeField] private bool _lockCursorOnPlay = true;

    [Header("Camera Rig")]
    [SerializeField] private float _cameraHeight = 1.65f;

    private CharacterController _characterController;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;

    private float _pitch;
    private float _verticalVelocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        EnsureCameraRig();
        BindActions();
    }

    private void OnEnable()
    {
        _moveAction?.Enable();
        _lookAction?.Enable();
        _jumpAction?.Enable();
        _sprintAction?.Enable();

        if (_lockCursorOnPlay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        _moveAction?.Disable();
        _lookAction?.Disable();
        _jumpAction?.Disable();
        _sprintAction?.Disable();

        if (_lockCursorOnPlay)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (_cameraPivot == null)
            return;

        UpdateLook();
        UpdateMovement();
    }

    private void UpdateLook()
    {
        Vector2 lookInput = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
        bool usingGamepad = Gamepad.current != null && _lookAction != null && _lookAction.activeControl != null &&
                            _lookAction.activeControl.device == Gamepad.current;

        float lookScale = usingGamepad ? _gamepadLookSensitivity * Time.deltaTime : _lookSensitivity;
        float yawDelta = lookInput.x * lookScale;
        float pitchDelta = lookInput.y * lookScale;

        _pitch = Mathf.Clamp(_pitch - pitchDelta, _minPitch, _maxPitch);
        _cameraPivot.localRotation = Quaternion.Euler(_pitch, 0.0f, 0.0f);
        transform.Rotate(Vector3.up * yawDelta, Space.Self);
    }

    private void UpdateMovement()
    {
        Vector2 moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        move = Vector3.ClampMagnitude(move, 1.0f);

        bool grounded = _characterController.isGrounded;
        if (grounded && _verticalVelocity < 0.0f)
            _verticalVelocity = _groundedStickForce;

        if (grounded && _jumpAction != null && _jumpAction.WasPressedThisFrame())
            _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2.0f * _gravity);

        _verticalVelocity += _gravity * Time.deltaTime;

        bool sprintHeld = _sprintAction != null && _sprintAction.IsPressed();
        float speed = sprintHeld ? _sprintSpeed : _walkSpeed;
        Vector3 velocity = move * speed;
        velocity.y = _verticalVelocity;

        _characterController.Move(velocity * Time.deltaTime);
    }

    private void BindActions()
    {
        if (_inputActions == null)
            _inputActions = Resources.Load<InputActionAsset>("InputSystem_Actions");

        if (_inputActions == null)
            return;

        InputActionMap actionMap = _inputActions.FindActionMap(_actionMapName, true);
        _moveAction = actionMap.FindAction(_moveActionName, true);
        _lookAction = actionMap.FindAction(_lookActionName, true);
        _jumpAction = actionMap.FindAction(_jumpActionName, false);
        _sprintAction = actionMap.FindAction(_sprintActionName, false);
    }

    private void EnsureCameraRig()
    {
        if (_cameraPivot == null)
        {
            Transform existingPivot = transform.Find("CameraPivot");
            if (existingPivot != null)
            {
                _cameraPivot = existingPivot;
            }
            else
            {
                GameObject pivot = new GameObject("CameraPivot");
                pivot.transform.SetParent(transform);
                pivot.transform.localPosition = new Vector3(0.0f, _cameraHeight, 0.0f);
                pivot.transform.localRotation = Quaternion.identity;
                _cameraPivot = pivot.transform;
            }
        }

        _cameraPivot.localPosition = new Vector3(0.0f, _cameraHeight, 0.0f);

        if (_playerCamera == null)
        {
            _playerCamera = GetComponentInChildren<Camera>();
            if (_playerCamera == null && Camera.main != null)
            {
                _playerCamera = Camera.main;
                _playerCamera.transform.SetParent(_cameraPivot);
                _playerCamera.transform.localPosition = Vector3.zero;
                _playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        if (_playerCamera != null && _playerCamera.transform.parent != _cameraPivot)
        {
            _playerCamera.transform.SetParent(_cameraPivot);
            _playerCamera.transform.localPosition = Vector3.zero;
            _playerCamera.transform.localRotation = Quaternion.identity;
        }
    }

    [ContextMenu("Setup FPS Camera Rig")]
    private void SetupCameraRig()
    {
        EnsureCameraRig();
    }

    private void Reset()
    {
        _characterController = GetComponent<CharacterController>();
        _characterController.center = new Vector3(0.0f, 1.0f, 0.0f);
        _characterController.height = 2.0f;
        _characterController.radius = 0.35f;
        EnsureCameraRig();
    }
}
