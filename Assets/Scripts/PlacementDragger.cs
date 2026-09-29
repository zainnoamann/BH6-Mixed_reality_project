using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves one object around the room: it follows the mouse and sits on top of
/// whatever flat surface is under the cursor (floor, table top, shelf, bed).
///
///   Mouse          - move (the object follows the cursor)
///   Left click     - drop it here (only when the preview is green)
///   Q / E, scroll  - rotate in 15 degree steps
///   Escape         - cancel
///
/// Preview: green = the spot is free, red = it overlaps other furniture.
///
/// Used for both:
///   - a new generated item (AddItemFlow). After the drop the session stays open
///     until Accept / Undo, and clicking the item again picks it up again.
///   - an existing object (Move on the selection toolbar, or the G key).
///     The drop ends the session; Escape puts it back where it was.
/// </summary>
public class PlacementDragger : MonoBehaviour
{
    [Tooltip("Clearance kept between the object's footprint and the walls, in metres.")]
    [SerializeField] private float wallMargin = 0.05f;

    [Tooltip("Degrees per Q / E press or scroll notch.")]
    [SerializeField] private float rotationStep = 15f;

    [Tooltip("How flat a surface must be to put things on it. 1 = perfectly flat.")]
    [SerializeField, Range(0.3f, 1f)] private float minSurfaceUp = 0.7f;

    [SerializeField] private float rayDistance = 200f;

    private enum Mode { NewItem, ExistingObject }

    /// <summary>A move session is open (the object is being placed).</summary>
    public bool IsActive { get; private set; }

    /// <summary>The object is attached to the cursor right now.</summary>
    public bool IsFollowing { get; private set; }

    /// <summary>The current spot overlaps other furniture.</summary>
    public bool Blocked { get; private set; }

    public Transform Target { get; private set; }

    /// <summary>Frame the last session ended, so the same click is not also read as a selection.</summary>
    public int LastEndFrame { get; private set; } = -1;

    private Mode mode;
    private Camera viewCamera;
    private ObjectInteraction interaction;
    private Action<bool> onEnd;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector2 lastMouse;
    private ObjectInteraction.PlacementState shownState = ObjectInteraction.PlacementState.None;

    private float floorY;
    private Bounds room;
    private bool roomKnown;

    // ------------------------------------------------------------------ public API

    /// <summary>New generated item: put it in view and attach it to the cursor.</summary>
    public void Begin(Transform target, string roomRootName)
    {
        if (!Open(target, roomRootName, Mode.NewItem, null))
        {
            return;
        }

        MoveBaseTo(SpawnPoint());
        startPosition = Target.position;
        startRotation = Target.rotation;
        UpdatePreview();
    }

    /// <summary>
    /// Existing object: attach it to the cursor. onEnd(true) after a drop,
    /// onEnd(false) after Escape (the object is back where it started).
    /// </summary>
    public bool BeginMove(Transform target, string roomRootName, Action<bool> onEnd)
    {
        if (IsActive)
        {
            return false;
        }

        if (!Open(target, roomRootName, Mode.ExistingObject, onEnd))
        {
            return false;
        }

        UpdatePreview();
        return true;
    }

    /// <summary>Keep the object where it is and close the session (Accept).</summary>
    public void Finish()
    {
        End(true);
    }

    /// <summary>Put the object back where the session started and close it (Undo / Escape).</summary>
    public void Cancel()
    {
        if (Target != null)
        {
            Target.SetPositionAndRotation(startPosition, startRotation);
        }

        End(false);
    }

    // ------------------------------------------------------------------ session

    private bool Open(Transform target, string roomRootName, Mode newMode, Action<bool> endCallback)
    {
        if (target == null)
        {
            return false;
        }

        if (IsActive)
        {
            // Close the previous session cleanly before starting a new one.
            End(true);
        }

        Target = target;
        mode = newMode;
        onEnd = endCallback;
        IsActive = true;
        IsFollowing = true;
        Blocked = false;

        viewCamera = Camera.main;
        roomKnown = RoomMetrics.TryMeasure(gameObject.scene, roomRootName, out room);
        floorY = roomKnown ? room.min.y : 0f;

        startPosition = target.position;
        startRotation = target.rotation;

        shownState = ObjectInteraction.PlacementState.None;
        interaction = target.GetComponent<ObjectInteraction>();
        if (interaction != null)
        {
            interaction.SetHover(false);
            interaction.SetSelected(false);
        }

        if (Mouse.current != null)
        {
            lastMouse = Mouse.current.position.ReadValue();
        }

        return true;
    }

