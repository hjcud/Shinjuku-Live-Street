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
public static class QuestLightingFix
{
    const string Request = "output/quest-optimization-20260913/apply-lighting-fix-v2.request";
    const string Output = "output/quest-optimization-20260913/apply-lighting-fix-v2.done";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string QuestMaterials = "Assets/_Shinjuku/Quest/GeneratedMaterials/";
    static readonly HashSet<string> ExcludedMaterials = new HashSet<string>(StringComparer.Ordinal)
    {
        "2-Shinjuku-1024_6b404c5f", // handled separately by QuestVisualFix
        "Bill_d81db392",             // night-balanced backdrop texture
        "ground_57a3cae8"            // night-balanced distant ground texture
    };

    static QuestLightingFix() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
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

    [MenuItem("Tools/Shinjuku/Quest/Restore Baked Building Materials")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != QuestScene) throw new InvalidOperationException("Open TEST_Quest first. No other scene will be changed.");
        string pcHashBefore = Hash(PcScene);
        var pcMaterials = AssetDatabase.GetDependencies(PcScene, true)
            .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).ToDictionary(path => path, Hash);
        Shader lightmapped = Shader.Find("VRChat/Mobile/Lightmapped");
        Shader standardLite = Shader.Find("VRChat/Mobile/Standard Lite");
        if (!lightmapped || !standardLite) throw new InvalidOperationException("Required VRChat mobile shaders have not imported.");

        Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
        Material[] candidates = renderers.SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material && material.shader == standardLite && material.IsKeywordEnabled("_EMISSION"))
            .Where(material => AssetDatabase.GetAssetPath(material).StartsWith(QuestMaterials, StringComparison.Ordinal))
            .Where(material => !ExcludedMaterials.Contains(material.name))
            .Distinct().ToArray();

        string backup = "output/quest-optimization-20260913/black-surface/material-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup);
        var converted = new List<Material>();
        var view = SceneView.lastActiveSceneView;
        if (view && view.camera) QuestBlackSurfaceAudit.Capture(view.camera, "output/quest-optimization-20260913/black-surface/buildings-before-repair.png");
        foreach (Material material in candidates)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (pcMaterials.ContainsKey(path)) throw new InvalidOperationException("Material is shared with PC: " + path);
            // Restrict rollback to the exact emission configuration introduced by v1.
            Color emission = material.GetColor("_EmissionColor");
            if (Mathf.Abs(emission.r - .70f) > .0001f || Mathf.Abs(emission.g - .62f) > .0001f || Mathf.Abs(emission.b - .52f) > .0001f) continue;
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
            // Preserve main texture/ST and stored source properties. Only undo the shader swap.
            // The existing bake already contains the warm architectural illumination.
            material.shader = lightmapped;
            material.shaderKeywords = Array.Empty<string>();
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            converted.Add(material);
        }
        if (view && view.camera) QuestBlackSurfaceAudit.Capture(view.camera, "output/quest-optimization-20260913/black-surface/buildings-after-repair.png");
        SceneView.RepaintAll();
        int affectedRenderers = renderers.Count(renderer => renderer.sharedMaterials.Any(material => material && converted.Contains(material)));
        int lightmappedRenderers = renderers.Count(renderer => renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534);
        string pcHashAfter = Hash(PcScene);
        if (pcHashBefore != pcHashAfter) throw new InvalidOperationException("PC scene changed during Quest-only lighting restoration.");
        foreach (var entry in pcMaterials)
            if (Hash(entry.Key) != entry.Value) throw new InvalidOperationException("PC material changed: " + entry.Key);

        var report = new List<string>
        {
            "QuestScene=" + QuestScene,
            $"RestoredLightmappedMaterials={converted.Count} AffectedRenderers={affectedRenderers}",
            "Backup=" + backup,
            "PCMaterialsVerifiedUnchanged=" + pcMaterials.Count,
            $"LightmapTextures={LightmapSettings.lightmaps.Length} LightmappedRenderers={lightmappedRenderers}",
            $"RuntimeLights={scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).Count()} (PC sources were baked-only)",
            "PCSceneHashBefore=" + pcHashBefore,
            "PCSceneHashAfter=" + pcHashAfter
        };
        report.AddRange(converted.Select(m => "Restored=" + AssetDatabase.GetAssetPath(m)));
        File.WriteAllText(Output, string.Join("\n", report), new UTF8Encoding(false));
    }

    static Texture SavedTexture(Material material, string propertyName)
    {
        var serialized = new SerializedObject(material);
        SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
        if (textures == null || !textures.isArray) return null;
        for (int i = 0; i < textures.arraySize; i++)
        {
            SerializedProperty entry = textures.GetArrayElementAtIndex(i);
            SerializedProperty name = entry.FindPropertyRelative("first");
            if (name == null || name.stringValue != propertyName) continue;
            SerializedProperty texture = entry.FindPropertyRelative("second.m_Texture");
            return texture == null ? null : texture.objectReferenceValue as Texture;
        }
        return null;
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
