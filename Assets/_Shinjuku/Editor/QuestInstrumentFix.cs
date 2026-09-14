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
public static class QuestInstrumentFix
{
    // Restores the PC shop display into the Quest-only scene without touching PC assets.
    const string Request = "output/quest-optimization-20260913/apply-instrument-fix-v2.request";
    const string Output = "output/quest-optimization-20260913/apply-instrument-fix-v2.done";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string InstrumentRoot = "Assets/Atelier_Rayrell/01_Quill_Instruments/";
    const string MaterialFolder = "Assets/_Shinjuku/Quest/GeneratedMaterials/Instrument";

    sealed class Record
    {
        public string prefabPath;
        public string parentPath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public int siblingIndex;
        public bool active;
    }

    static QuestInstrumentFix() { EditorApplication.update += Poll; }

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

    static void Apply()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes; refusing to replace the active scene.");

        string pcHashBefore = Hash(PcScene);
        Scene pc = EditorSceneManager.OpenScene(PcScene, OpenSceneMode.Single);
        Transform source = Find(pc, "30_Interactables/musical instruments");
        if (!source) throw new InvalidOperationException("The PC musical-instruments hierarchy was not found.");
        Vector3 localPosition = source.localPosition;
        Quaternion localRotation = source.localRotation;
        Vector3 localScale = source.localScale;
        int siblingIndex = source.GetSiblingIndex();
        bool active = source.gameObject.activeSelf;

        Scene quest = EditorSceneManager.OpenScene(QuestScene, OpenSceneMode.Additive);
        Transform questParent = Find(quest, "30_Interactables");
        if (!questParent) throw new InvalidOperationException("Quest 30_Interactables root was not found.");
        Shader mobileShader = Shader.Find("VRChat/Mobile/Standard Lite");
        if (!mobileShader) throw new InvalidOperationException("VRChat Mobile Standard Lite has not imported.");
        EnsureFolder(MaterialFolder);

        var report = new List<string>();
        var materialCache = new Dictionary<Material, Material>();
        Transform oldQuestCopy = Find(quest, "30_Interactables/musical instruments");
        if (oldQuestCopy) UnityEngine.Object.DestroyImmediate(oldQuestCopy.gameObject);
        GameObject instance = UnityEngine.Object.Instantiate(source.gameObject);
        instance.name = source.name;
        SceneManager.MoveGameObjectToScene(instance, quest);
        instance.transform.SetParent(questParent, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        instance.transform.localScale = localScale;
        instance.transform.SetSiblingIndex(Mathf.Min(siblingIndex, questParent.childCount - 1));
        instance.SetActive(active);

        int rendererCount = 0;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i]) materials[i] = GetQuestMaterial(materials[i], mobileShader, materialCache);
            renderer.sharedMaterials = materials;
            renderer.enabled = true;
            rendererCount++;
        }

        EditorSceneManager.MarkSceneDirty(quest);
        EditorSceneManager.SaveScene(quest);
        SceneManager.SetActiveScene(quest);
        EditorSceneManager.CloseScene(pc, true);
        AssetDatabase.SaveAssets();
        string pcHashAfter = Hash(PcScene);
        if (pcHashBefore != pcHashAfter) throw new InvalidOperationException("PC scene changed during Quest-only instrument restoration.");
        report.Add($"RestoredHierarchy=30_Interactables/musical instruments Renderers={rendererCount} QuestMaterials={materialCache.Count}");
        report.Add("PCSceneHashBefore=" + pcHashBefore);
        report.Add("PCSceneHashAfter=" + pcHashAfter);
        File.WriteAllText(Output, string.Join("\n", report), new UTF8Encoding(false));
    }

    static Material GetQuestMaterial(Material source, Shader shader, Dictionary<Material, Material> cache)
    {
        if (cache.TryGetValue(source, out Material cached)) return cached;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string guid = string.IsNullOrEmpty(sourcePath) ? source.name.GetHashCode().ToString("X8") : AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8);
        string safeName = string.Concat(source.name.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string outputPath = MaterialFolder + "/" + safeName + "_" + guid + ".mat";
        Material result = AssetDatabase.LoadAssetAtPath<Material>(outputPath);
        if (!result)
        {
            result = new Material(shader) { name = safeName + "_Quest" };
            AssetDatabase.CreateAsset(result, outputPath);
        }
        result.shader = shader;
        Texture main = FirstTexture(source, "_MainTex", "_BaseMap", "_BaseColorMap");
        Texture normal = FirstTexture(source, "_BumpMap", "_NormalMap");
        if (main) result.SetTexture("_MainTex", main);
        if (source.HasProperty("_MainTex"))
        {
            result.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
            result.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
        }
        if (result.HasProperty("_Color")) result.SetColor("_Color", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
        if (normal && result.HasProperty("_BumpMap")) result.SetTexture("_BumpMap", normal);
        if (result.HasProperty("_BumpScale")) result.SetFloat("_BumpScale", normal ? 0.45f : 0f);
        if (result.HasProperty("_Metallic")) result.SetFloat("_Metallic", 0.05f);
        if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", 0.22f);
        result.shaderKeywords = normal ? new[] { "_NORMALMAP" } : Array.Empty<string>();
        result.SetOverrideTag("RenderType", "Opaque");
        result.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        EditorUtility.SetDirty(result);
        cache[source] = result;
        return result;
    }

    static Texture FirstTexture(Material material, params string[] properties)
    {
        foreach (string property in properties)
            if (material.HasProperty(property) && material.GetTexture(property)) return material.GetTexture(property);
        return null;
    }

    static GameObject FindExistingInstrument(Transform parent, string prefabPath, Vector3 localPosition)
    {
        foreach (Transform child in parent)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
            if (string.Equals(path, prefabPath, StringComparison.OrdinalIgnoreCase) && Vector3.SqrMagnitude(child.localPosition - localPosition) < 0.0001f)
                return child.gameObject;
        }
        return null;
    }

    static Transform Find(Scene scene, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string[] parts = path.Split('/');
        Transform current = scene.GetRootGameObjects().FirstOrDefault(root => root.name == parts[0])?.transform;
        for (int i = 1; current && i < parts.Length; i++) current = current.Find(parts[i]);
        return current;
    }

    static string Path(Transform transform)
    {
        if (!transform) return string.Empty;
        var names = new Stack<string>();
        while (transform) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }

    static void EnsureFolder(string folder)
    {
        string current = "Assets";
        foreach (string part in folder.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
