using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Look around in Play / Game view. Scene-view orbit does not work here.
///
///   Middle-mouse drag     - look (most reliable in the Unity editor on Mac)
///   Alt + left-drag       - look (same idea as Scene view)
///   Right-drag            - look (often stolen by the editor on macOS)
///   Left / Right arrows   - turn without the mouse
///
/// Yaw turns the body, pitch tilts the camera so the CharacterController stays upright.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Degrees turned per pixel of mouse movement.")]
    [SerializeField, Range(0.01f, 0.5f)] private float rotationSpeed = 0.12f;

    [Tooltip("Degrees per second when turning with the arrow keys.")]
    [SerializeField] private float keyboardTurnSpeed = 90f;

    [Tooltip("Largest mouse delta accepted in one frame. Stops the view snapping when the " +
             "window regains focus or a frame hitches.")]
    [SerializeField] private float maxDeltaPerFrame = 80f;

    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    [Tooltip("Hide and lock the cursor while looking, so a drag never runs out of screen.")]
    [SerializeField] private bool lockCursorWhileLooking = true;

    [Tooltip("Body that yaws. Left empty, the parent is used, or this transform if it has none.")]
    [SerializeField] private Transform body;

    private float yaw;
    private float pitch;
    private bool looking;
    private Vector2 cursorBeforeLook;

    private void Awake()
    {
        if (body == null)
        {
            body = transform.parent != null ? transform.parent : transform;
        }

        yaw = body.eulerAngles.y;

        float x = body == transform ? transform.eulerAngles.x : transform.localEulerAngles.x;
        pitch = x > 180f ? x - 360f : x;
    }

    private void Update()
    {
        if (UiInput.KeyboardBlocked)
        {
            return;
        }

        TurnWithKeyboard();

        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        bool wantLook = !UiInput.PointerOverUI && LookHeld(mouse);

        if (wantLook && !looking)
        {
            looking = true;
            cursorBeforeLook = mouse.position.ReadValue();

            if (lockCursorWhileLooking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        if (!wantLook && looking)
        {
            ReleaseCursor();
            return;
        }

        if (!looking)
        {
            return;
        }

        // One huge delta arrives when the window regains focus; unclamped it throws the
        // view across the room.
        Vector2 delta = Vector2.ClampMagnitude(mouse.delta.ReadValue(), maxDeltaPerFrame);

        yaw += delta.x * rotationSpeed;
        pitch -= delta.y * rotationSpeed;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Apply();
    }

    private static bool LookHeld(Mouse mouse)
    {
        Keyboard keyboard = Keyboard.current;
        bool alt = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

        return mouse.middleButton.isPressed
            || mouse.rightButton.isPressed
            || (alt && mouse.leftButton.isPressed);
    }

    private void TurnWithKeyboard()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        float turn = 0f;

        if (keyboard.leftArrowKey.isPressed) turn -= 1f;
        if (keyboard.rightArrowKey.isPressed) turn += 1f;

        if (Mathf.Approximately(turn, 0f))
        {
            return;
        }

        yaw += turn * keyboardTurnSpeed * Time.deltaTime;
        Apply();
    }

    private void Apply()
    {
        if (body == transform)
        {
            // No separate body: this transform has to carry both.
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            return;
        }

        body.rotation = Quaternion.Euler(0f, yaw, 0f);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Unlocking drops the pointer at the screen centre; put it back where the drag began.
        if (looking && Mouse.current != null)
        {
            Mouse.current.WarpCursorPosition(cursorBeforeLook);
        }

        looking = false;
    }

    /// <summary>Never leave the cursor captured if the window loses focus mid-drag.</summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            ReleaseCursor();
        }
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }
}
