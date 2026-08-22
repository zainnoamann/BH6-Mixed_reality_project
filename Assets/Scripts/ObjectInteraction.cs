using UnityEngine;

public class ObjectInteraction : MonoBehaviour
{
    private Renderer[] renderers;
    private Material[][] originalMaterials;

    private bool isHovered;
    private bool isSelected;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);

        originalMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            // Save the REAL original materials
            originalMaterials[i] = renderers[i].sharedMaterials;
        }
    }

    public void SetHover(bool active)
    {
        isHovered = active;
        UpdateAppearance();
    }

    public void SetSelected(bool active)
    {
        isSelected = active;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        // Selected always has priority
        if (isSelected)
        {
            SetSelectedHighlight();
        }
        else if (isHovered)
        {
            SetHoverHighlight();
        }
        else
        {
            RestoreMaterials();
        }
    }

    private void SetHoverHighlight()
    {
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].color = Color.yellow;
            }
        }
    }

    private void SetSelectedHighlight()
    {
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].color = Color.cyan;
            }
        }
    }

    private void RestoreMaterials()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            // Restore the actual original materials
            renderers[i].sharedMaterials = originalMaterials[i];
        }
    }
}
