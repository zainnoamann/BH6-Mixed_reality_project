using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

/// <summary>
/// Builds the house's look from New_Model.fbx.
///
///   Tools > House > Build Materials          - URP Lit materials for every material in the
///                                              model (textures, bump, gloss, metal, glass),
///                                              hooked into the model's import settings.
///   Tools > House > Add Reflection Probes    - one box-projected probe per floor in the open
///                                              scene, then bakes them. Mirrors, glass and
///                                              metal need these to reflect the rooms.
///
/// Both can be run again after changing a recipe below; they update what they made before.
/// </summary>
public static class HouseMaterialBuilder
{
    private const string ModelPath = "Assets/Models/New_Model.fbx";
    private const string TextureFolder = "Assets/Models/New_Model_Textures";
    private const string MaterialFolder = "Assets/Models/New_Model_Materials";

    private class Recipe
    {
        public string Texture;          // file name in TextureFolder, without .jpg
        public Color Colour = Color.white;
        public float Smoothness = 0.3f;
        public float Metallic;
        public float Bump;              // 0 = none; bump map generated from the texture
        public float Alpha = 1f;        // below 1 = see-through
        public Color Emission = Color.black;
    }

    private static Recipe R(string texture, float r, float g, float b, float smoothness,
                            float metallic = 0f, float bump = 0f, float alpha = 1f) =>
        new Recipe { Texture = texture, Colour = new Color(r, g, b), Smoothness = smoothness,
                     Metallic = metallic, Bump = bump, Alpha = alpha };

    private static Recipe Glass(float r, float g, float b, float alpha) => R(null, r, g, b, 0.97f, 0f, 0f, alpha);
    private static Recipe Mirror() => R(null, 0.93f, 0.94f, 0.95f, 1f, 1f);

