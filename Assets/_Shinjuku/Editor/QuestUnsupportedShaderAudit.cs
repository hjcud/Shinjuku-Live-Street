using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class QuestUnsupportedShaderAudit
{
    const string Request = "output/quest-optimization-20260913/unsupported-shaders-audit.request";
    const string Report = "output/quest-optimization-20260913/unsupported-shaders-audit.txt";
    static readonly HashSet<string> WarningShaders = new HashSet<string>
    {
        "Hidden/lilToonTransparent",
        "HoshinoLabs/iwaSync3/Realtime Emissive Gamma",
        "Mochie/LED Screen",
        "Mochie/Standard Lite",
        "Shinjuku/Quest/Lightmapped Cutout",
        "Shinjuku/Quest/Unlit Tinted",
        "Shinjuku/Quest Placement Guide",
        "Shinjuku/Speaker Placement Guide",
        "Shinjuku/Speaker Ripple Guide",
        "Standard",
        "Unlit/Color",
        "Unlit/GammaCorrectedUnlitTexture",
        "Video/RealtimeEmissiveGamma"
    };

    static QuestUnsupportedShaderAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Audit(); }
        catch (Exception exception) { File.WriteAllText(Report + ".failed", exception.ToString(), new UTF8Encoding(false)); }
    }

    static void Audit()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity") throw new InvalidOperationException("TEST_Quest must be active.");
        var roots = scene.GetRootGameObjects();
        var renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
        var graphics = roots.SelectMany(root => root.GetComponentsInChildren<Graphic>(true)).ToArray();
        var dependencies = EditorUtility.CollectDependencies(roots.Cast<UnityEngine.Object>().ToArray());
        var materials = dependencies.OfType<Material>().Where(material => material && material.shader && WarningShaders.Contains(material.shader.name)).Distinct().OrderBy(material => material.shader.name).ThenBy(material => material.name).ToArray();
        var output = new StringBuilder();
        output.AppendLine($"Scene={scene.path} WarningShaderMaterials={materials.Length}");
        foreach (var group in materials.GroupBy(material => material.shader.name))
        {
            output.AppendLine();
            output.AppendLine("SHADER " + group.Key);
            foreach (var material in group)
            {
                output.AppendLine($"  MATERIAL {material.name} | {AssetDatabase.GetAssetPath(material)}");
                foreach (var renderer in renderers.Where(renderer => renderer.sharedMaterials.Contains(material)))
                    output.AppendLine($"    RENDERER active={renderer.gameObject.activeInHierarchy} type={renderer.GetType().Name} path={PathOf(renderer.transform)}");
                foreach (var graphic in graphics.Where(graphic => graphic.material == material))
                    output.AppendLine($"    GRAPHIC active={graphic.gameObject.activeInHierarchy} type={graphic.GetType().Name} path={PathOf(graphic.transform)}");
            }
        }
        File.WriteAllText(Report, output.ToString(), new UTF8Encoding(false));
    }

    static string PathOf(Transform transform)
    {
        var parts = new List<string>();
        while (transform) { parts.Add(transform.name); transform = transform.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
