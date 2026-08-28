using UnityEngine;
using UnityEngine.InputSystem;

public class MouseObjectSelector : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask selectableLayer = ~0;
    [SerializeField] private float rayDistance = 100f;

    private ObjectInteraction hoveredObject;
    private ObjectInteraction selectedObject;

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
    }

    private void Update()
    {
        if (Mouse.current == null || mainCamera == null)
            return;

        HandleHover();
        HandleSelection();
    }

    private void HandleHover()
    {   
        // Don't hover while rotating the camera
    if (Mouse.current.rightButton.isPressed)
        return;
        
        Ray ray = mainCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue()
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            rayDistance,
            selectableLayer))
        {
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

                    Debug.Log(
                        "Hovering: " +
                        hoveredObject.gameObject.name
                    );
                }

                return;
            }
        }

        if (hoveredObject != null &&
            hoveredObject != selectedObject)
        {
            hoveredObject.SetHover(false);
        }

        hoveredObject = null;
    }

    private void HandleSelection()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame)
            return;

        if (hoveredObject == null)
            return;

        if (selectedObject != null &&
            selectedObject != hoveredObject)
        {
            selectedObject.SetSelected(false);
        }

        selectedObject = hoveredObject;

        selectedObject.SetHover(false);
        selectedObject.SetSelected(true);

        Debug.Log(
            "Selected: " +
            selectedObject.gameObject.name
        );
    }
}
