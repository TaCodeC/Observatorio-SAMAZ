using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mouse button used to start a click-and-drag camera gesture.
/// </summary>
public enum StellarMouseLookButton
{
    Primary,
    Secondary
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class StellarFlyCameraController : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private bool controlsEnabled = true;
    [SerializeField, Min(0.01f)] private float moveSpeed = 8f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 4f;
    [SerializeField, Range(0.05f, 1f)] private float slowMultiplier = 0.25f;

    [Header("Vista")]
    [Tooltip("Arrastra este botón sobre el cielo para girar la cámara. Un clic breve con el primario se reserva para seleccionar estrellas.")]
    [SerializeField] private StellarMouseLookButton mouseLookButton = StellarMouseLookButton.Secondary;
    [SerializeField, Min(1f)] private float mouseDragThresholdPixels = 6f;
    [Tooltip("Una pulsación que comenzó sobre un control de interfaz no gira la cámara ni selecciona una estrella.")]
    [SerializeField] private bool ignoreMouseGesturesStartedOverUi = true;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
    [SerializeField, Min(1f)] private float keyboardLookSpeed = 75f;
    [SerializeField] private bool lockCursorWhileLooking = true;
    [SerializeField, Range(20f, 120f)] private float minFieldOfView = 35f;
    [SerializeField, Range(20f, 120f)] private float maxFieldOfView = 95f;
    [SerializeField, Min(0.1f)] private float fieldOfViewStep = 5f;

    [Header("Movil")]
    [SerializeField] private bool enableTouchControls = true;
    [SerializeField, Min(0.01f)] private float touchLookSensitivity = 0.08f;
    [SerializeField, Min(32f)] private float touchMoveRadiusPixels = 140f;

    private Camera controlledCamera;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float startFieldOfView;
    private float yaw;
    private float pitch;
    private int movementTouchId = -1;
    private int lookTouchId = -1;
    private Vector2 movementTouchStart;
    private Vector2 movementTouchCurrent;
    private Vector2 touchLookDelta;
    private Vector3 touchMoveInput;
    private bool primaryPointerTracking;
    private bool primaryPointerStartedOverUi;
    private bool primaryPointerDragged;
    private Vector2 primaryPointerStartPosition;
    private bool secondaryPointerTracking;
    private bool secondaryPointerStartedOverUi;
    private bool secondaryPointerDragged;
    private Vector2 secondaryPointerStartPosition;
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

    /// <summary>
    /// Raised when the primary mouse button is released without becoming a drag and the gesture
    /// did not begin over a UI control. Consumers use the screen position to query the same star
    /// catalogue that is currently being rendered.
    /// </summary>
    public event Action<Vector2> WorldClickReleased;

    private void OnEnable()
    {
        controlledCamera = GetComponent<Camera>();
        CaptureStartPose();
        SyncAnglesFromTransform();
    }

    private void OnDisable()
    {
        movementTouchId = -1;
        lookTouchId = -1;
        ResetPrimaryPointerGesture();
        ResetSecondaryPointerGesture();
        ReleaseCursor();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
        slowMultiplier = Mathf.Clamp(slowMultiplier, 0.05f, 1f);
        mouseDragThresholdPixels = Mathf.Max(1f, mouseDragThresholdPixels);
        mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
        keyboardLookSpeed = Mathf.Max(1f, keyboardLookSpeed);
        minFieldOfView = Mathf.Clamp(minFieldOfView, 20f, 120f);
        maxFieldOfView = Mathf.Clamp(maxFieldOfView, minFieldOfView, 120f);
        fieldOfViewStep = Mathf.Max(0.1f, fieldOfViewStep);
        touchLookSensitivity = Mathf.Max(0.01f, touchLookSensitivity);
        touchMoveRadiusPixels = Mathf.Max(32f, touchMoveRadiusPixels);
    }

    private void Update()
    {
        if (!Application.isPlaying || !controlsEnabled)
        {
            return;
        }

        if (WasResetPressed())
        {
            ResetPose();
        }

        UpdateMouseGestures();
        UpdateTouchControls();
        HandleLook();
        HandleMovement();
        HandleFieldOfView();
    }

