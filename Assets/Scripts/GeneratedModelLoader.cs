using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Turns a generated result into a room object.
///
/// With glTFast installed (Package Manager -> Unity Registry -> "glTFast") AND the
/// scripting define GLTFAST set (Project Settings -> Player -> Scripting Define Symbols),
/// the GLB is loaded for real. Otherwise the generated image is wrapped onto a block of the
/// right size, so the flow still completes end to end.
///
/// Either way the object is scaled so its height matches <c>targetHeight</c>: generated
/// meshes carry no real-world scale, and this is where the room's scale is imposed.
/// </summary>
public static class GeneratedModelLoader
{
    public static IEnumerator Load(
        byte[] glb,
        Texture2D image,
        Color fallbackColour,
        string name,
        float targetHeight,
        Action<GameObject> done,
        bool keepImportedMaterials = false)
    {
        GameObject result = null;

#if GLTFAST
        if (glb != null && glb.Length > 0)
        {
            var gltf = new GLTFast.GltfImport();
            var loadTask = gltf.Load(glb);

            while (!loadTask.IsCompleted)
            {
                yield return null;
            }

            if (loadTask.Result)
            {
                result = new GameObject(name);

                var instantiateTask = gltf.InstantiateMainSceneAsync(result.transform);

                while (!instantiateTask.IsCompleted)
                {
                    yield return null;
                }

                if (!instantiateTask.Result)
                {
                    UnityEngine.Object.Destroy(result);
                    result = null;
                }
                else if (!keepImportedMaterials)
                {
                    // Always assign a new URP Lit material. Mutating the glTFast
                    // shader in place (looking for _BaseMap) leaves Hunyuan meshes grey.
                    ApplyLook(result, image, fallbackColour);
                }
            }
            else
            {
                Debug.LogWarning("GeneratedModelLoader: glTFast could not load the GLB; using a textured block.");
            }
        }
#else
        if (glb != null && glb.Length > 0)
        {
            Debug.Log(
                "GeneratedModelLoader: GLB received (" + glb.Length / 1024 + " KB) but glTFast is not " +
                "enabled - install the glTFast package and add the GLTFAST scripting define to load it. " +
                "Showing a textured block instead.");
        }
#endif

        if (result == null)
        {
            result = TexturedBlock(name, image, fallbackColour);
        }

        FitHeight(result, targetHeight);

        done?.Invoke(result);

        yield break;
    }

    private static GameObject TexturedBlock(string name, Texture2D image, Color colour)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        ApplyLook(block, image, colour);
        return block;
    }

    /// <summary>
    /// Puts a new URP Lit look on every renderer. Used for generated meshes and for
    /// Change Texture on an object that is already in the room. Does not rebuild geometry.
    /// Flowers vs vase only stay separate if they are already different objects.
    /// </summary>
    public static void ApplyLook(GameObject target, Texture2D image, Color fallbackColour)
    {
        if (target == null)
        {
            return;
        }

        Material material = CreateLook(image, fallbackColour);

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            Material[] slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0)
            {
                renderer.sharedMaterial = material;
                continue;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = material;
            }

            renderer.sharedMaterials = slots;
        }
    }

    public static Material CreateLook(Texture2D image, Color fallbackColour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);

        if (image != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", image);
            material.mainTexture = image;
            fallbackColour = Color.white;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", fallbackColour);
        material.color = fallbackColour;

        return material;
    }

    /// <summary>Uniformly scales the object so its renderer bounds are targetHeight tall.</summary>
    private static void FitHeight(GameObject target, float targetHeight)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0 || targetHeight <= 0f)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;

        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        float height = bounds.size.y;

        if (height <= 0.0001f)
        {
            return;
        }

        target.transform.localScale *= targetHeight / height;
    }
}
