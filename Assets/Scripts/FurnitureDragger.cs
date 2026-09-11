using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pick furniture up and slide it along the floor.
///
///   G       - pick up whatever is under the cursor, press again to place
///   Mouse   - slide it along the floor
///   Q / E   - rotate
///   Escape  - cancel and put it back
///
/// Walls, floors, ceilings, windows and doors are refused, so the room shell
/// cannot be dragged around by accident.
/// </summary>

public class FurnitureDragger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the camera the player looks through.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Assign the object holding MouseObjectSelector.")]
    [SerializeField] private MouseObjectSelector selector;

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

    private Renderer[] heldRenderers;
    private Material[][] heldMaterials;
    private Material blockedMaterial;

    public bool IsDragging => held != null;

    private void Awake()
    {
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
        // "a grey chair" would otherwise trigger the G pickup shortcut.
        if (UiInput.KeyboardBlocked || UiInput.PointerOverUI)
            return;

        if (Keyboard.current == null || Mouse.current == null)
            return;

        if (held == null)
        {
            if (Keyboard.current.gKey.wasPressedThisFrame)
            {
                TryPickUp();
            }
            return;
        }

        Slide();

        if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            held.Rotate(Vector3.up, -rotationStep, Space.World);
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            held.Rotate(Vector3.up, rotationStep, Space.World);
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            held.SetPositionAndRotation(startPosition, startRotation);
            Release();
            return;
        }

        if (Keyboard.current.gKey.wasPressedThisFrame ||
            Keyboard.current.enterKey.wasPressedThisFrame)
        {
            if (blocked)
            {
                Debug.Log("That spot is blocked. Move it, or press Escape.");
                return;
            }

            Release();
        }
    }

    // ------------------------------------------------------------------

    private void TryPickUp()
    {
        Ray ray = viewCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance))
        {
            Debug.Log("Point at a piece of furniture, then press G.");
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
        Ray ray = viewCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue());

        Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, baseY, 0f));

        if (floorPlane.Raycast(ray, out float distance))
        {
            Vector3 target = ray.GetPoint(distance);
            Bounds bounds = GetBounds();

            // Follow the base of the bounding box rather than the pivot,
            // because imported models often have the pivot far off centre.
            Vector3 offset = held.position - new Vector3(
                bounds.center.x, bounds.min.y, bounds.center.z);

            held.position = target + offset;
        }

        bool nowBlocked = !IsPlacementValid();

        if (nowBlocked != blocked)
        {
            blocked = nowBlocked;
            ShowBlocked(blocked);
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