    private void CaptureStartPose()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        startFieldOfView = controlledCamera != null ? controlledCamera.fieldOfView : 60f;
    }

    private void SyncAnglesFromTransform()
    {
        Vector3 eulerAngles = transform.eulerAngles;
        yaw = eulerAngles.y;
        pitch = NormalizePitch(eulerAngles.x);
    }

    private void HandleLook()
    {
        Vector2 lookDelta = ReadKeyboardLookDelta() * keyboardLookSpeed * Time.unscaledDeltaTime;
        lookDelta += touchLookDelta * touchLookSensitivity;
        bool mouseLooking = IsMouseLookActive();

        if (mouseLooking)
        {
            if (lockCursorWhileLooking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            lookDelta += ReadMouseDelta() * mouseSensitivity;
        }
        else
        {
            ReleaseCursor();
        }

        if (lookDelta.sqrMagnitude <= 0f)
        {
            return;
        }

        yaw += lookDelta.x;
        pitch = Mathf.Clamp(pitch - lookDelta.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    /// <summary>
    /// Distinguishes a primary click from an intentional click-and-drag camera gesture. The
    /// decision is made from the press origin so UI controls remain reliable even when the
    /// pointer is released outside their rectangle.
    /// </summary>
    private void UpdateMouseGestures()
    {
        UpdatePrimaryPointerGesture();

        if (mouseLookButton == StellarMouseLookButton.Secondary)
        {
            UpdateSecondaryPointerGesture();
        }
        else
        {
            ResetSecondaryPointerGesture();
        }
    }

    private void UpdatePrimaryPointerGesture()
    {
        if (WasMouseButtonPressedThisFrame(StellarMouseLookButton.Primary))
        {
            primaryPointerTracking = true;
            primaryPointerStartPosition = ReadMousePosition();
            primaryPointerStartedOverUi = IsPointerOverUi(primaryPointerStartPosition);
            primaryPointerDragged = false;
        }

        if (!primaryPointerTracking)
        {
            return;
        }

        if (!primaryPointerStartedOverUi
            && IsMouseButtonPressed(StellarMouseLookButton.Primary)
            && !primaryPointerDragged)
        {
            Vector2 delta = ReadMousePosition() - primaryPointerStartPosition;
            primaryPointerDragged = delta.sqrMagnitude >= mouseDragThresholdPixels * mouseDragThresholdPixels;
        }

        if (WasMouseButtonReleasedThisFrame(StellarMouseLookButton.Primary))
        {
            Vector2 releasePosition = ReadMousePosition();
            bool shouldSelectWorld = !primaryPointerStartedOverUi && !primaryPointerDragged;
            ResetPrimaryPointerGesture();

            if (shouldSelectWorld)
            {
                WorldClickReleased?.Invoke(releasePosition);
            }

            return;
        }

        if (!IsMouseButtonPressed(StellarMouseLookButton.Primary))
        {
            ResetPrimaryPointerGesture();
        }
    }

    private void UpdateSecondaryPointerGesture()
    {
        if (WasMouseButtonPressedThisFrame(StellarMouseLookButton.Secondary))
        {
            secondaryPointerTracking = true;
            secondaryPointerStartPosition = ReadMousePosition();
            secondaryPointerStartedOverUi = IsPointerOverUi(secondaryPointerStartPosition);
            secondaryPointerDragged = false;
        }

        if (!secondaryPointerTracking)
        {
            return;
        }

        if (!secondaryPointerStartedOverUi
            && IsMouseButtonPressed(StellarMouseLookButton.Secondary)
            && !secondaryPointerDragged)
        {
            Vector2 delta = ReadMousePosition() - secondaryPointerStartPosition;
            secondaryPointerDragged = delta.sqrMagnitude >= mouseDragThresholdPixels * mouseDragThresholdPixels;
        }

        if (WasMouseButtonReleasedThisFrame(StellarMouseLookButton.Secondary)
            || !IsMouseButtonPressed(StellarMouseLookButton.Secondary))
        {
            ResetSecondaryPointerGesture();
        }
    }

    private bool IsPointerOverUi(Vector2 screenPosition)
    {
        if (!ignoreMouseGesturesStartedOverUi || EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition
        };

        EventSystem.current.RaycastAll(pointerData, uiRaycastResults);
        bool isOverUi = uiRaycastResults.Count > 0;
        uiRaycastResults.Clear();
        return isOverUi;
    }

    private void ResetPrimaryPointerGesture()
    {
        primaryPointerTracking = false;
        primaryPointerStartedOverUi = false;
        primaryPointerDragged = false;
        primaryPointerStartPosition = Vector2.zero;
    }

    private void ResetSecondaryPointerGesture()
    {
        secondaryPointerTracking = false;
        secondaryPointerStartedOverUi = false;
        secondaryPointerDragged = false;
        secondaryPointerStartPosition = Vector2.zero;
    }

    private void HandleMovement()
    {
        Vector3 input = ReadMovementInput();

        if (input.sqrMagnitude <= 0f)
        {
            return;
        }

        float speed = moveSpeed;
        if (IsSprintPressed())
        {
            speed *= sprintMultiplier;
        }

        if (IsSlowPressed())
        {
            speed *= slowMultiplier;
        }

        Vector3 worldMotion = transform.TransformDirection(input.normalized);
        transform.position += worldMotion * speed * Time.unscaledDeltaTime;
    }

    private void HandleFieldOfView()
    {
        if (controlledCamera == null)
        {
            return;
        }

        float zoomInput = ReadZoomInput();
        if (Mathf.Abs(zoomInput) <= 0.01f)
        {
            return;
        }

        float direction = Mathf.Sign(zoomInput);
        controlledCamera.fieldOfView = Mathf.Clamp(
            controlledCamera.fieldOfView - direction * fieldOfViewStep,
            minFieldOfView,
            maxFieldOfView
        );
    }

    private void UpdateTouchControls()
    {
        touchLookDelta = Vector2.zero;
        touchMoveInput = Vector3.zero;

        if (!enableTouchControls)
        {
            movementTouchId = -1;
            lookTouchId = -1;
            return;
        }

#if ENABLE_INPUT_SYSTEM
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            bool movementSeen = false;
            bool lookSeen = false;

            for (int i = 0; i < touchscreen.touches.Count; i++)
            {
                var touch = touchscreen.touches[i];
                UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                int touchId = touch.touchId.ReadValue();
                Vector2 position = touch.position.ReadValue();

                if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    ReleaseTouch(touchId);
                    continue;
                }

                if (phase == UnityEngine.InputSystem.TouchPhase.None)
                {
                    continue;
                }

                AssignTouchIfNeeded(touchId, position);

                if (touchId == movementTouchId)
                {
                    movementSeen = true;
                    movementTouchCurrent = position;
                }
                else if (touchId == lookTouchId)
                {
                    lookSeen = true;
                    touchLookDelta += touch.delta.ReadValue();
                }
            }

            if (!movementSeen)
            {
                movementTouchId = -1;
            }

            if (!lookSeen)
            {
                lookTouchId = -1;
            }

            touchMoveInput = CalculateTouchMoveInput();
            return;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool legacyMovementSeen = false;
        bool legacyLookSeen = false;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                ReleaseTouch(touch.fingerId);
                continue;
            }

            AssignTouchIfNeeded(touch.fingerId, touch.position);

            if (touch.fingerId == movementTouchId)
            {
                legacyMovementSeen = true;
                movementTouchCurrent = touch.position;
            }
            else if (touch.fingerId == lookTouchId)
            {
                legacyLookSeen = true;
                touchLookDelta += touch.deltaPosition;
            }
        }

        if (!legacyMovementSeen)
        {
            movementTouchId = -1;
        }

        if (!legacyLookSeen)
        {
            lookTouchId = -1;
        }

        touchMoveInput = CalculateTouchMoveInput();
#endif
    }

    private void AssignTouchIfNeeded(int touchId, Vector2 position)
    {
        bool isRightHalf = position.x >= Screen.width * 0.5f;

        if (!isRightHalf && movementTouchId < 0)
        {
            movementTouchId = touchId;
            movementTouchStart = position;
            movementTouchCurrent = position;
        }
        else if (isRightHalf && lookTouchId < 0)
        {
            lookTouchId = touchId;
        }
    }

    private void ReleaseTouch(int touchId)
    {
        if (touchId == movementTouchId)
        {
            movementTouchId = -1;
        }

        if (touchId == lookTouchId)
        {
            lookTouchId = -1;
        }
    }

    private Vector3 CalculateTouchMoveInput()
    {
        if (movementTouchId < 0)
        {
            return Vector3.zero;
        }

        float radius = Mathf.Max(1f, touchMoveRadiusPixels);
        Vector2 offset = Vector2.ClampMagnitude(movementTouchCurrent - movementTouchStart, radius) / radius;
        return new Vector3(offset.x, 0f, offset.y);
    }

    private static bool WasResetPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.rKey.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.R);
