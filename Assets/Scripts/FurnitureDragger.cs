using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shortcut for moving furniture: point at an object (or select it) and press G,
/// or squeeze the grip button on the right Quest controller.
///
/// The moving itself is done by PlacementDragger, the same code the toolbar's Move
/// button and new generated items use, so every object moves the same way:
/// it follows the mouse, sits on floors and table tops, Q / E or scroll rotates,
/// click places it, Escape puts it back.
///
/// This component stays in the scene only for the G shortcut. The old floor-only
/// dragging that lived here was replaced by PlacementDragger.
/// </summary>
public class FurnitureDragger : MonoBehaviour
{
    [Tooltip("Assign the object holding MouseObjectSelector. Found automatically if empty.")]
    [SerializeField] private MouseObjectSelector selector;

    private void Start()
    {
        if (selector == null)
        {
            selector = FindFirstObjectByType<MouseObjectSelector>();
        }
    }

    private void Update()
    {
        // "a grey chair" typed in a prompt would otherwise trigger the G shortcut.
        if (UiInput.KeyboardBlocked || UiInput.PointerOverUI)
            return;

        if (selector == null)
            return;

        // G on the keyboard, or the grip button on the right Quest controller.
        PointerSource pointer = PointerSource.Resolve();

        if (pointer != null && pointer.GrabPressed)
        {
            selector.MoveHoveredOrSelected();
        }
    }
}
