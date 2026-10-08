using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Baked lighting for the house: soft light bouncing off walls, floors and ceilings,
/// daylight flooding in through the windows, and soft shadow in corners. This is what
/// makes ArchViz scenes (like Assets/MinimalistBedroom) look real.
///
///   Tools > House > Bake Lighting   - run Tools > House > Realistic Rendering first.
///
/// Only the parts that never move are baked into lightmaps: walls, floors, ceilings,
/// windows, roof and railings (RoomShell). Furniture can be moved and doors swing, so
/// they, and every AI-generated object, are lit by light probes instead. The sun and
/// indoor lights stay real-time for their direct light and shadows (they follow moved
/// furniture); only their bounce light is baked.
///
/// Run it again after changing the house or the lights. The bake runs in the background
/// (progress bar, bottom right); save the scene when it finishes.
/// </summary>
public static class HouseLightBaker
{
    private const string ModelPath = "Assets/Models/New_Model.fbx";
    private const string LightingPath = "Assets/Settings/RealisticLighting.lighting";
    private const string WindowLightsName = "House Window Lights";
    private const string ProbesName = "House Light Probes";

    // ---- lightmaps: detail vs bake time
    private const float TexelsPerMetre = 25f;         // crisp baked shading along edges and corners
    private const float OutdoorLightmapScale = 0.3f;  // the garden needs far less detail than rooms
    private const int LightmapMaxSize = 2048;
    private const int Bounces = 3;
    private const int IndirectSamples = 512;
    private const float CornerShadowDistance = 0.6f;  // baked ambient occlusion reach (metres)
    private const float CornerShadowStrength = 1.3f;  // 1 = physical; higher = deeper corners
    private const float BounceStrength = 1.1f;        // above 1 = brighter rooms from bounced light

    // ---- window light: daylight entering each window, as in the bedroom scene. This is
    // what lights the rooms evenly; the sun's patches add the drama on top.
    private const float WindowLightIntensity = 1.5f;
    private const float WindowLightTemperature = 7000f; // cool skylight against the warm sun

    // ---- light probes: how finely moving objects pick up the baked light
    private const float ProbeSpacing = 1.5f;
    private static readonly float[] ProbeHeights = { 0.3f, 1.2f, 2.2f };  // above each storey's floor
    private const float OutdoorProbeSpacing = 4f;

    [MenuItem("Tools/House/Bake Lighting")]
    public static void BakeLighting()
    {
        if (GameObject.Find(HouseRenderingSetup.LightsGroupName) == null)
        {
            Debug.LogError("House bake: run Tools > House > Realistic Rendering first; the bake uses its lights.");
            return;
        }

        if (!EditorUtility.DisplayDialog("Bake house lighting",
                "This re-imports the house model once (to add lightmap UVs) and then bakes the lighting. " +
                "Expect anything from a few minutes to an hour, depending on the computer. " +
                "Unity stays usable while it bakes.", "Bake", "Cancel"))
            return;

        AddLightmapUVs();

        // Find the model after the re-import.
        GameObject model = HouseMaterialBuilder.FindModelInstance();
        if (model == null)
        {
            Debug.LogError("House bake: no New_Model in the open scene.");
            return;
        }

        int baked = MarkStaticParts(model);
        int windows = AddWindowLights(model);
        int probes = AddLightProbes(model);
        ConfigureBake();

        EditorSceneManager.MarkSceneDirty(model.scene);
        Bake();

        Debug.Log("House bake: " + baked + " fixed parts in lightmaps, " + windows + " window lights, " +
                  probes + " light probes. Baking in the background.");
    }

    /// <summary>
    /// Starts a bake with the house lighting settings. Reflection probes are baked again
    /// when it finishes, so mirrors and glass reflect the lit rooms.
    /// </summary>
    internal static void Bake()
    {
        Lightmapping.lightingSettings = LoadSettings();

        Lightmapping.bakeCompleted -= AfterBake;
        Lightmapping.bakeCompleted += AfterBake;
        if (!Lightmapping.BakeAsync())
        {
            Lightmapping.bakeCompleted -= AfterBake;
            Debug.LogError("House bake: Unity could not start the bake (is the scene saved to a file?).");
        }
    }

    private static void AfterBake()
    {
        Lightmapping.bakeCompleted -= AfterBake;
        HouseMaterialBuilder.AddReflectionProbes();
    }

