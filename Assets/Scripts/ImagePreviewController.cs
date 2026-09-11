using UnityEngine;
using UnityEngine.UI;

public class ImagePreviewController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject imagePreviewPanel;
    [SerializeField] private GameObject loadingPopup;

    public void ShowPreview()
    {
        if (loadingPopup != null)
        {
            loadingPopup.SetActive(false);
        }

        if (imagePreviewPanel != null)
        {
            imagePreviewPanel.SetActive(true);
        }

        Debug.Log("Image preview shown.");
    }

    public void RegenerateImage()
    {
        Debug.Log("Image regeneration requested.");

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.RegenerateImage();
        }
    }

    public void Generate3D()
    {
        Debug.Log("3D generation requested.");

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.GenerateModel();
        }
    }

    public void CancelPreview()
    {
        if (imagePreviewPanel != null)
        {
            imagePreviewPanel.SetActive(false);
        }

        Debug.Log("Image preview cancelled.");

        if (AddItemFlow.Instance != null)
        {
            AddItemFlow.Instance.CancelPreview();
        }
    }
}