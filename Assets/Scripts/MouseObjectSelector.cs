using UnityEngine;

/// <summary>
/// Hover and select, driven by whatever pointer is active.
///
/// The class name is unchanged so existing scene references keep working, but it is no
/// longer mouse specific. On desktop the ray comes from the cursor. On the headset it
/// comes out of the right controller and select is the trigger.
/// </summary>
public class MouseObjectSelector : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask selectableLayer = ~0;
    [SerializeField] private float rayDistance = 100f;

    [SerializeField] private SelectionUIController uiController;

    [Header("XR")]
    [Tooltip("Leave empty to find the one in the scene.")]
    [SerializeField] private PointerSource pointer;

    [Tooltip("Optional. A line renderer on the controller, shown as the aim ray.")]
    [SerializeField] private LineRenderer aimLine;

    private ObjectInteraction hoveredObject;
    private ObjectInteraction selectedObject;

    /// <summary>The current selection, so other scripts can read it.</summary>
    public ObjectInteraction SelectedObject => selectedObject;

    private void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (pointer == null)
        {
            pointer = GetComponent<PointerSource>();
        }

        if (pointer == null)
            pointer = PointerSource.Resolve();

        if (pointer == null)
            pointer = gameObject.AddComponent<PointerSource>();

        pointer.Configure(mainCamera);
    }

    private void Update()
    {
        if (pointer == null)
            return;

        // A click that lands on a panel must not also hit the room behind it.
        if (UiInput.PointerOverUI)
        {
            ClearHover();
            DrawAim(null, Vector3.zero);
            return;
        }

        HandleHover();
        HandleSelection();
    }

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
        // On desktop, do not hover while the right button is turning the camera.
        if (!pointer.UsingXr &&
            UnityEngine.InputSystem.Mouse.current != null &&
            UnityEngine.InputSystem.Mouse.current.rightButton.isPressed)
        {
            return;
        }

        if (!pointer.TryGetRay(out Ray ray))
            return;

        if (Physics.Raycast(ray, out RaycastHit hit,
                            rayDistance, selectableLayer))
        {
            DrawAim(ray, hit.point);

            ObjectInteraction interaction =
                hit.collider.GetComponentInParent<ObjectInteraction>();

            if (interaction != null)
            {
                if (hoveredObject != interaction)
                {
                    if (hoveredObject != null &&
                        hoveredObject != selectedObject)
                    {
                        hoveredObject.SetHover(false);
                    }

                    hoveredObject = interaction;

                    if (hoveredObject != selectedObject)
                    {
                        hoveredObject.SetHover(true);
                    }
                }

                return;
            }
        }
        else
        {
            DrawAim(ray, ray.origin + ray.direction * rayDistance);
        }

        if (hoveredObject != null && hoveredObject != selectedObject)
        {
            hoveredObject.SetHover(false);
        }

        hoveredObject = null;
    }

    private void HandleSelection()
    {
        if (!pointer.SelectPressed)
            return;

        if (hoveredObject == null)
            return;

        if (selectedObject != null && selectedObject != hoveredObject)
        {
            selectedObject.SetSelected(false);
        }

        selectedObject = hoveredObject;

        selectedObject.SetHover(false);
        selectedObject.SetSelected(true);

        Debug.Log("Selected: " + selectedObject.gameObject.name);

        if (uiController != null)
        {
            // ShowObject ignores this value, the panel stays anchored,
            // but the signature is kept so nothing else has to change.
            Vector3 screenPosition = mainCamera != null
                ? mainCamera.WorldToScreenPoint(
                    selectedObject.transform.position)
                : Vector3.zero;

            uiController.ShowObject(
                selectedObject.gameObject.name, screenPosition);
        }
    }

    /// <summary>Draws the controller aim ray so the user can see where they point.</summary>
    private void DrawAim(Ray? ray, Vector3 end)
    {
        if (aimLine == null)
            return;

        if (ray == null || !pointer.UsingXr)
        {
            aimLine.enabled = false;
            return;
        }

        aimLine.enabled = true;
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, ray.Value.origin);
        aimLine.SetPosition(1, end);
    }
}