#else
        return false;
#endif
    }

    private bool IsMouseLookActive()
    {
        return mouseLookButton == StellarMouseLookButton.Primary
            ? primaryPointerTracking
                && primaryPointerDragged
                && !primaryPointerStartedOverUi
                && IsMouseButtonPressed(StellarMouseLookButton.Primary)
            : secondaryPointerTracking
                && secondaryPointerDragged
                && !secondaryPointerStartedOverUi
                && IsMouseButtonPressed(StellarMouseLookButton.Secondary);
    }

    private static bool WasMouseButtonPressedThisFrame(StellarMouseLookButton button)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            return button == StellarMouseLookButton.Primary
                ? mouse.leftButton.wasPressedThisFrame
                : mouse.rightButton.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(button == StellarMouseLookButton.Primary ? 0 : 1);
#else
        return false;
#endif
    }

    private static bool WasMouseButtonReleasedThisFrame(StellarMouseLookButton button)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            return button == StellarMouseLookButton.Primary
                ? mouse.leftButton.wasReleasedThisFrame
                : mouse.rightButton.wasReleasedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonUp(button == StellarMouseLookButton.Primary ? 0 : 1);
#else
        return false;
#endif
    }

    private static bool IsMouseButtonPressed(StellarMouseLookButton button)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            return button == StellarMouseLookButton.Primary
                ? mouse.leftButton.isPressed
                : mouse.rightButton.isPressed;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(button == StellarMouseLookButton.Primary ? 0 : 1);
