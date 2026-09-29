using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
///   W A S D  - walk, relative to where the camera is facing
///   Shift    - faster
///   Space    - up      
///   Ctrl     - down    
/// </summary>

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float sprintSpeed = 4.0f;

    [Header("Direction")]
    [Tooltip("Movement follows this transform's facing. Assign the Main Camera.")]
    [SerializeField] private Transform cameraTransform;

    [Header("XR")]
    [SerializeField, Range(0f, 0.5f)] private float stickDeadzone = 0.15f;

    [Header("Gravity")]
    [Tooltip("Turn off if the floor has no collider and you keep falling through.")]
    [SerializeField] private bool useGravity = true;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float verticalSpeed = 2.0f;

    private CharacterController controller;
    private AudioListener[] audioListeners;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        audioListeners = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void LateUpdate()
    {
        if (audioListeners == null || audioListeners.Length < 2)
            return;

        bool usingXr = XRSettings.isDeviceActive;
        AudioListener selected = null;

        foreach (AudioListener listener in audioListeners)
        {
            if (listener != null && listener.gameObject.activeInHierarchy &&
                IsXrListener(listener) == usingXr)
            {
                selected = listener;
                break;
            }
        }

        if (selected == null)
        {
            foreach (AudioListener listener in audioListeners)
            {
                if (listener != null && listener.gameObject.activeInHierarchy)
                {
                    selected = listener;
                    break;
                }
            }
        }

        if (selected == null)
            return;

        foreach (AudioListener listener in audioListeners)
        {
            if (listener != null && listener.enabled != (listener == selected))
            {
                listener.enabled = listener == selected;
            }
        }
    }

    private static bool IsXrListener(AudioListener listener)
    {
        for (Transform current = listener.transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("XR Origin", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void Update()
    {
        UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool usingXr = XRSettings.isDeviceActive && head.isValid;
        Vector2 moveInput;
        float speed;

        // Typing a prompt must not walk the player across the room.
        if (!usingXr && UiInput.KeyboardBlocked)
        {
            if (useGravity)
            {
                controller.Move(new Vector3(0f, ReadVertical(), 0f) * Time.deltaTime);
            }

            return;
        }

        if (usingXr)
        {
            UnityEngine.XR.InputDevice leftHand =
                InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            moveInput = leftHand.isValid &&
                        leftHand.TryGetFeatureValue(
                            UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis)
                ? Vector2.ClampMagnitude(axis, 1f)
                : Vector2.zero;

            if (moveInput.sqrMagnitude < stickDeadzone * stickDeadzone)
            {
                moveInput = Vector2.zero;
            }

            speed = walkSpeed;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            moveInput = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
        }

        Vector3 velocity = ReadDirection(moveInput) * speed;
        velocity.y = ReadVertical();

        controller.Move(velocity * Time.deltaTime);
    }

    private Vector3 ReadDirection(Vector2 input)
    {
        if (input == Vector2.zero)
            return Vector3.zero;

        // Flatten the camera's facing so looking up or down does not
        // push the player into the floor or the ceiling.
        Vector3 forward = cameraTransform != null
            ? cameraTransform.forward
            : transform.forward;

        Vector3 right = cameraTransform != null
            ? cameraTransform.right
            : transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private float ReadVertical()
    {
        if (!useGravity)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return 0f;

            float v = 0f;

            if (keyboard.spaceKey.isPressed) v += verticalSpeed;
            if (keyboard.leftCtrlKey.isPressed) v -= verticalSpeed;

            return v;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        return verticalVelocity;
    }
}