    private void End(bool committed)
    {
        if (!IsActive)
        {
            return;
        }

        if (interaction != null)
        {
            interaction.SetPlacementState(ObjectInteraction.PlacementState.None);
        }

        shownState = ObjectInteraction.PlacementState.None;

        Action<bool> callback = onEnd;

        IsActive = false;
        IsFollowing = false;
        Blocked = false;
        Target = null;
        interaction = null;
        onEnd = null;
        LastEndFrame = Time.frameCount;

        callback?.Invoke(committed);
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (!IsActive)
        {
            return;
        }

        if (Target == null)
        {
            // The object was destroyed from outside (for example Undo).
            End(false);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && !UiInput.KeyboardBlocked)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (mode == Mode.ExistingObject)
                {
                    Cancel();
                    return;
                }

                // New item: just let go of it. Accept / Undo are still on screen.
                StopFollowing();
            }

            if (IsFollowing && keyboard.qKey.wasPressedThisFrame) Rotate(-1f);
            if (IsFollowing && keyboard.eKey.wasPressedThisFrame) Rotate(1f);
        }

        if (mouse == null || viewCamera == null)
        {
            return;
        }

        bool overUi = UiInput.PointerOverUI;

        if (IsFollowing && !overUi)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Rotate(Mathf.Sign(scroll));
            }

            // Only move once the mouse actually moves, so a new item does not jump
            // to wherever the cursor was resting when generation finished.
            Vector2 now = mouse.position.ReadValue();
            if ((now - lastMouse).sqrMagnitude > 1f)
            {
                lastMouse = now;

                if (TrySurfacePoint(now, out Vector3 point))
                {
                    MoveBaseTo(point);
                    UpdatePreview();
                }
            }
        }

        if (!mouse.leftButton.wasPressedThisFrame || overUi)
        {
            return;
        }

        if (IsFollowing)
        {
            if (Blocked)
            {
                return; // red: refuse the drop, keep following
            }

            if (mode == Mode.ExistingObject)
            {
                End(true);
            }
            else
            {
                StopFollowing();
            }

            return;
        }

        // New item already dropped: clicking it picks it up again.
        if (mode == Mode.NewItem && CursorOnTarget(mouse.position.ReadValue()))
        {
            IsFollowing = true;
            lastMouse = mouse.position.ReadValue();
            UpdatePreview();
        }
    }

    private void StopFollowing()
    {
        IsFollowing = false;

        if (interaction != null)
        {
            interaction.SetPlacementState(ObjectInteraction.PlacementState.None);
        }

        shownState = ObjectInteraction.PlacementState.None;
    }

    private void Rotate(float direction)
    {
        Target.Rotate(Vector3.up, direction * rotationStep, Space.World);

        // Rotation changes the footprint: keep it inside the room and re-check overlaps.
        Target.position = Contain(Target.position);
        UpdatePreview();
    }

    // ------------------------------------------------------------------ surfaces

    /// <summary>
    /// The first flat, upward-facing surface under the cursor, ignoring the object
    /// itself. A wall or other steep face falls back to the floor plane.
    /// </summary>
    private bool TrySurfacePoint(Vector2 screen, out Vector3 point)
    {
        point = default;

        Ray ray = viewCamera.ScreenPointToRay(screen);
        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(Target))
            {
                continue;
            }

            if (hit.normal.y >= minSurfaceUp)
            {
                point = hit.point;
                return true;
            }

            break; // a wall or the side of something: use the floor instead
        }

        Plane floor = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));

        // A camera with no pitch looks parallel to the floor and never intersects it.
        if (floor.Raycast(ray, out float distance) && distance < rayDistance)
        {
            point = ray.GetPoint(distance);
            return true;
        }

        return false;
    }

    /// <summary>Puts the bottom-centre of the object on the point, clamped inside the room.</summary>
    private void MoveBaseTo(Vector3 point)
    {
        if (!TryBounds(Target, out Bounds bounds))
        {
            Target.position = Contain(point);
            return;
        }

        // Follow the base of the bounding box rather than the pivot,
        // because imported models often have the pivot far off centre.
        Vector3 offset = Target.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Target.position = Contain(point + offset);
    }

    private bool CursorOnTarget(Vector2 screen)
    {
        Ray ray = viewCamera.ScreenPointToRay(screen);

        foreach (RaycastHit hit in Physics.RaycastAll(ray, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(Target))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ feedback

    private void UpdatePreview()
    {
        Blocked = IsFollowing && !IsPlacementFree();

        ObjectInteraction.PlacementState state =
            !IsFollowing ? ObjectInteraction.PlacementState.None :
            Blocked ? ObjectInteraction.PlacementState.Blocked :
            ObjectInteraction.PlacementState.Valid;

        // Re-tinting creates material copies, so only do it when the colour changes.
        if (interaction != null && state != shownState)
        {
            shownState = state;
            interaction.SetPlacementState(state);
        }
    }

    /// <summary>
    /// True when the object does not overlap other furniture. The test box is a
    /// little smaller and lifted, so resting on a table or carpet is not an overlap.
    /// </summary>
    private bool IsPlacementFree()
    {
        if (!TryBounds(Target, out Bounds bounds))
        {
            return true;
        }

        const float lift = 0.02f;
        Vector3 half = new Vector3(
            bounds.extents.x * 0.85f,
            Mathf.Max(0.005f, bounds.extents.y * 0.9f - lift),
            bounds.extents.z * 0.85f);
        Vector3 centre = bounds.center + Vector3.up * lift;

        Collider[] hits = Physics.OverlapBox(centre, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider other in hits)
        {
            if (other.transform.IsChildOf(Target))
            {
                continue;
            }

            ObjectInteraction owner = other.GetComponentInParent<ObjectInteraction>();

            if (owner == null || RoomShell.IsFixed(owner.gameObject.name))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ room

    /// <summary>
    /// First floor point for a new item: where the camera is looking.
    /// If that misses the room (camera outside or above it), use the middle of the room.
    /// </summary>
    private Vector3 SpawnPoint()
    {
        Vector3 point;

        if (viewCamera != null)
        {
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Plane floor = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));

            point = floor.Raycast(ray, out float distance) && distance < rayDistance
                ? ray.GetPoint(distance)
                : viewCamera.transform.position + viewCamera.transform.forward * 1.3f;
        }
        else
        {
            point = roomKnown ? room.center : Vector3.zero;
        }

        if (roomKnown && !InsideRoomXZ(point, 0.3f))
        {
            point = room.center;
        }

        point.y = floorY;

        return point;
    }

    private bool InsideRoomXZ(Vector3 point, float margin)
    {
        return point.x > room.min.x + margin && point.x < room.max.x - margin &&
               point.z > room.min.z + margin && point.z < room.max.z - margin;
    }

    /// <summary>Keeps the object's footprint inside the walls.</summary>
    private Vector3 Contain(Vector3 destination)
    {
        if (!roomKnown || Target == null)
        {
            return destination;
        }

        Vector3 half = Vector3.one * 0.1f;
        Vector3 offset = Vector3.zero;

        if (TryBounds(Target, out Bounds bounds))
        {
            half = bounds.extents;
            offset = bounds.center - Target.position;
        }

        float minX = room.min.x + half.x + wallMargin - offset.x;
        float maxX = room.max.x - half.x - wallMargin - offset.x;
        float minZ = room.min.z + half.z + wallMargin - offset.z;
        float maxZ = room.max.z - half.z - wallMargin - offset.z;

        destination.x = Mathf.Clamp(destination.x, minX, Mathf.Max(minX, maxX));
        destination.z = Mathf.Clamp(destination.z, minZ, Mathf.Max(minZ, maxZ));

        return destination;
    }

    private static bool TryBounds(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(target.position, Vector3.zero);

        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
}
