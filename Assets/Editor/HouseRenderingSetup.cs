using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Realistic rendering for the house in the open scene (made for SampleScene, desktop).
///
///   Tools > House > Realistic Rendering   - does everything below, in order:
///     1. Builds the house materials (Tools > House > Build Materials).
///     2. Sun, sky, ambient light and distance haze.
///     3. A light in every ceiling bulb and lamp shade of the model.
///     4. Crisp, high-resolution shadows; ambient occlusion in creases; the image drawn
///        above screen resolution (PC pipeline asset only, so the Quest settings are
///        untouched).
///     5. Camera: temporal anti-aliasing with sharpening, dithering, post-processing.
///     6. Post-processing profile: filmic tone mapping, a warm, lively grade, bloom on
///        bright lights, vignette, film grain.
///     7. Bakes the sky's light, then the reflection probes (Tools > House > Add
///        Reflection Probes). Takes seconds.
///
/// All lighting is real-time: lightmaps baked from this model came out black in places,
/// so the bounce light between surfaces is imitated by the sky's ambient light instead
/// (warm from below, as off a wooden floor).
///
/// Every value is in the recipe below; change one and run the menu again. It updates
/// what it made before instead of adding more.
/// </summary>
public static class HouseRenderingSetup
{
    private const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    private const string ProfilePath = "Assets/Settings/RealisticVolumeProfile.asset";
    private const string SkyPath = "Assets/Settings/RealisticSky.mat";
    private const string LightingPath = "Assets/Settings/RealisticLighting.lighting";
    private const string LightsGroupName = "House Lights";

    // The look is warm and lively: a golden-hour sun cuts crisp light patches through the
    // windows, every room is softly lit, lamps add warm pools.
    // Brighter overall: raise PostExposure first (each +1 doubles the brightness), then
    // AmbientIntensity. Darker and moodier: the reverse, and raise Contrast.

    // ---- sun: golden hour, warm, high enough to reach well into the rooms
    private const float SunElevation = 30f;         // degrees above the horizon
    private const float SunHeading = -40f;
    private const float SunIntensity = 3f;
    private const float SunTemperature = 4200f;     // kelvin

    // ---- sky light filling the shade: keeps every corner visible. It comes from the
    // sky above and from FloorBounce below, so ceilings and undersides glow warm.
    private const float AmbientIntensity = 1.25f;
    private const float ReflectionIntensity = 1f;
    private static readonly Color FloorBounce = new Color(0.52f, 0.42f, 0.32f);

    // ---- indoor lights: warm 2700 K light under each bulb, a glow from each lamp
    private const string BulbMaterial = "_Orange_";
    private const float BulbIntensity = 6f;
    private const float BulbRange = 4.5f;
    private const float BulbTemperature = 2700f;
    private const float BulbSpotAngle = 110f;       // a broad, soft wash down the walls
    private const float BulbInnerAngle = 0.4f;      // of BulbSpotAngle: a soft-edged pool

    private const string LampshadeMaterial = "H_Lampshade";
    private const float LampIntensity = 2.5f;
    private const float LampRange = 2.5f;
    private const float LampTemperature = 2400f;

    private const float FixtureJoin = 0.3f;         // mesh pieces closer than this are one fixture (metres)

    // ---- sharp shadows: high resolution, short shadow distance (more detail near the
    // camera), light filtering, small bias so shadows touch what casts them
    private const int SunShadowResolution = 4096;
    private const int LampShadowAtlasResolution = 4096;
    private const float ShadowDistance = 30f;
    private const float SunShadowBias = 0.05f;
    private const float SunShadowNormalBias = 0.25f;
    private const float OcclusionIntensity = 1.1f;  // defined creases where things meet
    private const float OcclusionRadius = 0.25f;    // tight, so the darkening hugs the contact
    private const float OcclusionDirectStrength = 0.25f;

    // ---- sharp image: drawn above screen resolution, then temporal anti-aliasing (no
    // shimmer on glossy floors) with sharpening
    private const float RenderScale = 1.3f;         // 1 = screen resolution; costs GPU time
    private const float Sharpening = 0.6f;          // 0 = soft, 1 = strongest
    private const float TextureSharpness = -0.5f;   // texture mip bias, -1 .. 0; lower is sharper

    // ---- grading
    private const float PostExposure = 0.5f;        // ACES tone mapping darkens; this lifts it back
    private const float Contrast = 10f;
    private const float Saturation = 6f;            // a little richer: wood, fabric and plants read alive
    private static readonly Color ShadowTint = new Color(0.48f, 0.5f, 0.53f);     // shade a touch cool
    private static readonly Color HighlightTint = new Color(0.6f, 0.53f, 0.45f);  // warm light

