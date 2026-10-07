using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hover and select furniture with the mouse.
///
///   Click an object      - select it (light blue tint + toolbar at the bottom)
///   Click empty space    - deselect
///   Escape               - deselect
///
/// Toolbar: Move (follows the mouse, sits on surfaces), Rotate (scroll or Q / E),
/// Resize (scroll or + / -), Change Texture, Close.
///
/// Selecting does nothing else. Move and Change Texture are buttons on the toolbar,
/// so the texture form no longer pops up on every click.
/// Walls, floor, windows and doors (RoomShell) cannot be hovered or selected.
/// </summary>
public class MouseObjectSelector : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask selectableLayer = ~0;
    [SerializeField] private float rayDistance = 100f;

    [Header("Room")]
    [Tooltip("Name of the room root, used to keep moved objects inside the walls.")]
    [SerializeField] private string roomRootName = "Model";

    private ObjectInteraction hoveredObject;
    private ObjectInteraction selectedObject;

    public ObjectInteraction Selected => selectedObject;

    [SerializeField] private SelectionUIController uiController;

    private SelectionToolbar toolbar;
    private PlacementDragger placer;
    private string resizeHint;
    private string rotateHint;

    private void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            Debug.LogError("Main Camera was not found.");
        }

        // AddItemFlow owns the PlacementDragger used for new items; share it, so only
        // one object can ever be moving at a time.
        placer = FindFirstObjectByType<PlacementDragger>();
        if (placer == null)
        {
            placer = gameObject.AddComponent<PlacementDragger>();
        }

        placer.SizeChanged += ShowSize;
        placer.AngleChanged += ShowAngle;

        Transform canvas = FindScreenCanvas();
        if (canvas != null)
        {
            toolbar = SelectionToolbar.Create(canvas);
            toolbar.MoveClicked += MoveSelected;
            toolbar.ResizeClicked += ResizeSelected;
            toolbar.RotateClicked += RotateSelected;
            toolbar.TextureClicked += OpenTexturePanel;
            toolbar.CloseClicked += Deselect;
        }
        else
        {
            Debug.LogWarning("MouseObjectSelector: no screen-space Canvas found; toolbar disabled.");
        }
    }

    private void Update()
    {
        if (Mouse.current == null || mainCamera == null)
            return;

        // While something is being placed, the mouse belongs to the placement.
        // LastEndFrame: the click that dropped it must not also select something.
        if (placer != null && (placer.IsActive || placer.LastEndFrame == Time.frameCount))
        {
            ClearHover();
            return;
        }

        if (Keyboard.current != null && !UiInput.KeyboardBlocked &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Deselect();
        }

        // A click that lands on a panel must not also hit the room behind it.
        if (UiInput.PointerOverUI)
        {
            ClearHover();
            return;
        }

        HandleHover();
        HandleSelection();
    }

    // ------------------------------------------------------------------ public actions

    /// <summary>Starts moving the selected object (toolbar Move button).</summary>
    public void MoveSelected()
    {
        StartMove(selectedObject);
    }

    /// <summary>Starts rotating the selected object in place (toolbar Rotate button).</summary>
    public void RotateSelected()
    {
        ObjectInteraction target = selectedObject;

        if (target == null || placer == null || placer.IsActive)
            return;

        if (uiController != null)
        {
            uiController.HideObjectPanel();
        }

        rotateHint = "Rotating " + target.gameObject.name;

        bool started = placer.BeginRotate(target.transform, roomRootName, committed =>
        {
            rotateHint = null;

            if (target != null)
            {
                Select(target);
            }
        });

        if (!started)
        {
            rotateHint = null;
        }
    }

    /// <summary>Starts resizing the selected object (toolbar Resize button).</summary>
    public void ResizeSelected()
    {
        ObjectInteraction target = selectedObject;

        if (target == null || placer == null || placer.IsActive)
            return;

        if (uiController != null)
        {
            uiController.HideObjectPanel();
        }

        resizeHint = "Resizing " + target.gameObject.name;

        bool started = placer.BeginResize(target.transform, roomRootName, committed =>
        {
            resizeHint = null;

            if (target != null)
            {
                Select(target);
            }
        });

        if (!started)
        {
            resizeHint = null;
        }
    }

    /// <summary>Selects and moves whatever is under the cursor, or the selection (G key).</summary>
    public void MoveHoveredOrSelected()
    {
        StartMove(hoveredObject != null ? hoveredObject : selectedObject);
    }

    public void Deselect()
    {
        if (selectedObject != null)
        {
            selectedObject.SetSelected(false);
        }

        selectedObject = null;

        if (toolbar != null)
        {
            toolbar.Hide();
        }

        if (uiController != null)
        {
            uiController.HideObjectPanel();
        }
    }

    // ------------------------------------------------------------------ hover / select

    private void ClearHover()
    {
        if (hoveredObject != null && hoveredObject != selectedObject)
        {
            hoveredObject.SetHover(false);
        }

        hoveredObject = null;
    }

    private void HandleHover()
    {
        // Don't hover while rotating the camera
        if (Mouse.current.rightButton.isPressed)
            return;

        ObjectInteraction interaction = null;

        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (RaycastClickable(ray, out RaycastHit hit))
        {
            interaction = hit.collider.GetComponentInParent<ObjectInteraction>();

            // The room shell is locked.
            if (interaction != null && RoomShell.IsFixed(interaction.gameObject.name))
            {
                interaction = null;
            }
        }

        if (interaction == hoveredObject)
            return;

        if (hoveredObject != null && hoveredObject != selectedObject)
        {
            hoveredObject.SetHover(false);
        }

        hoveredObject = interaction;

        if (hoveredObject != null && hoveredObject != selectedObject)
        {
            hoveredObject.SetHover(true);
        }
    }

    /// <summary>
    /// First thing under the ray that a click should land on. Glass in the room shell
    /// (windows, glass railings, the stair balustrade) is see-through, so the ray carries on
    /// to whatever is visible behind it; a solid wall still stops it. Glass on furniture,
    /// such as a glass table top, stays clickable.
    /// </summary>
    private bool RaycastClickable(Ray ray, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, selectableLayer, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            ObjectInteraction owner = hit.collider.GetComponentInParent<ObjectInteraction>();

            if (owner != null && RoomShell.IsFixed(owner.gameObject.name) && IsSeeThrough(hit))
                continue;

            result = hit;
            return true;
        }

        result = default;
        return false;
    }

    private static bool IsSeeThrough(RaycastHit hit)
    {
        MeshFilter filter = hit.collider.GetComponent<MeshFilter>();
        Renderer renderer = hit.collider.GetComponent<Renderer>();

        if (filter == null || filter.sharedMesh == null || renderer == null || hit.triangleIndex < 0)
            return false;

        // Both colliders InteractionSetup adds list their triangles in sub-mesh order, so the
        // hit triangle's sub-mesh, and with it its material, can be found by counting.
        Mesh mesh = filter.sharedMesh;
        Material[] materials = renderer.sharedMaterials;
        int first = 0;

        for (int i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
        {
            int count = (int)mesh.GetSubMesh(i).indexCount / 3;

            if (hit.triangleIndex < first + count)
                return IsTransparent(materials[i]);

            first += count;
        }

        return false;
    }

    private static bool IsTransparent(Material material)
    {
        if (material == null)
            return false;

        if (material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
            return true;

        Color colour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                     : material.HasProperty("_Color") ? material.color
                     : Color.white;

        return colour.a < 0.99f;
    }

    private void HandleSelection()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame)
            return;

        // Alt + left drag orbits the camera; that is not a selection click.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed))
            return;

        if (hoveredObject == null)
        {
            Deselect();
            return;
        }

        Select(hoveredObject);
    }

    private void Select(ObjectInteraction target)
    {
        if (selectedObject != null && selectedObject != target)
        {
            selectedObject.SetSelected(false);

            // The texture form belongs to the previous object.
            if (uiController != null)
            {
                uiController.HideObjectPanel();
            }
        }

        selectedObject = target;
        selectedObject.SetHover(false);
        selectedObject.SetSelected(true);

        Debug.Log("Selected: " + selectedObject.gameObject.name);

        if (toolbar != null)
        {
            toolbar.Show(selectedObject.gameObject.name);
        }
    }

    // ------------------------------------------------------------------ toolbar actions

    private void StartMove(ObjectInteraction target)
    {
        if (target == null || placer == null || placer.IsActive)
            return;

        if (RoomShell.IsFixed(target.gameObject.name))
            return;

        Select(target);

        if (uiController != null)
        {
            uiController.HideObjectPanel();
        }

        bool started = placer.BeginMove(target.transform, roomRootName, committed =>
        {
            // Moved (or put back): the object stays selected so you can keep editing it.
            if (target != null)
            {
                Select(target);
            }
        });

        if (started && toolbar != null)
        {
            toolbar.ShowHint("Moving " + target.gameObject.name +
                             ": click to place  |  Q / E or scroll to rotate  |  + / - to resize  |  Esc to cancel");
        }
    }

    private void OpenTexturePanel()
    {
        if (selectedObject == null || uiController == null)
            return;

        Vector3 screenPosition = mainCamera.WorldToScreenPoint(selectedObject.transform.position);
        uiController.ShowObject(selectedObject.gameObject.name, screenPosition);
    }

    /// <summary>Live size while resizing, e.g. "Height 42 cm (120%)".</summary>
    private void ShowSize(string size)
    {
        if (resizeHint == null || toolbar == null)
            return;

        toolbar.ShowHint(resizeHint + ": " + size +
                         "  |  scroll or + / -  |  click to keep  |  Esc to cancel");
    }

    /// <summary>Live angle while rotating, e.g. "Turned +30 degrees".</summary>
    private void ShowAngle(string angle)
    {
        if (rotateHint == null || toolbar == null)
            return;

        toolbar.ShowHint(rotateHint + ": " + angle +
                         "  |  scroll or Q / E (Shift = fine)  |  click to keep  |  Esc to cancel");
    }

    // ------------------------------------------------------------------ helpers

    private Transform FindScreenCanvas()
    {
        Canvas fromPanel = uiController != null ? uiController.GetComponentInParent<Canvas>() : null;
        if (fromPanel != null && fromPanel.rootCanvas.renderMode != RenderMode.WorldSpace)
        {
            return fromPanel.rootCanvas.transform;
        }

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
            {
                return canvas.transform;
            }
        }

        return null;
    }
}
