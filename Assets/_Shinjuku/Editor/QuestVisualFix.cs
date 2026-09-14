using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestVisualFix
{
    const string Request = "output/quest-optimization-20260913/apply-visual-fix-v9.request";
    const string Output = "output/quest-optimization-20260913/apply-visual-fix-v9.done";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string MaterialFolder = "Assets/_Shinjuku/Quest/GeneratedMaterials/";
    const string BackdropTextureFolder = "Assets/_Shinjuku/Quest/GeneratedTextures";
    const string BackdropTexturePath = BackdropTextureFolder + "/Bill_NightBalanced.png";
    const string BackdropSourcePath = "Assets/model/Texture_Main/Bill_BaseColor.png";
    const string GroundTexturePath = BackdropTextureFolder + "/Ground_NightBalanced.png";
    const string GroundSourcePath = "Assets/model/Texture_Main/ground_BaseColor.png";
    const string StationNameTexturePath = BackdropTextureFolder + "/StationName_Quest.png";
    const string StationNameSourcePath = "Assets/model/Texture_Main/2-Shinjuku-1024_BaseColor.png";
    const string StationNameEmissionPath = "Assets/model/Texture_Main/2-Shinjuku-1024_Emission.png";

    static QuestVisualFix() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        File.Delete(Request);
        if (File.Exists(Output)) File.Delete(Output);
        if (File.Exists(Output + ".failed")) File.Delete(Output + ".failed");
        try { Apply(); }
        catch (Exception exception)
        {
            File.WriteAllText(Output + ".failed", exception.ToString(), new UTF8Encoding(false));
            Debug.LogException(exception);
        }
    }

    static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != QuestScene || SceneManager.sceneCount != 1)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("A non-Quest scene has unsaved changes; refusing to replace it.");
            scene = EditorSceneManager.OpenScene(QuestScene, OpenSceneMode.Single);
        }
        if (scene.path != QuestScene || SceneManager.sceneCount != 1) throw new InvalidOperationException("Could not load TEST_Quest as the only scene.");
        string pcHashBefore = Hash(PcScene);
        Shader treeShader = Shader.Find("Mochie/Standard Lite");
        Shader mobileLightmapped = Shader.Find("VRChat/Mobile/Lightmapped");
        if (!treeShader || !mobileLightmapped) throw new InvalidOperationException("Required Quest visual shaders have not imported yet.");

        var report = new List<string>();
        var treeMaterials = new List<Material>();
        foreach (string file in new[] { "Zelkova_Tree_26cca945.mat", "Zelkova_Tree_08147b16.mat" })
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + file);
            if (!material) throw new InvalidOperationException("Missing material: " + file);
            Texture texture = material.GetTexture("_MainTex");
            Vector2 scale = material.GetTextureScale("_MainTex");
            Vector2 offset = material.GetTextureOffset("_MainTex");
            // The supported particle alpha shader does not render these imported static tree
            // meshes reliably in the Quest client. Restore the cutout shader that is known to
            // draw them, but disable its expensive reflection/specular/normal features.
            material.shader = treeShader;
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureOffset("_MainTex", offset);
            material.SetFloat("_Mode", 1f);
            material.SetFloat("_BlendMode", 1f);
            material.SetFloat("_Cutoff", 0.32f);
            material.SetColor("_Color", new Color(0.68f, 0.72f, 0.62f, 1f));
            material.SetFloat("_Brightness", 0.78f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_MetallicStrength", 0f);
            material.SetFloat("_Glossiness", 0.05f);
            material.SetFloat("_RoughnessStrength", 0.9f);
            material.SetFloat("_BumpScale", 0f);
            material.SetFloat("_EmissionStrength", 0f);
            material.shaderKeywords = new[] { "_ALPHATEST_ON" };
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            treeMaterials.Add(material);
            report.Add($"TreeMaterial={material.name} Shader={material.shader.name} Queue={material.renderQueue}");
        }

        Material bill = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "Bill_d81db392.mat");
        if (!bill) throw new InvalidOperationException("Missing Bill Quest material.");
        Texture billTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackdropSourcePath);
        if (!billTexture) throw new InvalidOperationException("Missing original backdrop texture: " + BackdropSourcePath);
        Vector2 billScale = bill.GetTextureScale("_MainTex");
        Vector2 billOffset = bill.GetTextureOffset("_MainTex");
        Texture2D balancedBackdrop = CreateBalancedTexture(billTexture, BackdropTexturePath);

        // These ten meshes form one low-detail backdrop: some are horizontal road cards and
        // others are vertical building cards. A lit shader therefore made the road bright and
        // the buildings almost black. The supported mobile lightmapped shader is unlit for
        // these non-baked cards, while the Quest-only texture supplies a restrained night tone.
        bill.shader = mobileLightmapped;
        bill.SetTexture("_MainTex", balancedBackdrop);
        bill.SetTextureScale("_MainTex", billScale);
        bill.SetTextureOffset("_MainTex", billOffset);
        bill.shaderKeywords = Array.Empty<string>();
        bill.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        EditorUtility.SetDirty(bill);
        report.Add($"BackdropMaterial={bill.name} Shader={bill.shader.name} Texture={AssetDatabase.GetAssetPath(balancedBackdrop)}");

        Material ground = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "ground_57a3cae8.mat");
        Texture groundSource = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundSourcePath);
        if (!ground || !groundSource) throw new InvalidOperationException("Missing Quest ground material or original ground texture.");
        Vector2 groundScale = ground.GetTextureScale("_MainTex");
        Vector2 groundOffset = ground.GetTextureOffset("_MainTex");
        Texture2D balancedGround = CreateBalancedTexture(groundSource, GroundTexturePath);
        ground.shader = mobileLightmapped;
        ground.SetTexture("_MainTex", balancedGround);
        ground.SetTextureScale("_MainTex", groundScale);
        ground.SetTextureOffset("_MainTex", groundOffset);
        ground.shaderKeywords = Array.Empty<string>();
        ground.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        EditorUtility.SetDirty(ground);
        report.Add($"GroundMaterial={ground.name} Shader={ground.shader.name} Texture={AssetDatabase.GetAssetPath(balancedGround)}");

        Material stationName = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "2-Shinjuku-1024_6b404c5f.mat");
        Texture2D stationBase = AssetDatabase.LoadAssetAtPath<Texture2D>(StationNameSourcePath);
        Texture2D stationEmission = AssetDatabase.LoadAssetAtPath<Texture2D>(StationNameEmissionPath);
        if (!stationName || !stationBase || !stationEmission)
            throw new InvalidOperationException("Missing Quest station-name material or source textures.");
        Vector2 stationScale = stationName.GetTextureScale("_MainTex");
        Vector2 stationOffset = stationName.GetTextureOffset("_MainTex");
        // The emission atlas has bright white/green faces and dark extruded sides. The
        // base-colour atlas has white padding that incorrectly makes the sides white.
        Texture2D stationTexture = stationEmission;

        // The original material relies on an emissive metallic setup. The first Quest pass used
        // Toon Lit, but this mesh carries black vertex colours and Toon Lit multiplies them into
        // the texture. Use the supported lightmapped shader while explicitly opting this one
        // renderer out of its baked lightmap; the Quest-only texture then reads as a stable,
        // restrained self-lit sign without metallic reflections or black vertex tinting.
        stationName.shader = mobileLightmapped;
        stationName.SetTexture("_MainTex", stationTexture);
        stationName.SetTextureScale("_MainTex", stationScale);
        stationName.SetTextureOffset("_MainTex", stationOffset);
        stationName.shaderKeywords = Array.Empty<string>();
        stationName.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        EditorUtility.SetDirty(stationName);
        report.Add($"StationNameMaterial={stationName.name} Shader={stationName.shader.name} Texture={AssetDatabase.GetAssetPath(stationTexture)}");

        AssetDatabase.SaveAssets();
        int treeRenderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Count(renderer => renderer.sharedMaterials.Any(material => material && treeMaterials.Contains(material)));
        int billRenderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Count(renderer => renderer.sharedMaterials.Any(material => material == bill));
        int groundRenderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Count(renderer => renderer.sharedMaterials.Any(material => material == ground));
        Renderer[] stationRenderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => renderer.sharedMaterials.Any(material => material == stationName)).ToArray();
        foreach (Renderer renderer in stationRenderers)
        {
            renderer.lightmapIndex = -1;
            renderer.realtimeLightmapIndex = -1;
            EditorUtility.SetDirty(renderer);
        }
        int stationNameRenderers = stationRenderers.Length;
        if (stationNameRenderers != 1) throw new InvalidOperationException("Expected exactly one Quest station-name renderer, found " + stationNameRenderers + ".");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        string pcHashAfter = Hash(PcScene);
        if (pcHashBefore != pcHashAfter) throw new InvalidOperationException("PC scene changed during Quest-only visual fix.");
        report.Add($"TreeRenderers={treeRenderers} BillRenderers={billRenderers} GroundRenderers={groundRenderers} StationNameRenderers={stationNameRenderers}");
        report.Add("PCSceneHashBefore=" + pcHashBefore);
        report.Add("PCSceneHashAfter=" + pcHashAfter);
        File.WriteAllText(Output, string.Join("\n", report), new UTF8Encoding(false));
    }

    static Texture2D CreateBalancedTexture(Texture source, string outputPath)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (!(AssetImporter.GetAtPath(sourcePath) is TextureImporter))
            throw new InvalidOperationException("Backdrop source is not an imported texture: " + sourcePath);

        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            Color32[] pixels = readable.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = BalancePixel(pixels[i]);
            }

            readable.SetPixels32(pixels);
            readable.Apply(false, false);
            if (!AssetDatabase.IsValidFolder(BackdropTextureFolder))
                AssetDatabase.CreateFolder("Assets/_Shinjuku/Quest", "GeneratedTextures");
            File.WriteAllBytes(outputPath, readable.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readable) UnityEngine.Object.DestroyImmediate(readable);
        }

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
        var outputImporter = (TextureImporter)AssetImporter.GetAtPath(outputPath);
        outputImporter.sRGBTexture = true;
        outputImporter.mipmapEnabled = true;
        outputImporter.wrapMode = TextureWrapMode.Clamp;
        outputImporter.filterMode = FilterMode.Bilinear;
        outputImporter.textureCompression = TextureImporterCompression.Compressed;
        outputImporter.maxTextureSize = 2048;
        var android = outputImporter.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 1024;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 50;
        outputImporter.SetPlatformTextureSettings(android);
        outputImporter.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    static Color32 BalancePixel(Color32 pixel)
    {
        float red = pixel.r / 255f;
        float green = pixel.g / 255f;
        float blue = pixel.b / 255f;
        float luminance = red * 0.2126f + green * 0.7152f + blue * 0.0722f;

        // Preserve the building midtones, but increasingly suppress atlas highlights. The
        // road pixels sit in the upper luminance band and receive roughly half the brightness
        // of equally adjusted building pixels.
        float highlight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.82f, luminance));
        float highlightFactor = Mathf.Lerp(1f, 0.46f, highlight);
        pixel.r = BalanceChannel(red, highlightFactor);
        pixel.g = BalanceChannel(green, highlightFactor);
        pixel.b = BalanceChannel(blue, highlightFactor);
        return pixel;
    }

    static Texture2D CreateStationNameTexture(Texture baseTexture, Texture emissionTexture, string outputPath)
    {
        int width = Mathf.Min(1024, baseTexture.width);
        int height = Mathf.Min(1024, baseTexture.height);
        Color32[] basePixels = ReadPixels(baseTexture, width, height);
        Color32[] emissionPixels = ReadPixels(emissionTexture, width, height);
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        try
        {
            for (int i = 0; i < basePixels.Length; i++)
            {
                Color baseColor = basePixels[i];
                Color emission = emissionPixels[i];
                float mask = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                // Bake only a restrained amount of the unavailable Quest emission into the
                // albedo. This closes the face/edge brightness gap without turning the sign
                // into a white glowing card.
                Color baked = new Color(
                    Mathf.Max(baseColor.r, emission.r * 0.86f),
                    Mathf.Max(baseColor.g, emission.g * 0.82f),
                    Mathf.Max(baseColor.b, emission.b * 0.72f),
                    baseColor.a);
                output.SetPixel(i % width, i / width, Color.Lerp(baseColor, baked, mask));
            }
            output.Apply(false, false);
            if (!AssetDatabase.IsValidFolder(BackdropTextureFolder))
                AssetDatabase.CreateFolder("Assets/_Shinjuku/Quest", "GeneratedTextures");
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(output); }

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(outputPath);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.maxTextureSize = 1024;
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 512;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.compressionQuality = 50;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    static Color32[] ReadPixels(Texture source, int width, int height)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readable) UnityEngine.Object.DestroyImmediate(readable);
        }
    }

    static byte BalanceChannel(float normalized, float highlightFactor)
    {
        float balanced = (0.009f + 0.27f * Mathf.Pow(normalized, 0.94f)) * highlightFactor;
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(balanced) * 255f);
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
