using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Measures the room so navigation, placement and UI anchoring agree on where the floor
/// and walls are.
/// </summary>
public static class RoomMetrics
{
    public static bool TryMeasure(Scene scene, string rootName, out Bounds bounds)
    {
        bounds = default;

        Transform root = RoomRootResolver.Resolve(null, rootName, scene);

        return root != null && TryMeasure(root, out bounds);
    }

    /// <summary>World-space bounds of every renderer under the root.</summary>
    public static bool TryMeasure(Transform root, out Bounds bounds)
    {
        bounds = new Bounds(root.position, Vector3.zero);

        bool found = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
}