#else
        return false;
#endif
    }

    private static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            return mouse.position.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    private static Vector2 ReadMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            return mouse.delta.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }

    private static Vector2 ReadKeyboardLookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Vector2 input = Vector2.zero;
            if (keyboard.leftArrowKey.isPressed || keyboard.jKey.isPressed)
            {
                input.x -= 1f;
            }

            if (keyboard.rightArrowKey.isPressed || keyboard.lKey.isPressed)
            {
                input.x += 1f;
            }

            if (keyboard.upArrowKey.isPressed || keyboard.iKey.isPressed)
            {
                input.y += 1f;
            }

            if (keyboard.downArrowKey.isPressed || keyboard.kKey.isPressed)
            {
                input.y -= 1f;
            }

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        Vector2 legacyInput = Vector2.zero;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.J))
        {
            legacyInput.x -= 1f;
        }

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.L))
        {
            legacyInput.x += 1f;
        }

        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.I))
        {
            legacyInput.y += 1f;
        }

        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.K))
        {
            legacyInput.y -= 1f;
        }

        return legacyInput.sqrMagnitude > 1f ? legacyInput.normalized : legacyInput;
#else
        return Vector2.zero;
#endif
    }

    private Vector3 ReadMovementInput()
    {
        Vector3 input = ReadKeyboardMovementInput();
        if (enableTouchControls && touchMoveInput.sqrMagnitude > 0f)
        {
            input += touchMoveInput;
        }

        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

    private static Vector3 ReadKeyboardMovementInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed)
            {
                input.z += 1f;
            }

            if (keyboard.sKey.isPressed)
            {
                input.z -= 1f;
            }

            if (keyboard.dKey.isPressed)
            {
                input.x += 1f;
            }

            if (keyboard.aKey.isPressed)
            {
                input.x -= 1f;
            }

            if (keyboard.eKey.isPressed)
            {
                input.y += 1f;
            }

            if (keyboard.qKey.isPressed)
            {
                input.y -= 1f;
            }

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        Vector3 legacyInput = Vector3.zero;
        if (Input.GetKey(KeyCode.W))
        {
            legacyInput.z += 1f;
        }

        if (Input.GetKey(KeyCode.S))
        {
            legacyInput.z -= 1f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            legacyInput.x += 1f;
        }

        if (Input.GetKey(KeyCode.A))
        {
            legacyInput.x -= 1f;
        }

        if (Input.GetKey(KeyCode.E))
        {
            legacyInput.y += 1f;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            legacyInput.y -= 1f;
        }

        return legacyInput.sqrMagnitude > 1f ? legacyInput.normalized : legacyInput;
#else
        return Vector3.zero;
#endif
    }

    private static bool IsSprintPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }

    private static bool IsSlowPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#else
        return false;
#endif
    }

    private static float ReadZoomInput()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                return scroll;
            }
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.zKey.wasPressedThisFrame)
            {
                return 1f;
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                return -1f;
            }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        float legacyScroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(legacyScroll) > 0.01f)
        {
            return legacyScroll;
        }

        if (Input.GetKeyDown(KeyCode.Z))
        {
            return 1f;
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            return -1f;
        }
#endif
        return 0f;
    }

    private void ResetPose()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        if (controlledCamera != null)
        {
            controlledCamera.fieldOfView = startFieldOfView;
        }

        SyncAnglesFromTransform();
    }

    private void ReleaseCursor()
    {
        if (!Application.isPlaying || !lockCursorWhileLooking)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static float NormalizePitch(float eulerX)
    {
        return eulerX > 180f ? eulerX - 360f : eulerX;
    }
}