    // ---- haze: invisible indoors, softens the far garden and the horizon
    private const float HazeDensity = 0.004f;
    private static readonly Color HazeColour = new Color(0.72f, 0.79f, 0.86f);

    [MenuItem("Tools/House/Realistic Rendering")]
    public static void Apply()
    {
        HouseMaterialBuilder.BuildMaterials();

        // Find the model after building: the material step re-imports it.
        GameObject model = HouseMaterialBuilder.FindModelInstance();
        if (model == null)
        {
            Debug.LogError("House rendering: no New_Model in the open scene.");
            return;
        }

        RemoveOldExperiments(model);
        SetUpSunAndSky();
        int lights = AddIndoorLights(model);
        SetUpPipeline();
        SetUpCameras();
        SetUpPostProcessing();
        MarkForReflections(model);

        EditorSceneManager.MarkSceneDirty(model.scene);
        BakeSkyLight();

        Debug.Log("House rendering: done, " + lights + " indoor lights. The sky's light is baking " +
                  "(a few seconds); save the scene when it finishes.");
    }

    // ------------------------------------------------------------------ clean-up

    /// <summary>
    /// Removes what earlier versions of these tools added: the wall plaster, and the
    /// lightmap bake (its lightmaps, window lights, light probes and static flags).
    /// Does nothing once the scene is clean; can be deleted when every copy of the scene is.
    /// </summary>
    private static void RemoveOldExperiments(GameObject model)
    {
        HouseMaterialBuilder.RemovePlaster(model);

        foreach (string name in new[] { "House Window Lights", "House Light Probes" })
        {
            GameObject old = GameObject.Find(name);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
        }

        Lightmapping.Clear();
    }

