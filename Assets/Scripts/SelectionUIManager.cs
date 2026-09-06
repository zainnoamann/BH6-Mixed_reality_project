using TMPro;
using UnityEngine;

public class SelectionUIManager : MonoBehaviour
{
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private TMP_Text objectInfoText;

    private void Start()
    {
        selectionPanel.SetActive(false);
    }

    public void ShowObject(string objectName)
    {
        selectionPanel.SetActive(true);

        objectInfoText.text =
            "Selected Object\n\n" +
            objectName;
    }

    public void HidePanel()
    {
        selectionPanel.SetActive(false);
    }
}