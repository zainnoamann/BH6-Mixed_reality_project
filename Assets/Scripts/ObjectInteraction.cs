using UnityEngine;

public class ObjectInteraction : MonoBehaviour
{
    private Renderer[] renderers;

    private Material[][] originalMaterials;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();

        originalMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            originalMaterials[i] = renderers[i].materials;
        }
    }

    public void SetHover(bool active)
    {
        if (active)
        {
            SetHighlight();
        }
        else
        {
            RestoreMaterials();
        }
    }

    public void SetSelected(bool active)
    {
        if (active)
        {
            SetSelectedHighlight();
        }
        else
        {
            RestoreMaterials();
        }
    }

    private void SetHighlight()
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
            renderers[i].materials = originalMaterials[i];
        }
    }
}
