using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Right-drag to look around: yaw turns the body, pitch tilts the camera.
///
/// The split matters. The body carries the CharacterController, and a capsule is always
/// aligned to its transform's up axis - so pitching the body would tip the capsule over and
/// physics would shove the player sideways. Yawing the body and pitching only the camera
/// keeps the capsule upright while giving a full 360 look.
///
/// Movement reads the camera's flattened forward, so turning here steers walking too.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Degrees turned per pixel of mouse movement.")]
    [SerializeField, Range(0.01f, 0.5f)] private float rotationSpeed = 0.12f;

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
        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame && !UiInput.PointerOverUI)
        {
            looking = true;
            cursorBeforeLook = mouse.position.ReadValue();

            if (lockCursorWhileLooking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        if (mouse.rightButton.wasReleasedThisFrame && looking)
        {
            ReleaseCursor();
        }

        if (!looking || !mouse.rightButton.isPressed)
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
