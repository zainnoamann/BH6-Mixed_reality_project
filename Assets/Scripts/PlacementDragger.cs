using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Drags a newly generated object along the floor while it is being placed.
///
/// Placement is uncommitted: the caller decides afterwards whether to keep the object
/// (Accept) or throw it away (Undo / Regenerate). The drag itself is deliberately simple -
/// project the cursor onto the floor plane, move the object there, keep it inside the room.
/// </summary>
public class PlacementDragger : MonoBehaviour
{
    [Tooltip("Clearance kept between the object's footprint and the walls, in metres.")]
    [SerializeField] private float wallMargin = 0.05f;

    public bool IsActive { get; private set; }
    public Transform Target { get; private set; }

    private Camera viewCamera;
    private float floorY;
    private Vector3 grabOffset;
    private bool dragging;

    private Bounds room;
    private bool roomKnown;

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

        viewCamera = Camera.main;
        roomKnown = RoomMetrics.TryMeasure(gameObject.scene, roomRootName, out room);
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

    /// <summary>On the floor, a little in front of the camera, clamped inside the room.</summary>
    private Vector3 SpawnPoint()
    {
        float lift = Height(Target) * 0.5f;

        if (viewCamera == null)
        {
            return new Vector3(0f, floorY + lift, 0f);
        }

        Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;

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
        if (!IsActive || Target == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;

        if (mouse == null || viewCamera == null)
        {
            return;
        }

        // Clicks on the review panel must not start a drag.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (mouse.leftButton.wasPressedThisFrame && !overUI && TryFloorPoint(mouse, out Vector3 hit))
        {
            dragging = true;
            grabOffset = Target.position - hit;
        }

        if (dragging && mouse.leftButton.isPressed && TryFloorPoint(mouse, out Vector3 point))
        {
            Vector3 destination = point + grabOffset;
            destination.y = Target.position.y;

            Target.position = Contain(destination);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            dragging = false;
        }
    }

    private bool TryFloorPoint(Mouse mouse, out Vector3 point)
    {
        point = default;

        Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
        Plane floor = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));

        // A camera with no pitch looks parallel to the floor and never intersects it.
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

    private static float Height(Transform target)
    {
        return target != null && TryBounds(target, out Bounds bounds) ? bounds.size.y : 0.5f;
    }
}
