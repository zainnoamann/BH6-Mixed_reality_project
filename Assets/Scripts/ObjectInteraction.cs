using UnityEngine;

public class ObjectInteraction : MonoBehaviour
{
    private Renderer[] renderers;
    private Material[][] originalMaterials;
    private Material[][] undoMaterials;

    private bool isHovered;
    private bool isSelected;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originalMaterials = Snapshot(renderers);
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
        ApplySnapshot(originalMaterials);
    }

    /// <summary>Clears hover/selection so a texture apply does not snapshot the cyan tint.</summary>
    public void ClearHighlights()
    {
        isHovered = false;
        isSelected = false;
        RestoreMaterials();
    }

    /// <summary>Remembers the committed materials so Change Texture can undo.</summary>
    public void RememberForUndo()
    {
        undoMaterials = Clone(originalMaterials);
    }

    /// <summary>After a new look is assigned, hover/select restore that look, not the old one.</summary>
    public void AdoptCurrentMaterials()
    {
        originalMaterials = Snapshot(renderers);
    }

    public void CommitTexture()
    {
        undoMaterials = null;
    }

    public void RestoreUndo()
    {
        if (undoMaterials == null)
        {
            return;
        }

        isHovered = false;
        isSelected = false;
        ApplySnapshot(undoMaterials);
        originalMaterials = undoMaterials;
        undoMaterials = null;
    }

    private void ApplySnapshot(Material[][] snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        int count = Mathf.Min(renderers.Length, snapshot.Length);

        for (int i = 0; i < count; i++)
        {
            if (renderers[i] != null && snapshot[i] != null)
            {
                renderers[i].sharedMaterials = snapshot[i];
            }
        }
    }

    private static Material[][] Snapshot(Renderer[] source)
    {
        var copy = new Material[source.Length][];

        for (int i = 0; i < source.Length; i++)
        {
            copy[i] = source[i] != null ? source[i].sharedMaterials : null;
        }

        return copy;
    }

    private static Material[][] Clone(Material[][] source)
    {
        if (source == null)
        {
            return null;
        }

        var copy = new Material[source.Length][];

        for (int i = 0; i < source.Length; i++)
        {
            copy[i] = source[i] != null ? (Material[])source[i].Clone() : null;
        }

        return copy;
    }
}
