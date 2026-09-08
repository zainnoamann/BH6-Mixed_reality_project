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
        if (imagePreviewPanel != null)
        {
            imagePreviewPanel.SetActive(false);
        }

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        Debug.Log("Image regeneration requested.");
    }

    public void Generate3D()
    {
        if (imagePreviewPanel != null)
        {
            imagePreviewPanel.SetActive(false);
        }

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }

        Debug.Log("3D generation requested.");
    }

    public void CancelPreview()
    {
        if (imagePreviewPanel != null)
        {
            imagePreviewPanel.SetActive(false);
        }

        Debug.Log("Image preview cancelled.");
    }
}