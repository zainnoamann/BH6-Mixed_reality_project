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

        if (imagePreviewPanel != null && loadingPopup != null)
        {
            RectTransform panelRect =
                imagePreviewPanel.GetComponent<RectTransform>();

            RectTransform loadingRect =
                loadingPopup.GetComponent<RectTransform>();

            if (panelRect != null && loadingRect != null)
            {
                loadingRect.anchoredPosition = panelRect.anchoredPosition;
            }
        }

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }
    }

    public void Generate3D()
    {
        Debug.Log("3D generation requested.");

        if (imagePreviewPanel != null && loadingPopup != null)
        {
            RectTransform panelRect =
                imagePreviewPanel.GetComponent<RectTransform>();

            RectTransform loadingRect =
                loadingPopup.GetComponent<RectTransform>();

            if (panelRect != null && loadingRect != null)
            {
                loadingRect.anchoredPosition = panelRect.anchoredPosition;
            }
        }

        if (loadingPopup != null)
        {
            loadingPopup.SetActive(true);
        }
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