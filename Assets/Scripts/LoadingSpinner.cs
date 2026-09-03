using UnityEngine;
using UnityEngine.UI;

public class LoadingSpinner : MonoBehaviour
{
    [Header("Spinner")]
    [SerializeField] private float rotationSpeed = 180f;

    [Header("Loading Bar")]
    [SerializeField] private Image loadingFill;
    [SerializeField] private float loadingDuration = 3f;

    private float loadingProgress = 0f;

    private void Start()
    {
        if (loadingFill != null)
        {
            loadingFill.fillAmount = 0f;
        }
    }

    private void Update()
    {
        // Rotate spinner
        transform.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);

        // Increase loading progress
        if (loadingFill != null)
        {
            loadingProgress += Time.deltaTime / loadingDuration;
            loadingProgress = Mathf.Clamp01(loadingProgress);

            loadingFill.fillAmount = loadingProgress;
        }
    }
}