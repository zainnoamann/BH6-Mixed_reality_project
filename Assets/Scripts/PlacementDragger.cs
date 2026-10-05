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
///   + / -          - resize (grows from its base, so it stays standing on its surface)
///   Escape         - cancel
///
/// In the headset (PointerSource): the object follows the right controller ray,
/// trigger places it, A / X rotate (or resize in Resize mode), B cancels.
///
/// Preview: green = the spot is free, red = it overlaps other furniture.
///
/// Used for both:
///   - a new generated item (AddItemFlow). After the drop the session stays open
///     until Accept / Undo, and clicking the item again picks it up again.
///   - an existing object (Move on the selection toolbar, or the G key).
///     The drop ends the session; Escape puts it back where it was.
///   - Resize on the selection toolbar: scroll or + / - changes the size in place,
///     click (or Enter) keeps it, Escape restores the old size.
///   - Rotate on the selection toolbar: scroll or Q / E turns it in place
///     (hold Shift for fine 5 degree steps), click (or Enter) keeps it,
///     Escape restores the old angle.
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

    [Header("Resize")]
    [Tooltip("Size change per scroll notch or + / - press. 1.1 = 10%.")]
    [SerializeField] private float scaleStep = 1.1f;

    [Tooltip("Smallest and largest size, compared with the size when the session started.")]
    [SerializeField] private float minScale = 0.25f;
    [SerializeField] private float maxScale = 3f;

    [Tooltip("Degrees per step while Shift is held, for fine rotation.")]
    [SerializeField] private float fineRotationStep = 5f;

    private enum Mode { NewItem, ExistingObject, Resize, Rotate }

    /// <summary>Human-readable angle ("Turned +30 degrees"), sent whenever the rotation changes.</summary>
    public event Action<string> AngleChanged;

    private float turnedDegrees;

    /// <summary>Human-readable size ("Height 42 cm (120%)"), sent whenever the size changes.</summary>
    public event Action<string> SizeChanged;

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
    private Vector3 startScale;
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

    /// <summary>
    /// Existing object: change its size in place. onEnd(true) after a click or Enter,
    /// onEnd(false) after Escape (the old size is restored).
    /// </summary>
    public bool BeginResize(Transform target, string roomRootName, Action<bool> onEnd)
    {
        if (IsActive)
        {
            return false;
        }

        if (!Open(target, roomRootName, Mode.Resize, onEnd))
        {
            return false;
        }

        IsFollowing = false;
        UpdatePreview();
        ReportSize();
        return true;
    }

    /// <summary>
    /// Existing object: turn it in place. onEnd(true) after a click or Enter,
    /// onEnd(false) after Escape (the old angle is restored).
    /// </summary>
    public bool BeginRotate(Transform target, string roomRootName, Action<bool> onEnd)
    {
        if (IsActive)
        {
            return false;
        }

        if (!Open(target, roomRootName, Mode.Rotate, onEnd))
        {
            return false;
        }

        IsFollowing = false;
        UpdatePreview();
        ReportAngle();
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
            Target.localScale = startScale;
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
        startScale = target.localScale;
        turnedDegrees = 0f;

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

        PointerSource pointer = PointerSource.Resolve();

        if (pointer != null && pointer.UsingXr)
        {
            UpdateXr(pointer);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (mode == Mode.Resize)
        {
            UpdateResize(keyboard, mouse);
            return;
        }

        if (mode == Mode.Rotate)
        {
            UpdateRotate(keyboard, mouse);
            return;
        }

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
            if (IsFollowing && GrowPressed(keyboard)) Resize(1f);
            if (IsFollowing && ShrinkPressed(keyboard)) Resize(-1f);
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

    /// <summary>
    /// Headset version of the three modes. Same rules as the mouse: the ray moves the
    /// object, trigger confirms (refused while red), B cancels, A / X step.
    /// </summary>
    private void UpdateXr(PointerSource pointer)
    {
        if (pointer.CancelPressed)
        {
            if (mode != Mode.NewItem)
            {
                Cancel();
                return;
            }

            // New item: just let go of it. Accept / Undo are still on the panel.
            StopFollowing();
        }

        float step = pointer.StepUpPressed ? 1f : pointer.StepDownPressed ? -1f : 0f;

        if (step != 0f)
        {
            if (mode == Mode.Resize)
            {
                Resize(step);
            }
            else if (mode == Mode.Rotate || IsFollowing)
            {
                Rotate(step);
            }
        }

        bool overUi = UiInput.PointerOverUI;
        bool hasRay = pointer.TryGetRay(out Ray ray);

        if (IsFollowing && hasRay && !overUi && TrySurfacePoint(ray, out Vector3 point))
        {
            MoveBaseTo(point);
            UpdatePreview();
        }

        if (!pointer.SelectPressed || overUi)
        {
            return;
        }

        if (mode == Mode.Resize || mode == Mode.Rotate)
        {
            if (!Blocked)
            {
                End(true);
            }

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

        // New item already dropped: pointing at it and pulling the trigger picks it up again.
        if (mode == Mode.NewItem && hasRay && RayOnTarget(ray))
        {
            IsFollowing = true;
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
        Keyboard keyboard = Keyboard.current;
        bool fine = keyboard != null && keyboard.shiftKey.isPressed;
        float degrees = direction * (fine ? fineRotationStep : rotationStep);

        // Turn around the middle of the object, not its pivot: imported models often
        // have the pivot in a corner, and turning around that swings the whole object away.
        if (TryBounds(Target, out Bounds bounds))
        {
            Vector3 centre = new Vector3(bounds.center.x, Target.position.y, bounds.center.z);
            Target.RotateAround(centre, Vector3.up, degrees);
        }
        else
        {
            Target.Rotate(Vector3.up, degrees, Space.World);
        }

        turnedDegrees = Mathf.Repeat(turnedDegrees + degrees + 180f, 360f) - 180f;

        // Rotation changes the footprint: keep it inside the room and re-check overlaps.
        Target.position = Contain(Target.position);
        UpdatePreview();
        ReportAngle();
    }

    private void UpdateRotate(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard != null && !UiInput.KeyboardBlocked)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            if (keyboard.qKey.wasPressedThisFrame) Rotate(-1f);
            if (keyboard.eKey.wasPressedThisFrame) Rotate(1f);

            if (keyboard.enterKey.wasPressedThisFrame && !Blocked)
            {
                End(true);
                return;
            }
        }

        if (mouse == null || UiInput.PointerOverUI)
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            Rotate(Mathf.Sign(scroll));
        }

        if (mouse.leftButton.wasPressedThisFrame && !Blocked)
        {
            End(true);
        }
    }

    private void ReportAngle()
    {
        if (AngleChanged == null)
        {
            return;
        }

        int turned = Mathf.RoundToInt(turnedDegrees);
        AngleChanged.Invoke("Turned " + (turned > 0 ? "+" : "") + turned + " degrees");
    }

    // ------------------------------------------------------------------ resize

    private void UpdateResize(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard != null && !UiInput.KeyboardBlocked)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            if (GrowPressed(keyboard)) Resize(1f);
            if (ShrinkPressed(keyboard)) Resize(-1f);

            if (keyboard.enterKey.wasPressedThisFrame && !Blocked)
            {
                End(true);
                return;
            }
        }

        if (mouse == null || UiInput.PointerOverUI)
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            Resize(Mathf.Sign(scroll));
        }

        if (mouse.leftButton.wasPressedThisFrame && !Blocked)
        {
            End(true);
        }
    }

    private static bool GrowPressed(Keyboard keyboard)
    {
        return keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame;
    }

    private static bool ShrinkPressed(Keyboard keyboard)
    {
        return keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame;
    }

    /// <summary>One step bigger (+1) or smaller (-1), keeping the base on its surface.</summary>
    private void Resize(float direction)
    {
        float ratio = CurrentRatio();
        float wanted = Mathf.Clamp(
            direction > 0f ? ratio * scaleStep : ratio / scaleStep, minScale, maxScale);

        if (Mathf.Approximately(wanted, ratio) || !TryBounds(Target, out Bounds before))
        {
            return;
        }

        Vector3 basePoint = new Vector3(before.center.x, before.min.y, before.center.z);

        Target.localScale = startScale * wanted;

        // Scaling happens around the pivot; move it back so the base stays where it was.
        if (TryBounds(Target, out Bounds after))
        {
            Vector3 newBase = new Vector3(after.center.x, after.min.y, after.center.z);
            Target.position += basePoint - newBase;
        }

        Target.position = Contain(Target.position);
        UpdatePreview();
        ReportSize();
    }

    private float CurrentRatio()
    {
        return Mathf.Abs(startScale.x) > 1e-6f ? Target.localScale.x / startScale.x : 1f;
    }

    private void ReportSize()
    {
        if (SizeChanged == null || Target == null || !TryBounds(Target, out Bounds bounds))
        {
            return;
        }

        float ceiling = roomKnown ? room.size.y : 2.6f;
        float metres = bounds.size.y / TypicalSizes.UnitsPerMetre(ceiling);

        SizeChanged.Invoke("Height " + Mathf.RoundToInt(metres * 100f) + " cm (" +
                           Mathf.RoundToInt(CurrentRatio() * 100f) + "%)");
    }

    // ------------------------------------------------------------------ surfaces

    /// <summary>
    /// The first flat, upward-facing surface under the cursor, ignoring the object
    /// itself. A wall or other steep face falls back to the floor plane.
    /// </summary>
    private bool TrySurfacePoint(Vector2 screen, out Vector3 point)
    {
        return TrySurfacePoint(viewCamera.ScreenPointToRay(screen), out point);
    }

    private bool TrySurfacePoint(Ray ray, out Vector3 point)
    {
        point = default;

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
        return RayOnTarget(viewCamera.ScreenPointToRay(screen));
    }

    private bool RayOnTarget(Ray ray)
    {
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
        bool previewing = IsFollowing || mode == Mode.Resize || mode == Mode.Rotate;
        Blocked = previewing && !IsPlacementFree();

        ObjectInteraction.PlacementState state =
            !previewing ? ObjectInteraction.PlacementState.None :
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

        PointerSource pointer = PointerSource.Resolve();

        if (viewCamera != null && pointer != null && pointer.UsingXr)
        {
            // Headset: the user looks straight ahead at the menu, so "where the camera
            // looks on the floor" is far away. Put it on the floor 1.5 m in front instead,
            // slightly to the right so the menu panel does not hide it.
            Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            point = viewCamera.transform.position + forward * 1.5f + right * 0.4f;
            point.y = floorY;

            // Outside the walls: Contain() in MoveBaseTo pulls it back to the nearest spot inside.
            return point;
        }

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
