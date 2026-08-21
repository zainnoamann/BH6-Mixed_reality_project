using UnityEngine;
using UnityEngine.InputSystem;

public class MouseObjectSelector : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask selectableLayer;
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
        if (Mouse.current == null)
        {
            return;
        }

        HandleHover();
        HandleSelection();
    }

    private void HandleHover()
{
    Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

    if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, selectableLayer))
    {
        Debug.Log("Raycast hit mesh: " + hit.collider.name);

        // Find ObjectInteraction on the hit object OR one of its parents
        ObjectInteraction interaction = hit.collider.GetComponentInParent<ObjectInteraction>();

        if (interaction != null)
        {
            Debug.Log("Found parent object: " + interaction.gameObject.name);

            if (hoveredObject != interaction)
            {
                hoveredObject = interaction;
            }
        }
        else
        {
            Debug.Log("No ObjectInteraction found on parent.");
            hoveredObject = null;
        }
    }
    else
    {
        hoveredObject = null;
    }
}
    private void HandleSelection()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (hoveredObject == null)
        {
            return;
        }

        // Remove selection from previous object.
        if (selectedObject != null &&
            selectedObject != hoveredObject)
        {
            selectedObject.SetSelected(false);
        }

        // Select the new object.
        selectedObject = hoveredObject;

        selectedObject.SetHover(false);
        selectedObject.SetSelected(true);

        Debug.Log(
            "Object selected: " +
            selectedObject.gameObject.name
        );
    }
}
