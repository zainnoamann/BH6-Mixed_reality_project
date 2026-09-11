using UnityEngine;

public class GenerationReviewController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject reviewPanel;
    [SerializeField] private GameObject loadingPopup;

    public void AcceptGeneration()
    {
        Debug.Log("Generation accepted.");

        if (reviewPanel != null)
        {
            reviewPanel.SetActive(false);
        }

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.AcceptResult();
        }
    }

    public void UndoGeneration()
    {
        Debug.Log("Generation undone.");

        if (reviewPanel != null)
        {
            reviewPanel.SetActive(false);
        }

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.UndoResult();
        }
    }

    public void Regenerate()
    {
        Debug.Log("Regeneration requested.");

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.RegenerateResult();
        }
    }
}