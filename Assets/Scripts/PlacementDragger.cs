using UnityEngine;

/// <summary>
/// Drags a newly generated object along the floor while it is being placed.
///
/// Placement is uncommitted: the caller decides afterwards whether to keep the object
/// (Accept) or throw it away (Undo / Regenerate).
///
/// Works with either pointer. On desktop, hold the left button and move the mouse.
/// On the headset, hold the trigger and move the controller, and twist your wrist to
/// turn the object. Wrist twist is used rather than the thumbstick because the
/// thumbstick already drives movement and snap turn on the XR rig.
/// </summary>
public class PlacementDragger : MonoBehaviour
{
    [Tooltip("Clearance kept between the object's footprint and the walls, in metres.")]
    [SerializeField] private float wallMargin = 0.05f;

    [Tooltip("Leave empty to find the one in the scene.")]
    [SerializeField] private PointerSource pointer;

    [Tooltip("Turn the object by twisting the controller while dragging.")]
    [SerializeField] private bool wristRotation = true;

    public bool IsActive { get; private set; }
    public Transform Target { get; private set; }

    private Camera viewCamera;
    private float floorY;
    private Vector3 grabOffset;
    private bool dragging;

    private float handYawAtGrab;
    private float targetYawAtGrab;

    private Bounds room;
    private bool roomKnown;

    private void Awake()
    {
        if (pointer == null)
        {
            pointer = PointerSource.Resolve();
        }
    }

    /// <summary>Puts the object in front of the player and starts accepting drags.</summary>
    public void Begin(Transform target, string roomRootName)
    {
        Target = target;
        IsActive = target != null;
        dragging = false;

        if (!IsActive)
        {
            return;
        }

        if (pointer == null)
        {
            pointer = PointerSource.Resolve();
        }

        viewCamera = pointer != null ? pointer.ViewCamera : Camera.main;

        roomKnown = RoomMetrics.TryMeasure(
            gameObject.scene, roomRootName, out room);

        floorY = roomKnown ? room.min.y : 0f;

        target.position = SpawnPoint();
    }

    public void Finish()
    {
        IsActive = false;
        dragging = false;
        Target = null;
    }

    public void Cancel()
    {
        Finish();
    }

    /// <summary>On the floor, a little in front of the viewer, clamped inside the room.</summary>
    private Vector3 SpawnPoint()
    {
        float lift = Height(Target) * 0.5f;

        if (viewCamera == null)
        {
            return new Vector3(0f, floorY + lift, 0f);
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            viewCamera.transform.forward, Vector3.up).normalized;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        Vector3 point = viewCamera.transform.position + forward * 1.3f;
        point.y = floorY + lift;

        return Contain(point);
    }

    private void Update()
    {
        if (!IsActive || Target == null || pointer == null)
        {
            return;
        }

        // Clicks on the review panel must not start a drag.
        bool overUI = UiInput.PointerOverUI;

        if (pointer.SelectPressed && !overUI &&
            TryFloorPoint(out Vector3 hit))
        {
            dragging = true;
            grabOffset = Target.position - hit;

            if (pointer.UsingXr && pointer.Hand != null)
            {
                handYawAtGrab = pointer.Hand.eulerAngles.y;
                targetYawAtGrab = Target.eulerAngles.y;
            }
        }

        if (dragging && pointer.SelectHeld &&
            TryFloorPoint(out Vector3 point))
        {
            Vector3 destination = point + grabOffset;
            destination.y = Target.position.y;

            Target.position = Contain(destination);

            ApplyWristRotation();
        }

        if (pointer.SelectReleased)
        {
            dragging = false;
        }
    }

    /// <summary>Turns the object as the controller is twisted.</summary>
    private void ApplyWristRotation()
    {
        if (!wristRotation || !pointer.UsingXr || pointer.Hand == null)
        {
            return;
        }

        float delta = Mathf.DeltaAngle(
            handYawAtGrab, pointer.Hand.eulerAngles.y);

        Vector3 angles = Target.eulerAngles;
        angles.y = targetYawAtGrab + delta;

        Target.eulerAngles = angles;
    }

    private bool TryFloorPoint(out Vector3 point)
    {
        point = default;

        if (!pointer.TryGetRay(out Ray ray))
        {
            return false;
        }

        Plane floor = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));

        // A ray parallel to the floor never intersects it.
        if (!floor.Raycast(ray, out float distance) || distance > 500f)
        {
            return false;
        }

        point = ray.GetPoint(distance);

        return true;
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

        destination.x = Mathf.Clamp(
            destination.x, minX, Mathf.Max(minX, maxX));

        destination.z = Mathf.Clamp(
            destination.z, minZ, Mathf.Max(minZ, maxZ));

        return destination;
    }

    private static bool TryBounds(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(target.position, Vector3.zero);

        bool found = false;

        foreach (Renderer renderer in
                 target.GetComponentsInChildren<Renderer>(true))
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

    private static float Height(Transform target)
    {
        return target != null && TryBounds(target, out Bounds bounds)
            ? bounds.size.y
            : 0.5f;
    }
}
