using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SelectionUIController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private TMP_Text objectInfoText;
    [SerializeField] private TMP_InputField promptInputField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button generateButton;
    [SerializeField] private GameObject loadingPopup;
    [Header("Panel Positions")]
    [SerializeField] private Vector2 normalPanelPosition;
    [SerializeField] private Vector2 newItemPanelPosition;

    private bool isNewItem = false;
    

    private void Start()
    {
        selectionPanel.SetActive(false);
        loadingPopup.SetActive(false);

        if (generateButton != null)
        {
            generateButton.onClick.AddListener(OnGenerateClicked);
        }
    }

    public void ShowObject(string objectName, Vector3 screenPosition)
    {
        isNewItem = false;

        Debug.Log("Selection UI: ShowObject called for " + objectName);

        selectionPanel.SetActive(true);
        
        RectTransform panelRect =
            selectionPanel.GetComponent<RectTransform>();

        if (panelRect != null)
        {
            panelRect.position =
                    screenPosition +
                    new Vector3(
                        normalPanelPosition.x,
                        normalPanelPosition.y,
                        0
                    );
        }

        objectInfoText.text = "Selected: " + objectName;

        if (promptInputField != null)
        {
            promptInputField.text = "";
        }

        if (statusText != null)
        {
            statusText.text = "Status: Ready";
        }
    }

    private void OnGenerateClicked()
    {
        string prompt = promptInputField != null
            ? promptInputField.text
            : "";

        Debug.Log("Generate clicked. Prompt: " + prompt);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (statusText != null)
            {
                statusText.text = "Please enter a modification.";
            }

            return;
        }

        // Hide the selection UI
        // selectionPanel.SetActive(false);

        RectTransform selectionRect =
            selectionPanel.GetComponent<RectTransform>();

        RectTransform loadingRect =
            loadingPopup.GetComponent<RectTransform>();

        if (selectionRect != null && loadingRect != null)
        {
            loadingRect.position =
                selectionRect.position + new Vector3(125f, 0f, 0f);
        }

        // Show the loading popup
        loadingPopup.SetActive(true);
    }

    public void HidePanel()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }

        Debug.Log("Selection panel closed.");
    }

    public void ShowNewItem()
    {
        isNewItem = true;

        selectionPanel.SetActive(true);

        RectTransform panelRect = selectionPanel.GetComponent<RectTransform>();

        if (panelRect != null)
        {
            panelRect.anchoredPosition = newItemPanelPosition;
        }

        if (objectInfoText != null)
        {
            objectInfoText.text = "New Item";
        }

        if (promptInputField != null)
        {
            promptInputField.text = "";
            promptInputField.placeholder.GetComponent<TMP_Text>().text =
                "Describe what you want to create...";
        }

        if (statusText != null)
        {
            statusText.text = "Status: Ready";
        }
    }
}