using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-shot, reversible comparisons. Does not save or replace any scene/asset.
[InitializeOnLoad]
public static class QuestBlackSurfaceAudit
{
    const string Folder = "output/quest-optimization-20260913/black-surface";
    const string Request = Folder + "/audit.request";
    static QuestBlackSurfaceAudit() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Run(); }
        catch (Exception e) { File.WriteAllText(Folder + "/audit.failed", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Shinjuku/Quest/Compare Black Building Surfaces")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity") throw new InvalidOperationException("Open TEST_Quest in Edit mode first.");
        var view = SceneView.lastActiveSceneView;
        if (!view || !view.camera) throw new InvalidOperationException("A Scene view is required.");
        Directory.CreateDirectory(Folder);
        var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
        var originals = renderers.ToDictionary(r => r, r => r.sharedMaterials);
        var targets = originals.Values.SelectMany(m => m).Where(m => m && m.shader.name == "VRChat/Mobile/Standard Lite"
            && AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Shinjuku/Quest/GeneratedMaterials/", StringComparison.Ordinal)
            && m.IsKeywordEnabled("_EMISSION")).Distinct().ToArray();
        var copies = new Dictionary<Material, Material>();
        var report = new List<string> { "Scene=" + scene.path, "Targets=" + targets.Length,
            "SceneLighting=" + view.sceneLighting, "CameraPosition=" + view.camera.transform.position,
            "CameraRotation=" + view.camera.transform.eulerAngles, "Lightmaps=" + LightmapSettings.lightmaps.Length };
        foreach (var m in targets) report.Add(m.name + " Keywords=" + string.Join(",", m.shaderKeywords)
            + " AO=" + AssetDatabase.GetAssetPath(m.GetTexture("_OcclusionMap")) + " AOStrength=" + m.GetFloat("_OcclusionStrength"));
        foreach (var r in renderers.Where(r => r.sharedMaterials.Any(m => m && targets.Contains(m))))
            report.Add("Renderer=" + r.name + " LM=" + r.lightmapIndex + " ST=" + r.lightmapScaleOffset);
        try
        {
            Capture(view.camera, Folder + "/01-before.png");
            foreach (var m in targets) { var c = new Material(m); c.hideFlags = HideFlags.HideAndDontSave; c.SetFloat("_OcclusionStrength", 0f); copies[m] = c; }
            Assign(originals, copies);
            Capture(view.camera, Folder + "/02-without-ao.png");
            foreach (var pair in copies)
            {
                var m = pair.Key; var c = pair.Value;
                var defaults = new Material(m.shader);
                c.CopyPropertiesFromMaterial(defaults);
                UnityEngine.Object.DestroyImmediate(defaults);
                c.shaderKeywords = new[] { "DISABLE_VERTEX_COLORING", "_EMISSION", "_GLOSSYREFLECTIONS_OFF", "_SPECULARHIGHLIGHTS_OFF" };
                c.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                c.SetTextureScale("_MainTex", m.GetTextureScale("_MainTex"));
                c.SetTextureOffset("_MainTex", m.GetTextureOffset("_MainTex"));
                c.SetColor("_Color", Color.white);
                c.SetTexture("_EmissionMap", m.GetTexture("_EmissionMap"));
                c.SetColor("_EmissionColor", m.GetColor("_EmissionColor"));
                c.SetFloat("_Metallic", 0f); c.SetFloat("_Glossiness", 0f);
                c.SetFloat("_OcclusionStrength", 0f); c.SetFloat("_BumpScale", 0f);
                c.SetFloat("_EnableGeometricSpecularAA", 0f);
                c.SetFloat("_SpecularHighlights", 0f); c.SetFloat("_GlossyReflections", 0f);
            }
            Capture(view.camera, Folder + "/03-clean-standard.png");
            foreach (var pair in copies) pair.Value.shader = Shader.Find("VRChat/Mobile/Lightmapped");
            Capture(view.camera, Folder + "/04-lightmapped.png");
        }
        finally
        {
            foreach (var pair in originals) if (pair.Key) pair.Key.sharedMaterials = pair.Value;
            foreach (var c in copies.Values) UnityEngine.Object.DestroyImmediate(c);
            SceneView.RepaintAll();
        }
        File.WriteAllLines(Folder + "/audit.done", report);
    }

    static void Assign(Dictionary<Renderer, Material[]> originals, Dictionary<Material, Material> copies)
    {
        foreach (var pair in originals) pair.Key.sharedMaterials = pair.Value.Select(m => m && copies.ContainsKey(m) ? copies[m] : m).ToArray();
    }

    public static void Capture(Camera source, string path)
    {
        var go = new GameObject("Quest diagnostic camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.CopyFrom(source); camera.enabled = false;
        camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        var view = SceneView.lastActiveSceneView;
        if (view && view.camera == source)
        {
            // SceneView's native camera can be reset between repaints while Unity is unfocused.
            // Its serialized pivot/rotation/size remain the authoritative viewing pose.
            float fov = Mathf.Clamp(source.fieldOfView, 20f, 100f);
            float distance = view.size / Mathf.Sin(fov * .5f * Mathf.Deg2Rad);
            camera.transform.SetPositionAndRotation(view.pivot - view.rotation * Vector3.forward * distance, view.rotation);
            camera.fieldOfView = fov;
            camera.orthographic = view.orthographic;
            camera.orthographicSize = view.size;
        }
        camera.ResetWorldToCameraMatrix();
        camera.ResetProjectionMatrix();
        camera.nearClipPlane = .1f; camera.farClipPlane = 2000f;
        camera.cameraType = CameraType.Game;
        camera.clearFlags = CameraClearFlags.Skybox;
        var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prior = RenderTexture.active;
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = rt; camera.aspect = 1280f / 720f; camera.Render();
            RenderTexture.active = rt;
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = prior; camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
