using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestStationSignFix
{
    const string Folder = "output/quest-optimization-20260913/station-sign";
    const string Quest = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string Pc = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string MaterialPath = "Assets/_Shinjuku/Quest/GeneratedMaterials/2-Shinjuku-1024_6b404c5f.mat";
    static QuestStationSignFix() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (string action in new[] { "audit", "apply", "verify" })
        {
            string request = Folder + "/" + action + ".request";
            if (!File.Exists(request)) continue;
            File.Delete(request);
            try { if (action == "verify") VerifyReload(); else Run(action == "apply"); }
            catch (Exception e) { File.WriteAllText(Folder + "/" + action + ".failed", e.ToString()); Debug.LogException(e); }
        }
    }

    static void VerifyReload()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != Quest || scene.isDirty || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Verification requires only the saved Quest scene.");
        string pcHash = Hash(Pc), questHash = Hash(Quest);
        scene = EditorSceneManager.OpenScene(Quest, OpenSceneMode.Single);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var signs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(r => r.sharedMaterials.Contains(material)).ToArray();
        if (signs.Length != 1 || signs[0].lightmapIndex != -1) throw new InvalidOperationException("Sign lightmap exclusion did not survive reload.");
        CaptureSign(signs[0].bounds, Folder + "/repair-close-reloaded.png");
        if (Hash(Pc) != pcHash || Hash(Quest) != questHash) throw new InvalidOperationException("Scene file changed during read-only reload verification.");
        File.WriteAllText(Folder + "/verify.done", "SignLightmapAfterSceneReload=-1\nPCAndQuestFilesUnchangedDuringVerification=true");
    }

    static void Run(bool apply)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != Quest) throw new InvalidOperationException("Open TEST_Quest first.");
        if (SceneManager.sceneCount != 1) throw new InvalidOperationException("Open only TEST_Quest before this repair.");
        if (apply && scene.isDirty) throw new InvalidOperationException("Unsaved Quest scene changes. Save or discard them before running the repair.");
        Directory.CreateDirectory(Folder);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) throw new InvalidOperationException("Missing Quest station sign material.");
        var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
        var signs = renderers.Where(r => r.sharedMaterials.Contains(material)).ToArray();
        if (signs.Length != 1) throw new InvalidOperationException("Expected exactly one sign renderer; got " + signs.Length);
        var storages = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ftLightmapsStorage>(true)).ToArray();
        var report = new List<string>();
        var originals = renderers.ToDictionary(r => r, r => r.lightmapIndex);
        foreach (var r in signs) report.Add("Sign=" + r.name + " LiveLightmap=" + r.lightmapIndex + " Bounds=" + r.bounds);
        foreach (var s in storages)
            for (int i = 0; i < s.bakedRenderers.Count; i++)
                if (signs.Contains(s.bakedRenderers[i])) report.Add("Bakery=" + s.name + " Entry=" + i + " StoredLightmap=" + s.bakedIDs[i]);
        var camera = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : null;
        if (!camera) throw new InvalidOperationException("Open the Scene view first.");
        string prefix = apply ? "repair" : "audit";
        QuestBlackSurfaceAudit.Capture(camera, Folder + "/" + prefix + "-before.png");
        CaptureSign(signs[0].bounds, Folder + "/" + prefix + "-close-before.png");

        if (!apply)
        {
            try
            {
                foreach (var r in signs) r.lightmapIndex = -1;
                QuestBlackSurfaceAudit.Capture(camera, Folder + "/audit-without-sign-lightmap.png");
                foreach (var s in storages) ftLightmaps.RefreshScene2(scene, s);
                report.Add("AfterBakeryRefresh=" + signs[0].lightmapIndex);
            }
            finally { foreach (var pair in originals) pair.Key.lightmapIndex = pair.Value; }
        }
        else
        {
            var protectedPaths = AssetDatabase.GetDependencies(Pc, true)
                .Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).Concat(new[] { Pc, MaterialPath }).Distinct().ToDictionary(p => p, Hash);
            File.Copy(Quest, Folder + "/TEST_Quest.before-sign-fix-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", false);
            int changed = 0;
            foreach (var s in storages)
            {
                for (int i = 0; i < s.bakedRenderers.Count; i++)
                {
                    if (!signs.Contains(s.bakedRenderers[i])) continue;
                    // Bakery reapplies this saved ID on Awake/Start. Setting only the live
                    // Renderer.lightmapIndex is lost when the scene or client reloads.
                    s.bakedIDs[i] = -1;
                    EditorUtility.SetDirty(s);
                    changed++;
                }
            }
            if (changed != 1) throw new InvalidOperationException("Unexpected number of Bakery sign entries: " + changed);
            foreach (var r in signs) { r.lightmapIndex = -1; EditorUtility.SetDirty(r); }
            foreach (var s in storages) ftLightmaps.RefreshScene2(scene, s);
            if (signs[0].lightmapIndex != -1) throw new InvalidOperationException("Bakery still restores the sign's baked lighting.");
            foreach (var pair in originals)
                if (!signs.Contains(pair.Key) && pair.Key.lightmapIndex != pair.Value)
                    throw new InvalidOperationException("Unrelated renderer lightmap changed: " + pair.Key.name);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Quest scene save failed.");
            foreach (var pair in protectedPaths)
                if (Hash(pair.Key) != pair.Value) throw new InvalidOperationException("Protected asset changed: " + pair.Key);
            report.Add("ProtectedAssetsUnchanged=" + protectedPaths.Count);
            report.Add("ChangedBakeryEntries=" + changed);
            report.Add("SignLightmapAfterBakeryRefresh=" + signs[0].lightmapIndex);
            QuestBlackSurfaceAudit.Capture(camera, Folder + "/repair-after.png");
            CaptureSign(signs[0].bounds, Folder + "/repair-close-after.png");
            var reopened = EditorSceneManager.OpenScene(Quest, OpenSceneMode.Single);
            var reloadedSigns = reopened.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.sharedMaterials.Contains(material)).ToArray();
            if (reloadedSigns.Length != 1 || reloadedSigns[0].lightmapIndex != -1)
                throw new InvalidOperationException("Sign lighting exclusion did not survive scene reload.");
            report.Add("SignLightmapAfterSceneReload=" + reloadedSigns[0].lightmapIndex);
            CaptureSign(reloadedSigns[0].bounds, Folder + "/repair-close-reloaded.png");
            foreach (var pair in protectedPaths)
                if (Hash(pair.Key) != pair.Value) throw new InvalidOperationException("Protected asset changed after reload: " + pair.Key);
        }
        File.WriteAllLines(Folder + "/" + prefix + ".done", report);
        SceneView.RepaintAll();
    }

    static void CaptureSign(Bounds bounds, string path)
    {
        var go = new GameObject("Quest sign inspection camera") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 50f;
            camera.transform.position = bounds.center + new Vector3(-3f, 1.4f, -15f);
            camera.transform.LookAt(bounds.center);
            QuestBlackSurfaceAudit.Capture(camera, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
