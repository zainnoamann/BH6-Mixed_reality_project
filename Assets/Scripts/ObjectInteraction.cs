using UnityEngine;

public class ObjectInteraction : MonoBehaviour
{
    private Renderer[] renderers;
    private Material[][] originalMaterials;
    private Material[][] undoMaterials;

    private bool isHovered;
    private bool isSelected;
    private PlacementState placement = PlacementState.None;

    public enum PlacementState { None, Valid, Blocked }

    // Light tints multiply the object's own colours, so the texture stays visible.
    // The old full yellow / cyan made every clicked object look painted blue.
    private static readonly Color HoverTint = new Color(1f, 0.95f, 0.75f);
    private static readonly Color SelectedTint = new Color(0.7f, 0.88f, 1f);
    private static readonly Color ValidTint = new Color(0.65f, 1f, 0.65f);
    private static readonly Color BlockedTint = new Color(1f, 0.5f, 0.5f);

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

    /// <summary>Green / red preview while the object is being moved. None = normal look.</summary>
    public void SetPlacementState(PlacementState state)
    {
        placement = state;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        // Placement feedback first, then selection, then hover.
        if (placement == PlacementState.Blocked)
        {
            Tint(BlockedTint);
        }
        else if (placement == PlacementState.Valid)
        {
            Tint(ValidTint);
        }
        else if (isSelected)
        {
            Tint(SelectedTint);
        }
        else if (isHovered)
        {
            Tint(HoverTint);
        }
        else
        {
            RestoreMaterials();
        }
    }

    private void Tint(Color tint)
    {
        // Start from the real materials each time so tints never stack.
        RestoreMaterials();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];

                if (material == null)
                {
                    continue;
                }

                // URP Lit uses _BaseColor, glTFast (generated GLBs) uses baseColorFactor.
                string property =
                    material.HasProperty("_BaseColor") ? "_BaseColor" :
                    material.HasProperty("baseColorFactor") ? "baseColorFactor" :
                    material.HasProperty("_Color") ? "_Color" : null;

                if (property != null)
                {
                    material.SetColor(property, material.GetColor(property) * tint);
                }
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
        placement = PlacementState.None;
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
