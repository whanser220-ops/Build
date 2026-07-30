using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class RuntimeFreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 8.0f;
    [SerializeField] private float _fastMoveMultiplier = 3.0f;
    [SerializeField] private float _slowMoveMultiplier = 0.35f;
    [SerializeField] private float _verticalSpeedMultiplier = 0.75f;

    [Header("Look")]
    [SerializeField] private float _mouseSensitivity = 0.12f;
    [SerializeField] private float _gamepadSensitivity = 120.0f;
    [SerializeField] private float _minPitch = -85.0f;
    [SerializeField] private float _maxPitch = 85.0f;
    [SerializeField] private bool _lockCursorOnStart = true;

    private float _yaw;
    private float _pitch;
    private bool _isMouseLookCaptured;

    private void Awake()
    {
        Vector3 eulerAngles = transform.eulerAngles;
        _yaw = eulerAngles.y;
        _pitch = NormalizeAngle(eulerAngles.x);
    }

    private void OnEnable()
    {
        if (_lockCursorOnStart)
            SetMouseLookCaptured(true);
    }

    private void OnDisable()
    {
        if (_lockCursorOnStart)
            SetMouseLookCaptured(false);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0f)
            return;

        UpdateCursorCaptureState();
        UpdateLook(deltaTime);
        UpdateMovement(deltaTime);
    }

    private void UpdateCursorCaptureState()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetMouseLookCaptured(false);
            return;
        }

        Mouse mouse = Mouse.current;
        if (_isMouseLookCaptured || mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            SetMouseLookCaptured(true);
    }

    private void UpdateLook(float deltaTime)
    {
        Vector2 lookInput = Vector2.zero;

        if (_isMouseLookCaptured && Mouse.current != null)
            lookInput += Mouse.current.delta.ReadValue() * _mouseSensitivity;

        if (Gamepad.current != null)
            lookInput += Gamepad.current.rightStick.ReadValue() * (_gamepadSensitivity * deltaTime);

        if (lookInput.sqrMagnitude <= 0.0001f)
            return;

        _yaw += lookInput.x;
        _pitch = Mathf.Clamp(_pitch - lookInput.y, _minPitch, _maxPitch);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
    }

    private void UpdateMovement(float deltaTime)
    {
        Vector3 input = ReadMoveInput();
        if (input.sqrMagnitude <= 0.0001f)
            return;

        if (input.sqrMagnitude > 1.0f)
            input.Normalize();

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;

        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 movement = (right * input.x) + (forward * input.z) + (Vector3.up * input.y * _verticalSpeedMultiplier);

        float speed = _moveSpeed;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                speed *= _fastMoveMultiplier;
            else if (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
                speed *= _slowMoveMultiplier;
        }

        transform.position += movement * (speed * deltaTime);
    }

    private static Vector3 ReadMoveInput()
    {
        Vector3 input = Vector3.zero;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                input.x -= 1.0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                input.x += 1.0f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                input.z -= 1.0f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                input.z += 1.0f;
            if (keyboard.spaceKey.isPressed || keyboard.eKey.isPressed)
                input.y += 1.0f;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.qKey.isPressed || keyboard.cKey.isPressed)
                input.y -= 1.0f;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            Vector2 stick = gamepad.leftStick.ReadValue();
            input.x += stick.x;
            input.z += stick.y;
            if (gamepad.rightShoulder.isPressed)
                input.y += 1.0f;
            if (gamepad.leftShoulder.isPressed)
                input.y -= 1.0f;
        }

        return input;
    }

    private void SetMouseLookCaptured(bool isCaptured)
    {
        _isMouseLookCaptured = isCaptured;
        Cursor.lockState = isCaptured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isCaptured;
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
