using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestLightingAudit
{
    const string Request = "output/quest-optimization-20260913/lighting-audit-v1.request";
    const string Output = "output/quest-optimization-20260913/lighting-audit-v1.txt";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";

    static QuestLightingAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        try { Run(); }
        catch (Exception exception)
        {
            File.WriteAllText(Output + ".failed", exception.ToString(), new UTF8Encoding(false));
            Debug.LogException(exception);
        }
    }

    static void Run()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes; lighting audit aborted.");
        var report = new List<string>();
        Audit(PcScene, "PC", report);
        Audit(QuestScene, "QUEST", report);
        File.WriteAllText(Output, string.Join("\n", report), new UTF8Encoding(false));
    }

    static void Audit(string path, string label, List<string> report)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
        Light[] lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        LightingSettings settings = Lightmapping.lightingSettings;
        report.Add("=== " + label + " ===");
        report.Add("Scene=" + path);
        report.Add($"AmbientMode={RenderSettings.ambientMode} AmbientIntensity={RenderSettings.ambientIntensity:F3} AmbientLight={RenderSettings.ambientLight} AmbientSky={RenderSettings.ambientSkyColor} AmbientEquator={RenderSettings.ambientEquatorColor} AmbientGround={RenderSettings.ambientGroundColor}");
        report.Add($"Skybox={AssetDatabase.GetAssetPath(RenderSettings.skybox)} ReflectionMode={RenderSettings.defaultReflectionMode} ReflectionIntensity={RenderSettings.reflectionIntensity:F3} Fog={RenderSettings.fog} FogColor={RenderSettings.fogColor}");
        report.Add($"LightingSettings={AssetDatabase.GetAssetPath(settings)} RealtimeGI={(settings ? settings.realtimeGI.ToString() : "-")} BakedGI={(settings ? settings.bakedGI.ToString() : "-")} MixedMode={(settings ? settings.mixedBakeMode.ToString() : "-")} Lightmapper={(settings ? settings.lightmapper.ToString() : "-")}");
        report.Add($"LightingData={AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)} Lightmaps={LightmapSettings.lightmaps.Length} LightProbes={(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0)} LightmapsMode={LightmapSettings.lightmapsMode}");
        report.Add($"Renderers={renderers.Length} LightmappedRenderers={renderers.Count(renderer => renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534)} RealtimeLightmappedRenderers={renderers.Count(renderer => renderer.realtimeLightmapIndex >= 0 && renderer.realtimeLightmapIndex < 65534)}");
        report.Add($"Lights={lights.Length} ActiveLights={lights.Count(light => light.gameObject.activeInHierarchy && light.enabled)}");
        foreach (Light light in lights)
            report.Add($"Light={Path(light.transform)} active={light.gameObject.activeInHierarchy} enabled={light.enabled} type={light.type} mode={light.lightmapBakeType} intensity={light.intensity:F3} range={light.range:F2} color={light.color} shadows={light.shadows}");
        report.Add(string.Empty);
    }

    static string Path(Transform transform)
    {
        var names = new Stack<string>();
        while (transform) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }
}
