using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestUploadPreparation
{
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string QuestRoot = "Assets/_Shinjuku/Quest";
    const string QuestMaterials = QuestRoot + "/GeneratedMaterials";
    const string Output = "output/quest-optimization-20260913/";
    const string PrepareRequest = Output + "prepare-upload.request";
    const string SwitchRequest = Output + "switch-android.request";
    const string SwitchSessionKey = "Shinjuku.Quest.SwitchingAndroid";

    static readonly string[] RemovePaths =
    {
        "00_World/VRCShinjuku_Rinasciita",
        "00_World/shinjuku_Sio",
        "10_Lighting/noribenSkylightWindowLightShaft",
        "10_Lighting/Reflection Probe Group",
        "20_Systems/TrafficSystem_V2",
        "30_Interactables/Mirror Group",
        "30_Interactables/musical instruments",
        "40_Displays/Posters",
        "90_Cameras"
    };

    static QuestUploadPreparation()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(PrepareRequest)) Prepare();
        else if (File.Exists(SwitchRequest)) SwitchAndroid();
    }

    static void Prepare()
    {
        File.Delete(PrepareRequest);
        Directory.CreateDirectory(Output);
        string failed = Output + "prepare-upload.failed";
        if (File.Exists(failed)) File.Delete(failed);

        try
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows)
                throw new InvalidOperationException("Preparation must begin from the Windows target so the PC baseline can be verified.");
            if (!File.Exists(PcScene) || !File.Exists(QuestScene)) throw new FileNotFoundException("PC or Quest scene is missing.");
            if (AssetDatabase.IsValidFolder(QuestRoot)) throw new InvalidOperationException(QuestRoot + " already exists; refusing to overwrite it.");

            var mobileLightmapped = Shader.Find("VRChat/Mobile/Lightmapped");
            if (mobileLightmapped == null) throw new InvalidOperationException("VRChat mobile Lightmapped shader was not found.");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Open scene save was cancelled.");
            string pcHashBefore = HashFile(PcScene);
            string questHashBefore = HashFile(QuestScene);
            string recovery = Output + "TEST_Quest.before-mobile-prep-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(QuestScene, recovery, false);

            AssetDatabase.CreateFolder("Assets/_Shinjuku", "Quest");
            AssetDatabase.CreateFolder(QuestRoot, "GeneratedMaterials");

            var scene = EditorSceneManager.OpenScene(QuestScene, OpenSceneMode.Single);
            var removed = new List<string>();
            foreach (string path in RemovePaths)
            {
                var target = Find(scene, path);
                if (target == null) throw new InvalidOperationException("Expected Quest-only removal target was not found: " + path);
                UnityEngine.Object.DestroyImmediate(target);
                removed.Add(path);
            }

            var particleObjects = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<ParticleSystem>(true))
                .Select(p => p.gameObject).Distinct().ToArray();
            foreach (var go in particleObjects) UnityEngine.Object.DestroyImmediate(go);

            var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var originals = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            var originalMaterialHashes = originals.Select(AssetDatabase.GetAssetPath).Where(File.Exists).Distinct()
                .ToDictionary(p => p, HashFile);
            var materialMap = new Dictionary<Material, Material>();
            int converted = 0;
            int preservedShader = 0;

            foreach (var original in originals)
            {
                var clone = new Material(original) { name = original.name + "_Quest", enableInstancing = true };
                if (ShouldUseMobileLightmapped(original))
                {
                    Texture mainTexture = original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") : null;
                    Color color = original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
                    clone.shader = mobileLightmapped;
                    if (clone.HasProperty("_MainTex")) clone.SetTexture("_MainTex", mainTexture);
                    if (clone.HasProperty("_Color")) clone.SetColor("_Color", color);
                    converted++;
                }
                else preservedShader++;

                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
                if (string.IsNullOrEmpty(guid)) guid = original.GetInstanceID().ToString("X8");
                string assetName = Sanitize(original.name, 56) + "_" + guid.Substring(0, Math.Min(8, guid.Length)) + ".mat";
                string assetPath = QuestMaterials + "/" + assetName;
                AssetDatabase.CreateAsset(clone, assetPath);
                materialMap.Add(original, clone);
            }

            int rendererOverrides = 0;
            foreach (var renderer in renderers)
            {
                var source = renderer.sharedMaterials;
                var replacements = new Material[source.Length];
                bool changed = false;
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] != null && materialMap.TryGetValue(source[i], out var replacement))
                    {
                        replacements[i] = replacement;
                        changed = true;
                    }
                    else replacements[i] = source[i];
                }
                if (changed)
                {
                    renderer.sharedMaterials = replacements;
                    rendererOverrides++;
                    EditorUtility.SetDirty(renderer);
                }
            }

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = Mathf.Min(RenderSettings.reflectionIntensity, 0.55f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] dependencies = AssetDatabase.GetDependencies(QuestScene, true).Distinct().ToArray();
            int textureOverrides = 0;
            int audioOverrides = 0;
            foreach (string dependency in dependencies)
            {
                if (AssetImporter.GetAtPath(dependency) is TextureImporter textureImporter)
                {
                    ApplyAndroidTextureOverride(textureImporter, dependency);
                    textureOverrides++;
                }
                else if (AssetImporter.GetAtPath(dependency) is AudioImporter audioImporter)
                {
                    ApplyAndroidAudioOverride(audioImporter, dependency);
                    audioOverrides++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var pair in originalMaterialHashes)
                if (!File.Exists(pair.Key) || HashFile(pair.Key) != pair.Value)
                    throw new InvalidOperationException("A PC/shared source material changed unexpectedly: " + pair.Key);
            string pcHashAfter = HashFile(PcScene);
            if (pcHashAfter != pcHashBefore) throw new InvalidOperationException("PC scene changed during Quest preparation.");

            var finalRenderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            long triangles = finalRenderers.Select(RendererMesh).Where(m => m != null).Sum(MeshTriangles);
            long activeTriangles = finalRenderers.Where(r => r.gameObject.activeInHierarchy && r.enabled)
                .Select(RendererMesh).Where(m => m != null).Sum(MeshTriangles);
            var remainingMaterials = finalRenderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            int originalMaterialRefs = remainingMaterials.Count(m => !AssetDatabase.GetAssetPath(m).StartsWith(QuestMaterials + "/", StringComparison.Ordinal));
            string questHashAfter = HashFile(QuestScene);

            string report =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " QUEST UPLOAD PREPARATION PASS\n" +
                "Scene=" + QuestScene + "\n" +
                "Recovery=" + recovery + "\n" +
                "PCSceneHashBefore=" + pcHashBefore + "\n" +
                "PCSceneHashAfter=" + pcHashAfter + "\n" +
                "QuestSceneHashBefore=" + questHashBefore + "\n" +
                "QuestSceneHashAfter=" + questHashAfter + "\n" +
                "Removed=" + string.Join(", ", removed) + "\n" +
                $"RemovedParticleObjects={particleObjects.Length}\n" +
                $"TrianglesSerialized={triangles} TrianglesActive={activeTriangles}\n" +
                $"Renderers={finalRenderers.Length} QuestMaterials={remainingMaterials.Length} OriginalMaterialRefs={originalMaterialRefs}\n" +
                $"MaterialAssetsCreated={materialMap.Count} MobileLightmapped={converted} PreservedSpecialShaders={preservedShader} RendererOverrides={rendererOverrides}\n" +
                $"AndroidTextureOverrides={textureOverrides} AndroidAudioOverrides={audioOverrides} Dependencies={dependencies.Length}\n" +
                "Original materials and TEST_PC scene hashes verified unchanged. Android overrides do not alter Standalone import settings.\n";
            File.WriteAllText(Output + "prepare-upload.done", report, new UTF8Encoding(false));
            File.WriteAllText(SwitchRequest, "switch", new UTF8Encoding(false));
            Debug.Log("[QuestUploadPreparation] Prepared Quest-only scene. Android switch queued.\n" + report);
        }
        catch (Exception ex)
        {
            File.WriteAllText(failed, ex.ToString(), new UTF8Encoding(false));
            Debug.LogException(ex);
        }
    }

    static void SwitchAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            try
            {
                SessionState.EraseBool(SwitchSessionKey);
                if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
                {
                    PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                    return;
                }
                if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                {
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    return;
                }
                if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
                {
                    PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                    return;
                }
                var graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                if (graphicsApis.Length != 1 || graphicsApis[0] != GraphicsDeviceType.OpenGLES3)
                {
                    PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
                    return;
                }
                AssetDatabase.SaveAssets();
                File.Delete(SwitchRequest);
                File.WriteAllText(Output + "switch-android.done",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " PASS\nActiveBuildTarget=Android\nScene=" + SceneManager.GetActiveScene().path +
                    "\nScriptingBackend=IL2CPP\nArchitecture=ARM64\nGraphicsAPI=OpenGLES3\n", new UTF8Encoding(false));
                Debug.Log("[QuestUploadPreparation] Android target is ready for VRChat SDK validation/upload.");
            }
            catch (Exception ex)
            {
                File.Delete(SwitchRequest);
                File.WriteAllText(Output + "switch-android.failed", ex.ToString(), new UTF8Encoding(false));
                Debug.LogException(ex);
            }
            return;
        }

        if (SessionState.GetBool(SwitchSessionKey, false)) return;
        SessionState.SetBool(SwitchSessionKey, true);
        bool started = EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
        if (!started)
        {
            SessionState.EraseBool(SwitchSessionKey);
            File.Delete(SwitchRequest);
            File.WriteAllText(Output + "switch-android.failed", "Unity refused to start Android target switching.");
        }
    }

    static void ApplyAndroidTextureOverride(TextureImporter importer, string path)
    {
        int desired = IsDetailTexture(path, importer) ? 512 : 1024;
        var settings = importer.GetPlatformTextureSettings("Android");
        settings.name = "Android";
        settings.overridden = true;
        settings.maxTextureSize = desired;
        settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        settings.textureCompression = TextureImporterCompression.Compressed;
        settings.compressionQuality = 50;
        settings.crunchedCompression = false;
        settings.format = importer.textureType == TextureImporterType.Sprite ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(settings);
        importer.SaveAndReimport();
    }

    static void ApplyAndroidAudioOverride(AudioImporter importer, string path)
    {
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.45f;
        settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
        string lower = path.ToLowerInvariant();
        settings.loadType = lower.Contains("ambience") || lower.Contains("ambient")
            ? AudioClipLoadType.Streaming
            : AudioClipLoadType.CompressedInMemory;
        importer.SetOverrideSampleSettings("Android", settings);
        importer.SaveAndReimport();
    }

    static bool IsDetailTexture(string path, TextureImporter importer)
    {
        string lower = path.ToLowerInvariant();
        return importer.textureType == TextureImporterType.NormalMap || lower.Contains("normal") || lower.Contains("_n.") ||
               lower.Contains("rough") || lower.Contains("metal") || lower.Contains("mask") || lower.Contains("occlusion") ||
               lower.Contains("_ao") || lower.Contains("emission");
    }

    static bool ShouldUseMobileLightmapped(Material material)
    {
        if (material.shader == null || material.renderQueue > 2450) return false;
        string shader = material.shader.name;
        if (shader != "Standard" && shader != "Mochie/Standard" && shader != "Mochie/Standard Lite" &&
            shader != "Legacy Shaders/Diffuse") return false;
        if (material.HasProperty("_Mode") && material.GetFloat("_Mode") > 0.1f) return false;
        return true;
    }

    static GameObject Find(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject current = scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0]);
        if (current == null) return null;
        for (int i = 1; i < parts.Length; i++)
        {
            var child = current.transform.Cast<Transform>().FirstOrDefault(t => t.name == parts[i]);
            if (child == null) return null;
            current = child.gameObject;
        }
        return current;
    }

    static Mesh RendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    static long MeshTriangles(Mesh mesh)
    {
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i) / 3L;
        return count;
    }

    static string HashFile(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    static string Sanitize(string value, int maxLength)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        value = value.Replace('/', '_').Replace('\\', '_').Trim();
        if (string.IsNullOrEmpty(value)) value = "Material";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
