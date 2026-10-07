using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives a UI image a soft top-to-bottom colour fade, like the backplates in
/// Meta's UI Set. It only changes the colours of the image's corners, so the
/// image keeps the normal UI material (VrMenuPlacer needs that).
/// </summary>
[RequireComponent(typeof(Graphic))]
public class UiGradient : BaseMeshEffect
{
    public Color top = Color.white;
    public Color bottom = Color.grey;

    public void Set(Color topColour, Color bottomColour)
    {
        top = topColour;
        bottom = bottomColour;

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
        {
            return;
        }

        Rect rect = graphic.rectTransform.rect;
        float height = Mathf.Max(rect.height, 0.0001f);
        UIVertex vertex = new UIVertex();

        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);

            float t = Mathf.Clamp01((vertex.position.y - rect.yMin) / height);
            Color fade = Color.Lerp(bottom, top, t);
            Color original = vertex.color;
            vertex.color = original * fade;

            vh.SetUIVertex(vertex, i);
        }
    }
}