    /// <summary>
    /// Reflection probes only capture objects flagged for them, so mirrors and glass show
    /// the rooms. Nothing is flagged for lightmaps, and nothing for batching, which would
    /// stop furniture and doors from moving.
    /// </summary>
    private static void MarkForReflections(GameObject model)
    {
        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            GameObject go = renderer.gameObject;
            Undo.RecordObject(go, "Mark for reflections");
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
            flags |= StaticEditorFlags.ReflectionProbeStatic;
            flags &= ~StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
        }
    }

    /// <summary>
    /// Bakes only the sky's ambient light and reflection (no lightmaps), so builds look
    /// like the editor; then the reflection probes, so they show the lit rooms.
    /// </summary>
    private static void BakeSkyLight()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (settings == null)
        {
            settings = new LightingSettings();
            AssetDatabase.CreateAsset(settings, LightingPath);
        }
        settings.bakedGI = false;
        settings.realtimeGI = false;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Lightmapping.lightingSettings = settings;
        Lightmapping.bakeCompleted -= AfterBake;
        Lightmapping.bakeCompleted += AfterBake;
        if (!Lightmapping.BakeAsync())
        {
            Lightmapping.bakeCompleted -= AfterBake;
            Debug.LogError("House rendering: Unity could not start baking the sky light (is the scene saved to a file?).");
        }
    }

    private static void AfterBake()
    {
        Lightmapping.bakeCompleted -= AfterBake;
        HouseMaterialBuilder.AddReflectionProbes();
    }

    // ------------------------------------------------------------------ sun and sky

    private static void SetUpSunAndSky()
    {
        Light sun = FindSun();
        if (sun == null)
        {
            GameObject go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Add sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        Undo.RecordObjects(new Object[] { sun, sun.transform }, "Set up sun");
        sun.enabled = true;
        sun.gameObject.SetActive(true);
        sun.transform.rotation = Quaternion.Euler(SunElevation, SunHeading, 0f);
        sun.intensity = SunIntensity;
        sun.useColorTemperature = true;
        sun.colorTemperature = SunTemperature;
        sun.color = Color.white;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.shadowBias = SunShadowBias;
        sun.shadowNormalBias = SunShadowNormalBias;

        // Its own bias (above) instead of the pipeline's, and light filtering, which keeps
        // shadow edges crisp but not jagged.
        UniversalAdditionalLightData sunData = sun.GetUniversalAdditionalLightData();
        Undo.RecordObject(sunData, "Set up sun");
        sunData.usePipelineSettings = false;
        sunData.softShadowQuality = SoftShadowQuality.Low;
        EditorUtility.SetDirty(sunData);
        sun.lightmapBakeType = LightmapBakeType.Realtime; // shadows follow moved furniture

        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, SkyPath);
        }
        sky.SetFloat("_SunDisk", 2f);               // high quality sun disc
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 1.1f); // a touch of haze, warmer near the horizon
        sky.SetColor("_SkyTint", new Color(0.5f, 0.52f, 0.55f));
        sky.SetColor("_GroundColor", FloorBounce);  // the light from below in the ambient
        sky.SetFloat("_Exposure", 1.2f);
        EditorUtility.SetDirty(sky);

        // Ambient light and default reflections come from the sky, so shade colours
        // match the time of day instead of a flat grey.
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = ReflectionIntensity;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = HazeDensity;
        RenderSettings.fogColor = HazeColour;

        DynamicGI.UpdateEnvironment();
    }

    private static Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional)
                    return light;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ indoor lights

    private static int AddIndoorLights(GameObject model)
    {
        GameObject old = GameObject.Find(LightsGroupName);
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        GameObject group = new GameObject(LightsGroupName);
        Undo.RegisterCreatedObjectUndo(group, "Add house lights");

        // Downlights: a spot just under each bulb, shining down. Shadows keep the
        // light from leaking through the floor into the room below.
        List<Bounds> bulbs = FindFixtures(model, BulbMaterial);
        for (int i = 0; i < bulbs.Count; i++)
        {
            Bounds bulb = bulbs[i];
            Light light = AddLight(group, "Bulb " + (i + 1), LightType.Spot,
                                   new Vector3(bulb.center.x, bulb.min.y - 0.05f, bulb.center.z),
                                   BulbIntensity, BulbRange, BulbTemperature);
            light.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            light.spotAngle = BulbSpotAngle;
            light.innerSpotAngle = BulbSpotAngle * BulbInnerAngle;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.05f;
        }

        // Lamps: a soft glow from inside each shade. No shadows: the shade itself
        // would block them, and the short range keeps the light in its room.
        List<Bounds> lamps = FindFixtures(model, LampshadeMaterial);
        for (int i = 0; i < lamps.Count; i++)
        {
            AddLight(group, "Lamp " + (i + 1), LightType.Point, lamps[i].center,
                     LampIntensity, LampRange, LampTemperature);
        }

        if (bulbs.Count + lamps.Count == 0)
            Debug.LogWarning("House rendering: found no '" + BulbMaterial + "' or '" + LampshadeMaterial +
                             "' pieces in the model, so no indoor lights were added.");

        return bulbs.Count + lamps.Count;
    }

    private static Light AddLight(GameObject group, string name, LightType type, Vector3 position,
                                  float intensity, float range, float temperature)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(group.transform, false);
        go.transform.position = position;

        Light light = go.AddComponent<Light>();
        light.type = type;
        light.intensity = intensity;
        light.range = range;
        light.useColorTemperature = true;
        light.colorTemperature = temperature;
        light.color = Color.white;
        light.lightmapBakeType = LightmapBakeType.Realtime; // as the sun
        return light;
    }

    /// <summary>
    /// World-space boxes around every separate piece of the model drawn with a material.
    /// Works per vertex, so it finds each bulb even when an export merged many into one mesh.
    /// </summary>
    private static List<Bounds> FindFixtures(GameObject model, string materialName)
    {
        var fixtures = new List<Bounds>();

        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            Vector3[] vertices = null;
            for (int sub = 0; sub < materials.Length && sub < mesh.subMeshCount; sub++)
            {
                if (materials[sub] == null || materials[sub].name != materialName)
                    continue;

                if (!mesh.isReadable)
                {
                    Join(fixtures, renderer.bounds);
                    continue;
                }

                vertices ??= mesh.vertices;
                foreach (int index in mesh.GetTriangles(sub))
                    Join(fixtures, new Bounds(renderer.transform.TransformPoint(vertices[index]), Vector3.zero));
            }
        }

        // Joining point by point can leave one fixture in two parts; join until none touch.
        for (bool joined = true; joined;)
        {
            joined = false;
            for (int i = 0; i < fixtures.Count && !joined; i++)
            {
                for (int j = i + 1; j < fixtures.Count; j++)
                {
                    if (!Touches(fixtures[i], fixtures[j]))
                        continue;

                    Bounds both = fixtures[i];
                    both.Encapsulate(fixtures[j]);
                    fixtures[i] = both;
                    fixtures.RemoveAt(j);
                    joined = true;
                    break;
                }
            }
        }

        return fixtures;
    }

    private static void Join(List<Bounds> fixtures, Bounds piece)
    {
        for (int i = 0; i < fixtures.Count; i++)
        {
            if (!Touches(fixtures[i], piece))
                continue;

            Bounds both = fixtures[i];
            both.Encapsulate(piece);
            fixtures[i] = both;
            return;
        }
        fixtures.Add(piece);
    }

    private static bool Touches(Bounds a, Bounds b)
    {
        a.Expand(FixtureJoin * 2f); // Expand grows the size, so each side moves by FixtureJoin
        return a.Intersects(b);
    }

    // ------------------------------------------------------------------ pipeline and cameras

    private static void SetUpPipeline()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            Debug.LogError("House rendering: " + PipelinePath + " not found.");
            return;
        }

        Undo.RecordObject(pipeline, "Set up pipeline");
        pipeline.mainLightShadowmapResolution = SunShadowResolution;
        pipeline.additionalLightsShadowmapResolution = LampShadowAtlasResolution;
        pipeline.shadowDistance = ShadowDistance;
        pipeline.renderScale = RenderScale;
        pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;    // filmic tone mapping needs HDR grading
        pipeline.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits; // no banding in dim corners
        EditorUtility.SetDirty(pipeline);

        // Ambient occlusion: darkens corners, under furniture and where objects meet.
        // Its settings are internal to URP, so they are set the way the inspector does.
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        ScriptableRendererFeature occlusion = renderer == null ? null
            : renderer.rendererFeatures.Find(f => f is ScreenSpaceAmbientOcclusion);
        if (occlusion == null)
        {
            Debug.LogWarning("House rendering: no Screen Space Ambient Occlusion feature on " + RendererPath + ".");
            return;
        }

        var settings = new SerializedObject(occlusion);
        settings.FindProperty("m_Settings.Intensity").floatValue = OcclusionIntensity;
        settings.FindProperty("m_Settings.Radius").floatValue = OcclusionRadius;
        settings.FindProperty("m_Settings.DirectLightingStrength").floatValue = OcclusionDirectStrength;
        settings.FindProperty("m_Settings.BlurQuality").enumValueIndex = 0;       // high
        settings.ApplyModifiedProperties();
        occlusion.SetActive(true);
        EditorUtility.SetDirty(renderer);
    }

    private static void SetUpCameras()
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                Undo.RecordObjects(new Object[] { camera, data }, "Set up camera");
                camera.allowHDR = true;
                data.renderPostProcessing = true;
                // Temporal AA: smooths edges and the sparkle on glossy floors and metal;
                // its sharpening and mip bias keep edges and textures crisp. If moved
                // furniture leaves trails, switch to SubpixelMorphologicalAntiAliasing.
                data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.taaSettings.quality = TemporalAAQuality.VeryHigh;
                data.taaSettings.contrastAdaptiveSharpening = Sharpening;
                data.taaSettings.mipBias = TextureSharpness;
                data.dithering = true;
                data.stopNaN = true;
                EditorUtility.SetDirty(data);
            }
        }
    }

    // ------------------------------------------------------------------ post-processing

    private static void SetUpPostProcessing()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // Filmic response: highlights roll off like a camera instead of clipping.
        Override<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

        ColorAdjustments colour = Override<ColorAdjustments>(profile);
        colour.postExposure.Override(PostExposure);
        colour.contrast.Override(Contrast);
        colour.saturation.Override(Saturation);

        // Cool shade, warm light: the split every interior photographer's grade leans on.
        SplitToning toning = Override<SplitToning>(profile);
        toning.shadows.Override(ShadowTint);
        toning.highlights.Override(HighlightTint);
        toning.balance.Override(0f);

        // Rich blacks, open midtones: shade stays dark at the very bottom, but the rest
        // of the image stays bright enough to see into every room.
        LiftGammaGain levels = Override<LiftGammaGain>(profile);
        levels.lift.Override(new Vector4(1f, 1f, 1f, -0.01f));
        levels.gamma.Override(new Vector4(1f, 1f, 1f, 0.08f));

        Bloom bloom = Override<Bloom>(profile);     // only bulbs, sun glints and sky glow
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.3f);
        bloom.scatter.Override(0.6f);
        bloom.highQualityFiltering.Override(true);

        Vignette vignette = Override<Vignette>(profile);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.45f);

        FilmGrain grain = Override<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.1f);
        grain.response.Override(0.8f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Volume volume = FindGlobalVolume();
        if (volume == null)
        {
            GameObject go = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(go, "Add global volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }

        Undo.RecordObject(volume, "Set up post-processing");
        volume.sharedProfile = profile;
        volume.weight = 1f;
    }

    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            // Saved inside the profile asset, as the Volume inspector does.
            component = profile.Add<T>();
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        component.active = true;
        EditorUtility.SetDirty(component);
        return component;
    }

    private static Volume FindGlobalVolume()
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Volume volume in root.GetComponentsInChildren<Volume>(true))
            {
                if (volume.isGlobal)
                    return volume;
            }
        }
        return null;
    }

}
