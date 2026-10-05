using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hover and select furniture with the mouse, or with the Quest controller ray.
/// PointerSource decides which: mouse + left click on desktop, right controller +
/// trigger in the headset (B deselects, grip picks up the object you point at).
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
    private PointerSource pointer;

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

        pointer = PointerSource.Resolve();

        Transform canvas = FindUiCanvas();
        if (canvas != null)
        {
            VrKeyboard.Create(canvas);
            toolbar = SelectionToolbar.Create(canvas);
            toolbar.MoveClicked += MoveSelected;
            toolbar.ResizeClicked += ResizeSelected;
            toolbar.RotateClicked += RotateSelected;
            toolbar.DeleteClicked += DeleteSelected;
            toolbar.TextureClicked += OpenTexturePanel;
            toolbar.CloseClicked += Deselect;
        }
        else
        {
            Debug.LogWarning("MouseObjectSelector: no Canvas found; toolbar disabled.");
        }
    }

    private void Update()
    {
        if (pointer == null)
        {
            pointer = PointerSource.Resolve();
        }

        bool xr = pointer.UsingXr;

        if (!xr && (Mouse.current == null || mainCamera == null))
            return;

        // While something is being placed, the mouse belongs to the placement.
        // LastEndFrame: the click that dropped it must not also select something.
        if (placer != null && (placer.IsActive || placer.LastEndFrame == Time.frameCount))
        {
            ClearHover();
            return;
        }

        // Escape on desktop, B on the headset.
        if (xr ? pointer.CancelPressed
               : Keyboard.current != null && !UiInput.KeyboardBlocked &&
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

    /// <summary>Selects an object from code, for example a new item right after Accept.</summary>
    public void SelectObject(ObjectInteraction target)
    {
        if (target == null || RoomShell.IsFixed(target.gameObject.name))
            return;

        Select(target);
    }

    /// <summary>Removes the selected object from the room (toolbar Delete button).</summary>
    public void DeleteSelected()
    {
        ObjectInteraction target = selectedObject;

        if (target == null || (placer != null && placer.IsActive))
            return;

        if (hoveredObject == target)
        {
            hoveredObject = null;
        }

        Deselect();
        Destroy(target.gameObject);
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
        bool xr = pointer.UsingXr;

        // Don't hover while rotating the camera
        if (!xr && Mouse.current.rightButton.isPressed)
            return;

        ObjectInteraction interaction = null;

        if (pointer.TryGetRay(out Ray ray) &&
            Physics.Raycast(ray, out RaycastHit hit, rayDistance, selectableLayer))
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

    private void HandleSelection()
    {
        if (!pointer.SelectPressed)
            return;

        // Alt + left drag orbits the camera; that is not a selection click.
        Keyboard keyboard = Keyboard.current;
        if (!pointer.UsingXr && keyboard != null &&
            (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed))
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
            toolbar.ShowHint("Moving " + target.gameObject.name + ": " + Hint(
                "click to place  |  Q / E or scroll to rotate  |  + / - to resize  |  Esc to cancel",
                "trigger to place  |  X / Y to rotate  |  B to cancel"));
        }
    }

    private void OpenTexturePanel()
    {
        if (selectedObject == null || uiController == null)
            return;

        Vector3 screenPosition = mainCamera != null
            ? mainCamera.WorldToScreenPoint(selectedObject.transform.position)
            : Vector3.zero;
        uiController.ShowObject(selectedObject.gameObject.name, screenPosition);
    }

    /// <summary>Live size while resizing, e.g. "Height 42 cm (120%)".</summary>
    private void ShowSize(string size)
    {
        if (resizeHint == null || toolbar == null)
            return;

        toolbar.ShowHint(resizeHint + ": " + size + "  |  " + Hint(
            "scroll or + / -  |  click to keep  |  Esc to cancel",
            "Y bigger, X smaller  |  trigger to keep  |  B to cancel"));
    }

    /// <summary>Live angle while rotating, e.g. "Turned +30 degrees".</summary>
    private void ShowAngle(string angle)
    {
        if (rotateHint == null || toolbar == null)
            return;

        toolbar.ShowHint(rotateHint + ": " + angle + "  |  " + Hint(
            "scroll or Q / E (Shift = fine)  |  click to keep  |  Esc to cancel",
            "Y right, X left  |  trigger to keep  |  B to cancel"));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Desktop or headset wording for the toolbar hints.</summary>
    private string Hint(string desktop, string headset)
    {
        return pointer != null && pointer.UsingXr ? headset : desktop;
    }

    /// <summary>
    /// The canvas that holds the app's panels. A screen canvas on desktop, the
    /// world-space canvas in the headset; the toolbar works on both.
    /// </summary>
    private Transform FindUiCanvas()
    {
        Canvas fromPanel = uiController != null ? uiController.GetComponentInParent<Canvas>() : null;
        if (fromPanel != null)
        {
            return fromPanel.rootCanvas.transform;
        }

        Canvas worldCanvas = null;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas)
                continue;

            if (canvas.renderMode != RenderMode.WorldSpace)
                return canvas.transform;

            worldCanvas = canvas;
        }

        return worldCanvas != null ? worldCanvas.transform : null;
    }
}
