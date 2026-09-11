using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds the room root at runtime.
///
/// A scene reference into an imported model welds the scene to that exact FBX: swapping
/// the model breaks the reference and it has to be re-dragged by hand. Resolving by name
/// instead means the room root is found whichever model is in the scene.
///
/// An explicitly assigned Transform always wins; this is only the fallback.
/// </summary>
public static class RoomRootResolver
{
    private const int MaxDepth = 3;

    public static Transform Resolve(Transform explicitRoot, string rootName, Scene scene)
    {
        if (explicitRoot != null)
        {
            return explicitRoot;
        }

        if (string.IsNullOrEmpty(rootName) || !scene.IsValid())
        {
            return null;
        }

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            Transform found = Search(rootObject.transform, rootName, 0);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform Search(Transform current, string rootName, int depth)
    {
        if (current.name == rootName)
        {
            return current;
        }

        if (depth >= MaxDepth)
        {
            return null;
        }

        foreach (Transform child in current)
        {
            Transform found = Search(child, rootName, depth + 1);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
