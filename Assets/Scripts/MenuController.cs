using UnityEngine;

public class MenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private SelectionUIController selectionUIController;

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
}