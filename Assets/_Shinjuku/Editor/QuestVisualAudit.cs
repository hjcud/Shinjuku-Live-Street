using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestVisualAudit
{
    const string Request = "output/quest-optimization-20260913/visual-audit-v2.request";
    const string Report = "output/quest-optimization-20260913/visual-audit-v2.txt";

    static QuestVisualAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity") throw new InvalidOperationException("TEST_Quest must be active.");
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            var mobileLightmapped = renderers.Where(renderer => renderer.sharedMaterials.Any(material => material && material.shader && material.shader.name == "VRChat/Mobile/Lightmapped")).ToArray();
            var noLightmap = mobileLightmapped.Where(renderer => renderer.lightmapIndex < 0 || renderer.lightmapIndex >= 0xFFFE).ToArray();
            var trees = renderers.Where(renderer => renderer.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    renderer.name.IndexOf("zelkova", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    renderer.sharedMaterials.Any(material => material && material.name.IndexOf("zelkova", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();

            var text = new StringBuilder();
            text.AppendLine("Scene=" + scene.path);
            text.AppendLine("Lightmaps=" + (LightmapSettings.lightmaps == null ? 0 : LightmapSettings.lightmaps.Length));
            text.AppendLine($"Renderers={renderers.Length} MobileLightmappedRenderers={mobileLightmapped.Length} MobileLightmappedWithoutBakedIndex={noLightmap.Length}");
            text.AppendLine("TREE RENDERERS");
            foreach (var renderer in trees)
            {
                text.AppendLine($"{PathOf(renderer.transform)} | active={renderer.gameObject.activeInHierarchy} lightmap={renderer.lightmapIndex} scaleOffset={renderer.lightmapScaleOffset} | {Materials(renderer)}");
            }
            text.AppendLine("LARGEST MOBILE/LIGHTMAPPED RENDERERS WITHOUT BAKED INDEX");
            foreach (var renderer in noLightmap.OrderByDescending(renderer => renderer.bounds.size.sqrMagnitude).Take(80))
            {
                text.AppendLine($"{PathOf(renderer.transform)} | active={renderer.gameObject.activeInHierarchy} size={renderer.bounds.size} | {Materials(renderer)}");
            }
            text.AppendLine("BILL BACKDROP RENDERERS");
            foreach (var renderer in renderers.Where(renderer => renderer.sharedMaterials.Any(material => material && material.name.StartsWith("Bill_"))))
            {
                text.AppendLine($"{PathOf(renderer.transform)} | active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} pos={renderer.transform.position} rot={renderer.transform.eulerAngles} localScale={renderer.transform.lossyScale} size={renderer.bounds.size} lightmap={renderer.lightmapIndex} | {Materials(renderer)}");
            }
            File.WriteAllText(Report, text.ToString(), new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Report + ".failed", exception.ToString(), new UTF8Encoding(false));
        }
    }

    static string Materials(Renderer renderer)
    {
        return string.Join("; ", renderer.sharedMaterials.Where(material => material).Select(material =>
        {
            string mainTexture = material.mainTexture ? AssetDatabase.GetAssetPath(material.mainTexture) : "<none>";
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : -1f;
            return $"mat={material.name} shader={(material.shader ? material.shader.name : "<none>")} queue={material.renderQueue} cutoff={cutoff:0.###} tex={mainTexture}";
        }));
    }

    static string PathOf(Transform transform)
    {
        var parts = new List<string>();
        while (transform) { parts.Add(transform.name); transform = transform.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