    private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>
    {
        // ---- walls, floors, ceilings
        ["FrontColor"] = R(null, 0.94f, 0.92f, 0.88f, 0.12f),                  // warm white matte paint
        ["H_Paint_Ceiling"] = R(null, 0.97f, 0.96f, 0.94f, 0.05f),
        ["_2"] = R("_2", 1f, 1f, 1f, 0.5f, 0f, 0.6f),                          // oak floorboards
        ["Lisanne_Caulk"] = R(null, 0.95f, 0.94f, 0.91f, 0.45f),               // white satin lacquer
        ["Concrete_Tile"] = R("Concrete_Tile", 1f, 1f, 1f, 0.25f, 0f, 0.5f),
        ["Tile_Limestone_Large"] = R("Tile_Limestone_Large", 1f, 1f, 1f, 0.6f, 0f, 0.4f),
        ["_Tile_Mosaic_Multi__copy"] = R("_Tile_Mosaic_Multi__copy", 1f, 1f, 1f, 0.7f, 0f, 0.5f),

        // ---- outside
        ["Cladding_Stucco_White"] = R("Cladding_Stucco_White", 1f, 1f, 1f, 0.05f, 0f, 1f),
        ["Modern_Siding"] = R("Modern_Siding", 1f, 1f, 1f, 0.35f, 0f, 0.8f),
        ["_Modern_Siding_1"] = R("_Modern_Siding_1", 1f, 1f, 1f, 0.35f, 0f, 0.8f),
        ["_Modern_Siding_2"] = R("_Modern_Siding_2", 1f, 1f, 1f, 0.3f, 0f, 0.8f),
        ["Roofing_Slate_Dark"] = R("Roofing_Slate_Dark", 1f, 1f, 1f, 0.2f, 0f, 0.8f),
        ["Grass_Light_Green"] = R("Grass_Light_Green", 1f, 1f, 1f, 0.05f, 0f, 0.6f),
        ["Pavers_Stone_Walk"] = R("Pavers_Stone_Walk", 1f, 1f, 1f, 0.2f, 0f, 0.8f),
        ["Stone_Sandstone_Ashlar_Light"] = R("Stone_Sandstone_Ashlar_Light", 1f, 1f, 1f, 0.15f, 0f, 1f),
        ["Concrete_Pavers_Block_Multi"] = R("Concrete_Pavers_Block_Multi", 1f, 1f, 1f, 0.2f, 0f, 0.7f),
        ["Pavers_with_Grass_Brick"] = R("Pavers_with_Grass_Brick", 1f, 1f, 1f, 0.15f, 0f, 0.7f),
        ["Water_Pool_Light"] = R("Water_Pool_Light", 0.75f, 0.95f, 1f, 0.95f, 0f, 0.3f, 0.75f),
        ["H_Soil"] = R(null, 0.2f, 0.17f, 0.13f, 0.05f),

        // ---- windows, glass, mirrors
        ["Metal_Seamed"] = R(null, 0.12f, 0.12f, 0.13f, 0.5f, 0.6f),            // anthracite frames
        ["_Metal_Aluminum_Anodized_1"] = R(null, 0.12f, 0.12f, 0.13f, 0.5f, 0.6f),
        ["Translucent_Glass_Gray"] = Glass(0.8f, 0.88f, 0.9f, 0.15f),
        ["Translucent_Glass_Dark_Green"] = Glass(0.8f, 0.88f, 0.9f, 0.15f),
        ["Translucent_Glass_Tinted"] = Glass(0.7f, 0.82f, 0.85f, 0.2f),
        ["Translucent_1"] = Glass(0.9f, 0.95f, 0.95f, 0.2f),                     // shower screens
        ["glass_door"] = Glass(0.6f, 0.6f, 0.6f, 0.35f),
        ["_Translucent_Glass_Gray_1"] = Glass(0.15f, 0.13f, 0.12f, 0.55f),      // smoked table glass
        ["_Translucent_Glass_Blue_1"] = Glass(0.1f, 0.1f, 0.1f, 0.3f),          // fireplace glass
        ["Mirror_01"] = Mirror(),
        ["_Mirror_01_1"] = Mirror(),

        // ---- metals
        ["Metal_Aluminum_Anodized"] = R(null, 0.8f, 0.81f, 0.82f, 0.75f, 1f),
        ["_Metal_Aluminum_Anodized_2"] = R(null, 0.7f, 0.7f, 0.72f, 0.6f, 1f),
        ["Metal_Corrugated_Shiny"] = R(null, 0.2f, 0.2f, 0.2f, 0.5f, 1f),
        ["Metal_Seamed1"] = R(null, 0.8f, 0.8f, 0.8f, 0.85f, 1f),
        ["_CorrogateShiny_2"] = R(null, 0.05f, 0.05f, 0.05f, 0.5f, 0.8f),
        ["m_aluminium"] = R(null, 0.05f, 0.05f, 0.05f, 0.5f, 0.8f),
        ["H_Metal_Black"] = R(null, 0.04f, 0.04f, 0.04f, 0.45f, 0.8f),
        ["H_Brass"] = R(null, 0.83f, 0.66f, 0.38f, 0.7f, 1f),
        ["H_Chrome"] = R(null, 0.92f, 0.92f, 0.92f, 0.95f, 1f),
        ["H_Steel"] = R(null, 0.78f, 0.78f, 0.77f, 0.65f, 1f),

        // ---- wood
        ["_Wood_Cherry_Original_2"] = R("_Wood_Cherry_Original_1", 1f, 1f, 1f, 0.45f, 0f, 0.3f), // cabinetry: warmer walnut
        ["_Wood_Cherry_Original_1"] = R("_Wood_Cherry_Original_1", 1f, 1f, 1f, 0.3f, 0f, 0.3f),
        ["Wood_Cherry_Original"] = R("Wood_Cherry_Original", 1f, 1f, 1f, 0.45f, 0f, 0.3f),
        ["Wood_Lumber_ButtJoined"] = R("Wood_Lumber_ButtJoined", 1f, 1f, 1f, 0.3f, 0f, 0.4f),
        ["Wood__Floor"] = R("Wood__Floor", 1f, 1f, 1f, 0.4f, 0f, 0.3f),
        ["images_11"] = R("images_11", 1f, 1f, 1f, 0.5f, 0f, 0.3f),
        ["Wood_Veneer_01"] = R(null, 0.45f, 0.32f, 0.2f, 0.4f),
        ["Wood_Cedar_Post"] = R(null, 0.33f, 0.24f, 0.16f, 0.35f),

        // ---- fabric, leather, bedding
        ["H_Fabric_Oat"] = R(null, 0.78f, 0.72f, 0.64f, 0.08f),
        ["H_Fabric_Terracotta"] = R(null, 0.66f, 0.37f, 0.26f, 0.08f),
        ["H_Fabric_Slate"] = R(null, 0.42f, 0.47f, 0.52f, 0.1f),
        ["H_Linen"] = R(null, 0.94f, 0.92f, 0.88f, 0.1f),
        ["H_Leather_Cognac"] = R(null, 0.48f, 0.27f, 0.14f, 0.45f),
        ["_26"] = R("_26", 1f, 1f, 1f, 0f, 0f, 0.8f),                         // rug
        ["_27"] = R("_27", 1f, 1f, 1f, 0f, 0f, 0.8f),

        // ---- stone, ceramic, plastic
        ["Carrera_Marble"] = R(null, 0.93f, 0.93f, 0.92f, 0.85f),
        ["H_Quartz"] = R(null, 0.9f, 0.89f, 0.86f, 0.75f),
        ["countertop"] = R("countertop", 1f, 1f, 1f, 0.75f, 0f, 0.2f),
        ["countertop1"] = R(null, 0.97f, 0.97f, 0.96f, 0.85f),
        ["H_Ceramic"] = R(null, 0.97f, 0.97f, 0.96f, 0.9f),
        ["H_Plastic_White"] = R(null, 0.93f, 0.93f, 0.92f, 0.55f),

        // ---- appliances, screens, fireplace
        ["Paint_05"] = R(null, 0.03f, 0.03f, 0.03f, 0.85f),                     // black glass appliances
        ["H_Screen"] = R(null, 0.01f, 0.01f, 0.012f, 0.95f),
        ["M_0136_Charcoal"] = R(null, 0.08f, 0.08f, 0.08f, 0.6f),
        ["Color_M07"] = R(null, 0.12f, 0.12f, 0.12f, 0.35f, 0.4f),
        ["Color_M08"] = R(null, 0.05f, 0.05f, 0.05f, 0.2f),
        ["M_7dcf7758da6b78b92722cb2fe6b0373a"] = R("M_7dcf7758da6b78b92722cb2fe6b0373a", 1f, 1f, 1f, 0.6f),
        ["_19"] = R("_19", 1f, 1f, 1f, 0.3f),

        // ---- remaining generic colours
        ["Color_002"] = R(null, 0.86f, 0.85f, 0.82f, 0.35f),
        ["M_0007_DarkGray"] = R(null, 0.2f, 0.2f, 0.21f, 0.4f, 0.3f),
        ["_CoolGray4_"] = R(null, 0.86f, 0.87f, 0.88f, 0.5f),
        ["_Ivory_"] = R(null, 0.97f, 0.96f, 0.9f, 0.6f),
        ["_LightSteelBlue_"] = R(null, 0.6f, 0.7f, 0.8f, 0.6f),
    };

