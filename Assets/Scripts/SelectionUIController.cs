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

    private void Start()
    {
        selectionPanel.SetActive(false);
        loadingPopup.SetActive(false);

        if (generateButton != null)
        {
            generateButton.onClick.AddListener(OnGenerateClicked);
        }
    }

    public void ShowObject(string objectName)
    {
        Debug.Log("Selection UI: ShowObject called for " + objectName);

        selectionPanel.SetActive(true);

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

        // Show the loading popup
        loadingPopup.SetActive(true);
    }

    public void HidePanel()
    {
        selectionPanel.SetActive(false);
    }
}