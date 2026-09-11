using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tells gameplay input when the UI owns the mouse or the keyboard.
///
/// The Input System reads devices directly, so it has no idea a panel is on screen or that
/// a text field has focus. Without these checks, clicking a button also clicks the room
/// behind it, and typing a prompt walks the player around.
/// </summary>
public static class UiInput
{
    /// <summary>True when the cursor is over any UI element that blocks raycasts.</summary>
    public static bool PointerOverUI =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>True while a text field has keyboard focus.</summary>
    public static bool Typing
    {
        get
        {
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                return false;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;

            return selected != null
                && selected.TryGetComponent(out TMP_InputField field)
                && field.isFocused;
        }
    }

    /// <summary>True when gameplay should ignore the mouse entirely this frame.</summary>
    public static bool MouseBlocked => PointerOverUI;

    /// <summary>True when gameplay should ignore the keyboard entirely this frame.</summary>
    public static bool KeyboardBlocked => Typing;
}
