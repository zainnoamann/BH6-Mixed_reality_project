using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hover and select furniture with the mouse.
///
///   Click an object      - select it (light blue tint + toolbar at the bottom)
///   Click empty space    - deselect
///   Escape               - deselect
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

        Transform canvas = FindScreenCanvas();
        if (canvas != null)
        {
            toolbar = SelectionToolbar.Create(canvas);
            toolbar.MoveClicked += MoveSelected;
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

        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, selectableLayer))
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
                             ": click to place  |  Q / E or scroll to rotate  |  Esc to cancel");
        }
    }

    private void OpenTexturePanel()
    {
        if (selectedObject == null || uiController == null)
            return;

        Vector3 screenPosition = mainCamera.WorldToScreenPoint(selectedObject.transform.position);
        uiController.ShowObject(selectedObject.gameObject.name, screenPosition);
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
