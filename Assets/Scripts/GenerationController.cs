using UnityEngine;

public class GenerationController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject loadingPopup;

    private bool generationRunning = false;

    public void StartGeneration()
    {
        if (generationRunning)
            return;

        generationRunning = true;

        // Show loading UI
        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        Debug.Log("Generation started.");

        // TODO:
        // Connect this to the AI wrapper/backend later.
    }

    public void CancelGeneration()
    {
        Debug.Log("Generation cancelled.");
    
        generationRunning = false;
    
        if (loadingPopup != null)
        {
            loadingPopup.SetActive(false);
        }
    }
}