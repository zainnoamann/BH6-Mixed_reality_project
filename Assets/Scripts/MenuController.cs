using UnityEngine;

public class MenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private SelectionUIController selectionUIController;
    [SerializeField] private RectTransform menuButtonRect; // drag MenuButton here

    public void OpenMenu()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }

        Debug.Log("Menu opened.");
    }

    public void CloseMenu()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        Debug.Log("Menu closed.");
    }

    // The Menu button should call this instead of OpenMenu
    public void ToggleMenu()
    {
        if (menuPanel == null) return;

        if (menuPanel.activeSelf)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    public void NewItem()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        if (selectionUIController != null)
        {
            selectionUIController.ShowNewItem();
        }

        Debug.Log("New Item selected.");
    }

    private void Update()
    {
        if (menuPanel == null || !menuPanel.activeSelf) return;

        PointerSource pointer = PointerSource.Resolve();
        if (!pointer.SelectPressed) return;
        if (!pointer.TryGetRay(out Ray ray)) return;

        bool overPanel = RayOver(menuPanel.GetComponent<RectTransform>(), ray);
        bool overButton = menuButtonRect != null && RayOver(menuButtonRect, ray);

        // Clicking the Menu button is handled by ToggleMenu, and clicking inside
        // the panel (New Item) must not close it. Anything else closes the menu.
        if (!overPanel && !overButton)
        {
            CloseMenu();
        }
    }

    private static bool RayOver(RectTransform rect, Ray ray)
    {
        Plane plane = new Plane(rect.forward, rect.position);
        if (!plane.Raycast(ray, out float distance)) return false;

        Vector2 local = rect.InverseTransformPoint(ray.GetPoint(distance));
        return rect.rect.Contains(local);
    }
}