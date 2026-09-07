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

        // TODO:
        // Keep the generated result once AI/backend integration is available.
    }

    public void UndoGeneration()
    {
        Debug.Log("Generation undone.");

        if (reviewPanel != null)
        {
            reviewPanel.SetActive(false);
        }

        // TODO:
        // Restore the previous object state once generation is integrated.
    }

    public void Regenerate()
    {
        Debug.Log("Regeneration requested.");

        if (reviewPanel != null)
        {
            reviewPanel.SetActive(false);
        }

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        // TODO:
        // Trigger a new AI generation request once the backend is connected.
    }
}