    static HouseMaterialBuilder()
    {
        // Light sources glow: the lamp shades and the spotlight bulbs.
        Recipes["H_Lampshade"] = new Recipe { Colour = new Color(0.96f, 0.93f, 0.86f), Smoothness = 0.1f,
                                              Emission = new Color(1f, 0.85f, 0.6f) * 0.6f };
        Recipes["_Orange_"] = new Recipe { Colour = new Color(1f, 0.85f, 0.6f), Smoothness = 0.5f,
                                           Emission = new Color(1f, 0.78f, 0.5f) * 2f };
    }

    // ------------------------------------------------------------------ materials

    [MenuItem("Tools/House/Build Materials")]
    public static void BuildMaterials()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("House materials: " + ModelPath + " not found.");
            return;
        }

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("House materials: the URP Lit shader was not found.");
            return;
        }

        Directory.CreateDirectory(MaterialFolder);

        // Every material the model uses, as the importer names them.
        var names = new List<string>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Material && !names.Contains(asset.name))
                names.Add(asset.name);
        }
        foreach (var pair in importer.GetExternalObjectMap())
        {
            if (pair.Key.type == typeof(Material) && !names.Contains(pair.Key.name))
                names.Add(pair.Key.name);
        }

        foreach (string name in names)
            PrepareTextures(name);

        int built = 0;
        foreach (string name in names)
        {
            if (!Recipes.TryGetValue(name, out Recipe recipe))
            {
                Debug.LogWarning("House materials: no recipe for '" + name + "', left as imported.");
                continue;
            }

            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }

            Apply(material, lit, recipe);
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            built++;
        }

        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
        Debug.Log("House materials: built " + built + " of " + names.Count + " materials. " +
                  "Run Tools > House > Add Reflection Probes in each scene for mirrors and glass.");
    }

    private static void PrepareTextures(string name)
    {
        if (!Recipes.TryGetValue(name, out Recipe recipe) || recipe.Texture == null)
            return;

        string source = TextureFolder + "/" + recipe.Texture + ".jpg";
        if (AssetImporter.GetAtPath(source) is TextureImporter colour)
        {
            colour.anisoLevel = 8; // floors and walls seen at shallow angles stay sharp
            colour.wrapMode = TextureWrapMode.Repeat;
            colour.SaveAndReimport();
        }

        if (recipe.Bump <= 0f)
            return;

        // Bump map generated from the colour texture's brightness: a copy of the file,
        // imported as a normal map.
        string bump = TextureFolder + "/" + recipe.Texture + "_Bump.jpg";
        if (!File.Exists(bump) && File.Exists(source))
        {
            File.Copy(source, bump);
            AssetDatabase.ImportAsset(bump);
        }
        if (AssetImporter.GetAtPath(bump) is TextureImporter normal)
        {
            normal.textureType = TextureImporterType.NormalMap;
            normal.convertToNormalmap = true;
            normal.heightmapScale = 0.1f * recipe.Bump;
            normal.normalmapFilter = TextureImporterNormalFilter.Sobel;
            normal.anisoLevel = 8;
            normal.SaveAndReimport();
        }
    }

    private static void Apply(Material material, Shader lit, Recipe recipe)
    {
        material.shader = lit;

        Texture2D baseMap = recipe.Texture == null ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + recipe.Texture + ".jpg");
        Texture2D bumpMap = recipe.Texture == null || recipe.Bump <= 0f ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + recipe.Texture + "_Bump.jpg");

        Color colour = recipe.Colour;
        colour.a = recipe.Alpha;

        material.SetTexture("_BaseMap", baseMap);
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", recipe.Smoothness);
        material.SetFloat("_Metallic", recipe.Metallic);
        material.SetTexture("_BumpMap", bumpMap);
        material.SetFloat("_BumpScale", 1f);

        bool seeThrough = recipe.Alpha < 1f;
        material.SetFloat("_Surface", seeThrough ? 1f : 0f);   // 1 = Transparent
        material.SetFloat("_Blend", 0f);                       // alpha blending
        material.SetFloat("_Cull", 2f);                        // back faces: the model is double-sided where it matters

        material.SetColor("_EmissionColor", recipe.Emission);
        material.globalIlluminationFlags = recipe.Emission.maxColorComponent > 0f
            ? MaterialGlobalIlluminationFlags.BakedEmissive
            : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        // Let URP derive keywords, blend states and render queue from the properties,
        // exactly as its material inspector does.
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
    }

    // ------------------------------------------------------------------ reflections

    // Floors in the model's own coordinates (metres): x and z cover the house, y each storey.
    private static readonly (string name, float bottom, float top)[] Storeys =
    {
        ("Ground floor", 3.05f, 5.75f),
        ("Upper floor", 6.05f, 8.75f),
    };

    [MenuItem("Tools/House/Add Reflection Probes")]
    public static void AddReflectionProbes()
    {
        GameObject model = FindModelInstance();
        if (model == null)
        {
            Debug.LogError("House probes: no New_Model in the open scene.");
            return;
        }

        const string groupName = "House Reflection Probes";
        GameObject old = GameObject.Find(groupName);
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        GameObject group = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(group, "Add house reflection probes");

        // Model x is mirrored on import (Unity is left-handed), so the house spans
        // x -23.3 .. -9.8 in the model's space.
        Vector3 min = new Vector3(-23.3f, 0f, -26.5f);
        Vector3 max = new Vector3(-9.8f, 0f, -7.0f);

        var probes = new List<ReflectionProbe>();
        foreach (var storey in Storeys)
        {
            Vector3 localCentre = new Vector3((min.x + max.x) / 2f, (storey.bottom + storey.top) / 2f, (min.z + max.z) / 2f);
            Vector3 localSize = new Vector3(max.x - min.x, storey.top - storey.bottom, max.z - min.z);

            GameObject go = new GameObject(storey.name);
            go.transform.SetParent(group.transform, false);
            go.transform.position = model.transform.TransformPoint(localCentre);

            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = Vector3.Scale(localSize, model.transform.lossyScale);
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;
            probe.blendDistance = 0.5f;
            probes.Add(probe);
        }

        // Bake each probe to its own cubemap file and use that file directly, so the
        // reflections don't depend on the scene's lighting data being baked.
        string sceneName = group.scene.name;
        foreach (ReflectionProbe probe in probes)
        {
            string path = MaterialFolder + "/Reflection_" + sceneName + "_" + probe.name.Replace(' ', '_') + ".exr";
            if (!Lightmapping.BakeReflectionProbe(probe, path))
            {
                Debug.LogError("House probes: baking '" + probe.name + "' failed.");
                continue;
            }

            AssetDatabase.ImportAsset(path);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log("House probes: added and baked one reflection probe per floor. Save the scene.");
    }

    private static GameObject FindModelInstance()
    {
        Object modelAsset = AssetDatabase.LoadMainAssetAtPath(ModelPath);
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (PrefabUtility.GetCorrespondingObjectFromSource(root) == modelAsset)
                return root;
        }
        return null;
    }
}
