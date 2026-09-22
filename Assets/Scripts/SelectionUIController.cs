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
    private MouseObjectSelector selector;
    

    private void Start()
    {
        selectionPanel.SetActive(false);
        loadingPopup.SetActive(false);

        if (generateButton != null)
        {
            generateButton.onClick.AddListener(OnGenerateClicked);
        }

        selector = FindFirstObjectByType<MouseObjectSelector>();
    }

    public void ShowObject(string objectName, Vector3 screenPosition)
    {
        isNewItem = false;

        Debug.Log("Selection UI: ShowObject called for " + objectName);

        selectionPanel.SetActive(true);
        
        // The panel stays where it is anchored. Moving it per selection made each step
        // of the flow appear somewhere different, which read as the UI jumping around.
        objectInfoText.text = "Selected: " + objectName;

        if (promptInputField != null)
        {
            promptInputField.text = "";
            TMP_Text placeholder = promptInputField.placeholder as TMP_Text;
            if (placeholder != null)
            {
                placeholder.text = "Describe the new look (e.g. red roses, oak wood)...";
            }
        }

        if (statusText != null)
        {
            statusText.text = "Status: Change Texture — the mesh stays, only the look changes.";
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

        selectionPanel.SetActive(false);

        AddItemFlow flow = AddItemFlow.Instance;

        if (flow == null)
        {
            // No flow in the scene: fall back to the old behaviour so the UI still responds.
            loadingPopup.SetActive(true);
            return;
        }

        if (isNewItem)
        {
            flow.GenerateImage(prompt);
            return;
        }

        ObjectInteraction selected = selector != null ? selector.Selected : null;

        if (selected == null)
        {
            if (statusText != null)
            {
                statusText.text = "Status: click an object in the room first.";
            }

            selectionPanel.SetActive(true);
            return;
        }

        flow.BeginTextureChange(prompt, selected);
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