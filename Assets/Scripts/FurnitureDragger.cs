using UnityEngine;

/// <summary>
/// Picks up existing furniture and slides it along the floor.
///
/// Desktop: point at a piece, press G, move the mouse, press G again to place,
/// Q and E to rotate, Escape to cancel.
///
/// Headset: point at a piece, squeeze the grip, move the controller, release to place.
/// Twisting your wrist turns the object. The B button cancels.
///
/// Walls, floors, ceilings, windows and doors are refused, so the room shell cannot be
/// dragged around by accident.
/// </summary>
public class FurnitureDragger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private MouseObjectSelector selector;

    [Tooltip("Leave empty to find the one in the scene.")]
    [SerializeField] private PointerSource pointer;

    [Header("Placement")]
    [SerializeField] private float rotationStep = 15f;
    [SerializeField] private float rayDistance = 100f;

    [Tooltip("Optional. Assign the floor so furniture cannot leave the room.")]
    [SerializeField] private Transform floorObject;

    [Header("Feedback")]
    [SerializeField] private Color blockedColour = new Color(1f, 0.3f, 0.3f);

    private static readonly string[] FixedParts =
    {
        "wall", "floor", "ceiling", "window", "door", "roof"
    };

    private Transform held;
    private ObjectInteraction heldInteraction;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float baseY;
    private bool blocked;

    private float handYawAtGrab;
    private float targetYawAtGrab;

    private Renderer[] heldRenderers;
    private Material[][] heldMaterials;
    private Material blockedMaterial;

    public bool IsDragging => held != null;

    private void Awake()
    {
        if (pointer == null)
        {
            pointer = PointerSource.Resolve();
        }

        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");

        blockedMaterial = new Material(shader) { color = blockedColour };
    }

    private void Update()
    {
        if (pointer == null)
            return;

        if (held == null)
        {
            if (pointer.GrabPressed && !UiInput.PointerOverUI)
            {
                TryPickUp();
            }

            return;
        }

        Slide();
        HandleRotation();

        if (pointer.CancelPressed)
        {
            held.SetPositionAndRotation(startPosition, startRotation);
            Release();
            return;
        }

        // On the headset, releasing the grip places the object.
        // On desktop, pressing G again places it.
        bool placeNow = pointer.UsingXr
            ? pointer.GrabReleased
            : pointer.GrabPressed;

        if (placeNow)
        {
            if (blocked)
            {
                Debug.Log("That spot is blocked. Move it, or cancel.");

                if (pointer.UsingXr)
                {
                    // Do not strand the object in a bad spot on release.
                    held.SetPositionAndRotation(startPosition, startRotation);
                    Release();
                }

                return;
            }

            Release();
        }
    }

    // ------------------------------------------------------------------

    private void TryPickUp()
    {
        if (!pointer.TryGetRay(out Ray ray))
            return;

        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance))
        {
            Debug.Log("Point at a piece of furniture first.");
            return;
        }

        ObjectInteraction interaction =
            hit.collider.GetComponentInParent<ObjectInteraction>();

        if (interaction == null)
        {
            Debug.Log("That is not a selectable object.");
            return;
        }

        if (IsFixed(interaction.gameObject.name))
        {
            Debug.Log($"{interaction.gameObject.name} is part of the room "
                      + "and cannot be moved.");
            return;
        }

        held = interaction.transform;
        heldInteraction = interaction;

        startPosition = held.position;
        startRotation = held.rotation;
        baseY = GetBounds().min.y;

        if (pointer.UsingXr && pointer.Hand != null)
        {
            handYawAtGrab = pointer.Hand.eulerAngles.y;
            targetYawAtGrab = held.eulerAngles.y;
        }

        // Let the selector restore its own materials before we cache them.
        heldInteraction.SetHover(false);
        heldInteraction.SetSelected(false);

        CacheMaterials();

        if (selector != null)
        {
            selector.enabled = false;
        }
    }

    private void Slide()
    {
        if (pointer.TryGetRay(out Ray ray))
        {
            Plane floorPlane =
                new Plane(Vector3.up, new Vector3(0f, baseY, 0f));

            if (floorPlane.Raycast(ray, out float distance) &&
                distance < 500f)
            {
                Vector3 target = ray.GetPoint(distance);
                Bounds bounds = GetBounds();

                // Follow the base of the bounding box rather than the pivot,
                // because imported models often have the pivot far off centre.
                Vector3 offset = held.position - new Vector3(
                    bounds.center.x, bounds.min.y, bounds.center.z);

                held.position = target + offset;
            }
        }

        bool nowBlocked = !IsPlacementValid();

        if (nowBlocked != blocked)
        {
            blocked = nowBlocked;
            ShowBlocked(blocked);
        }
    }

    private void HandleRotation()
    {
        if (pointer.UsingXr)
        {
            if (pointer.Hand == null)
                return;

            float delta = Mathf.DeltaAngle(
                handYawAtGrab, pointer.Hand.eulerAngles.y);

            Vector3 angles = held.eulerAngles;
            angles.y = targetYawAtGrab + delta;

            held.eulerAngles = angles;
            return;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.qKey.wasPressedThisFrame)
        {
            held.Rotate(Vector3.up, -rotationStep, Space.World);
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            held.Rotate(Vector3.up, rotationStep, Space.World);
        }
    }

    private void Release()
    {
        ShowBlocked(false);

        if (selector != null)
        {
            selector.enabled = true;
        }

        if (heldInteraction != null)
        {
            heldInteraction.SetSelected(true);
        }

        held = null;
        heldInteraction = null;
        heldRenderers = null;
        heldMaterials = null;
        blocked = false;
    }

    // ------------------------------------------------------------------

    /// <summary>Fits in the room, and does not overlap other furniture.</summary>
    private bool IsPlacementValid()
    {
        Bounds bounds = GetBounds();

        if (floorObject != null)
        {
            Renderer floorRenderer =
                floorObject.GetComponentInChildren<Renderer>();

            if (floorRenderer != null)
            {
                Bounds room = floorRenderer.bounds;

                if (bounds.min.x < room.min.x || bounds.max.x > room.max.x ||
                    bounds.min.z < room.min.z || bounds.max.z > room.max.z)
                {
                    return false;
                }
            }
        }

        Collider[] hits = Physics.OverlapBox(
            bounds.center,
            bounds.extents * 0.9f,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);

        foreach (Collider other in hits)
        {
            ObjectInteraction owner =
                other.GetComponentInParent<ObjectInteraction>();

            if (owner == null || owner.transform == held)
                continue;

            if (!IsFixed(owner.gameObject.name))
                return false;
        }

        return true;
    }

    private Bounds GetBounds()
    {
        Renderer[] renderers = held.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(held.position, Vector3.one * 0.1f);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static bool IsFixed(string objectName)
    {
        string lower = objectName.ToLowerInvariant();

        foreach (string part in FixedParts)
        {
            if (lower.Contains(part))
                return true;
        }

        return false;
    }

    private void CacheMaterials()
    {
        heldRenderers = held.GetComponentsInChildren<Renderer>(true);
        heldMaterials = new Material[heldRenderers.Length][];

        for (int i = 0; i < heldRenderers.Length; i++)
        {
            heldMaterials[i] = heldRenderers[i].sharedMaterials;
        }
    }

    private void ShowBlocked(bool show)
    {
        if (heldRenderers == null)
            return;

        for (int i = 0; i < heldRenderers.Length; i++)
        {
            if (!show)
            {
                heldRenderers[i].sharedMaterials = heldMaterials[i];
                continue;
            }

            int slots = Mathf.Max(1, heldMaterials[i].Length);
            Material[] filled = new Material[slots];

            for (int s = 0; s < slots; s++)
            {
                filled[s] = blockedMaterial;
            }

            heldRenderers[i].sharedMaterials = filled;
        }
    }
}