    /// <summary>
    /// The scene's lighting settings. Made on first use as sky light only (no lightmaps),
    /// which Realistic Rendering bakes in seconds; Bake Lighting turns on the full bake.
    /// </summary>
    private static LightingSettings LoadSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (settings == null)
        {
            settings = new LightingSettings { bakedGI = false, realtimeGI = false };
            AssetDatabase.CreateAsset(settings, LightingPath);
        }
        return settings;
    }

    private static void ConfigureBake()
    {
        LightingSettings settings = LoadSettings();
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;  // direct light stays real-time
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.lightmapResolution = TexelsPerMetre;
        settings.lightmapMaxSize = LightmapMaxSize;
        settings.lightmapPadding = 2;
        settings.directionalityMode = LightmapsMode.CombinedDirectional; // bounce light shows the bump detail
        settings.lightmapCompression = LightmapCompression.HighQuality;
        settings.maxBounces = Bounces;
        settings.indirectSampleCount = IndirectSamples;
        settings.directSampleCount = 32;
        settings.environmentSampleCount = 256;
        settings.indirectScale = BounceStrength;
        settings.ao = true;
        settings.aoMaxDistance = CornerShadowDistance;
        settings.aoExponentIndirect = CornerShadowStrength;
        settings.aoExponentDirect = 0f;  // direct light already has real-time shadows
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ model

    private static void AddLightmapUVs()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null || importer.generateSecondaryUV)
            return;

        importer.generateSecondaryUV = true;
        importer.SaveAndReimport();
    }

    private static bool IsBaked(string objectName)
    {
        // Doors are part of the room shell but swing open (AutoDoor).
        return RoomShell.IsFixed(objectName) && !objectName.ToLowerInvariant().Contains("door");
    }

    /// <summary>
    /// The model's direct children are its objects (as InteractionSetup sees them).
    /// Fixed ones go into the lightmaps; the rest use light probes. Everything is flagged
    /// for reflection probes, which only capture flagged objects. No batching flags:
    /// static batching would stop furniture and doors from moving.
    /// </summary>
    internal static int MarkStaticParts(GameObject model)
    {
        int baked = 0;
        foreach (Transform part in model.transform)
        {
            bool fixedPart = IsBaked(part.name);
            foreach (MeshRenderer renderer in part.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObject go = renderer.gameObject;
                Undo.RecordObjects(new Object[] { go, renderer }, "Mark static parts");

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                flags |= StaticEditorFlags.ReflectionProbeStatic; // seen in mirrors either way
                if (fixedPart)
                    flags |= StaticEditorFlags.ContributeGI;
                else
                    flags &= ~StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(go, flags);

                if (fixedPart)
                {
                    renderer.receiveGI = ReceiveGI.Lightmaps;
                    renderer.scaleInLightmap = InsideHouse(model, renderer.bounds.center) ? 1f : OutdoorLightmapScale;
                    baked++;
                }
                else
                {
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
            }
        }
        return baked;
    }

    private static bool InsideHouse(GameObject model, Vector3 worldPoint)
    {
        Vector3 p = model.transform.InverseTransformPoint(worldPoint);
        Vector3 min = HouseMaterialBuilder.HouseMin;
        Vector3 max = HouseMaterialBuilder.HouseMax;
        return p.x >= min.x && p.x <= max.x && p.z >= min.z && p.z <= max.z;
    }

    // ------------------------------------------------------------------ window light

    /// <summary>
    /// A baked rectangle of daylight just inside each window, facing into the house.
    /// The trick ArchViz scenes use: the sky alone, through small openings, leaves rooms
    /// dim and noisy; a light the size of the window fills them evenly.
    /// </summary>
    private static int AddWindowLights(GameObject model)
    {
        GameObject old = GameObject.Find(WindowLightsName);
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        GameObject group = new GameObject(WindowLightsName);
        Undo.RegisterCreatedObjectUndo(group, "Add window lights");

        Vector3 houseCentre = model.transform.TransformPoint(
            (HouseMaterialBuilder.HouseMin + HouseMaterialBuilder.HouseMax) / 2f);

        int count = 0;
        foreach (Transform part in model.transform)
        {
            if (!part.name.ToLowerInvariant().Contains("window") || !TryBounds(part, out Bounds bounds))
                continue;

            Vector3 size = bounds.size;
            Vector3 inward;
            Vector2 area;
            if (size.y <= size.x && size.y <= size.z)
            {
                inward = Vector3.down;                      // roof window
                area = new Vector2(size.x, size.z);
            }
            else if (size.x <= size.z)
            {
                inward = Vector3.right * Mathf.Sign(houseCentre.x - bounds.center.x);
                area = new Vector2(size.z, size.y);
            }
            else
            {
                inward = Vector3.forward * Mathf.Sign(houseCentre.z - bounds.center.z);
                area = new Vector2(size.x, size.y);
            }

            float thickness = Vector3.Scale(size, inward).magnitude;
            if (thickness > 0.6f || area.x < 0.3f || area.y < 0.3f)
            {
                Debug.LogWarning("House bake: '" + part.name + "' isn't shaped like one window, so it got no window light.");
                continue;
            }

            GameObject go = new GameObject(part.name + " light");
            go.transform.SetParent(group.transform, false);
            go.transform.position = bounds.center + inward * (thickness / 2f + 0.05f);
            go.transform.rotation = Quaternion.LookRotation(inward, inward == Vector3.down ? Vector3.forward : Vector3.up);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.areaSize = area;
            light.intensity = WindowLightIntensity;
            light.useColorTemperature = true;
            light.colorTemperature = WindowLightTemperature;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Baked;  // rectangle lights only exist in the bake
            count++;
        }

        if (count == 0)
            Debug.LogWarning("House bake: found no window objects in the model, so no window lights were added.");
        return count;
    }

    private static bool TryBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!any)
                bounds = renderer.bounds;
            else
                bounds.Encapsulate(renderer.bounds);
            any = true;
        }
        return any;
    }

    // ------------------------------------------------------------------ light probes

    /// <summary>
    /// A grid of probes through every storey, and a coarser one over the garden. Probes
    /// that land inside a wall or a piece of furniture would read black, so they're left out.
    /// </summary>
    private static int AddLightProbes(GameObject model)
    {
        GameObject old = GameObject.Find(ProbesName);
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        var colliders = AddTemporaryColliders(model);
        var positions = new List<Vector3>();
        try
        {
            Vector3 min = HouseMaterialBuilder.HouseMin;
            Vector3 max = HouseMaterialBuilder.HouseMax;
            const float margin = 0.3f;

            // Indoors, in the model's own coordinates.
            foreach (var storey in HouseMaterialBuilder.Storeys)
            {
                foreach (float height in ProbeHeights)
                {
                    for (float x = min.x + margin; x <= max.x - margin; x += ProbeSpacing)
                    {
                        for (float z = min.z + margin; z <= max.z - margin; z += ProbeSpacing)
                            AddProbe(positions, model.transform.TransformPoint(new Vector3(x, storey.bottom + height, z)), colliders);
                    }
                }
            }

            // Outdoors, round the house, at ground-floor level.
            if (TryBounds(model.transform, out Bounds all))
            {
                float ground = HouseMaterialBuilder.Storeys[0].bottom;
                foreach (float height in new[] { 0.5f, 2.5f })
                {
                    for (float x = all.min.x; x <= all.max.x; x += OutdoorProbeSpacing)
                    {
                        for (float z = all.min.z; z <= all.max.z; z += OutdoorProbeSpacing)
                        {
                            if (InsideHouse(model, new Vector3(x, 0f, z)))
                                continue;
                            Vector3 local = model.transform.InverseTransformPoint(new Vector3(x, 0f, z));
                            AddProbe(positions, model.transform.TransformPoint(new Vector3(local.x, ground + height, local.z)), colliders);
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (Collider collider in colliders)
                Object.DestroyImmediate(collider);
        }

        GameObject group = new GameObject(ProbesName);
        Undo.RegisterCreatedObjectUndo(group, "Add light probes");
        group.AddComponent<LightProbeGroup>().probePositions = positions.ToArray(); // world = local: group sits at the origin
        return positions.Count;
    }

    private static void AddProbe(List<Vector3> positions, Vector3 point, HashSet<Collider> colliders)
    {
        foreach (Collider hit in Physics.OverlapSphere(point, 0.15f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (colliders.Contains(hit))
                return;
        }
        positions.Add(point);
    }

    private static HashSet<Collider> AddTemporaryColliders(GameObject model)
    {
        var colliders = new HashSet<Collider>();
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.hideFlags = HideFlags.DontSave;
            collider.sharedMesh = filter.sharedMesh;
            colliders.Add(collider);
        }
        Physics.SyncTransforms();
        return colliders;
    }
}
