using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bakery 연결 상태를 기록하고 명시적 요청 시 기존 베이크 연결만 복구</summary>
[InitializeOnLoad]
public static class WorldLightingAudit
{
    private const string Output = "output/speaker-map-ui-20260912";
    static WorldLightingAudit() { EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string restore = Output + "/lighting-restore.request";
        if (File.Exists(restore) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(restore);
            try { Restore(); }
            catch (Exception error) { File.WriteAllText(Output + "/lighting-restore.failed", error.ToString()); }
            return;
        }
        string request = Output + "/lighting-audit.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Inspect(); }
        catch (Exception error) { File.WriteAllText(Output + "/lighting-audit.failed", error.ToString()); }
    }

    private static void Inspect()
    {
        var report = new StringBuilder();
        report.AppendLine("Time=" + DateTime.Now + "; playing=" + EditorApplication.isPlaying);
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            report.AppendLine("SCENE " + scene.path + "; loaded=" + scene.isLoaded + "; dirty=" + scene.isDirty);
        }
        foreach (SceneView view in SceneView.sceneViews)
            report.AppendLine("VIEW " + view.cameraMode.name + "; lighting=" + view.sceneLighting);
        report.AppendLine("Lighting data=" + AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset));
        var maps = LightmapSettings.lightmaps;
        report.AppendLine("Lightmaps=" + maps.Length + "; mode=" + LightmapSettings.lightmapsMode);
        for (int i = 0; i < maps.Length; i++)
            report.AppendLine("MAP " + i + " color=" + AssetDatabase.GetAssetPath(maps[i].lightmapColor));
        foreach (var storage in UnityEngine.Object.FindObjectsOfType<ftLightmapsStorage>(true))
        {
            report.AppendLine("STORAGE " + storage.gameObject.scene.path + "/" + storage.name + "; active=" + storage.gameObject.activeInHierarchy + "; maps=" + storage.maps.Count + "; renderers=" + storage.bakedRenderers.Count);
            int missing = 0, mismatched = 0, valid = 0;
            for (int i = 0; i < storage.bakedRenderers.Count; i++)
            {
                var renderer = storage.bakedRenderers[i];
                int bakedId = storage.bakedIDs[i];
                if (!renderer || bakedId < 0 || bakedId >= storage.maps.Count) continue;
                int index = renderer.lightmapIndex;
                var actual = index >= 0 && index < maps.Length ? maps[index].lightmapColor : null;
                var expected = storage.maps[bakedId];
                if (!actual) missing++;
                else if (actual != expected) mismatched++;
                else { valid++; continue; }
                if (missing + mismatched <= 18)
                    report.AppendLine("BAD " + renderer.name + "; index=" + index + "; expected=" + AssetDatabase.GetAssetPath(expected) + "; actual=" + AssetDatabase.GetAssetPath(actual));
            }
            report.AppendLine("RESULT valid=" + valid + "; missing=" + missing + "; mismatched=" + mismatched + "; missingSourceTextures=" + storage.maps.Count(t => !t));
        }
        File.WriteAllText(Output + "/lighting-audit.txt", report.ToString());
    }

    private static void Restore()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity" || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Restore requires only the original TEST scene.");
        var storage = UnityEngine.Object.FindObjectsOfType<ftLightmapsStorage>(true)
            .Single(s => s.gameObject.scene == scene);
        if (storage.maps.Count != 4 || storage.maps.Any(t => !t) || storage.bakedIDs.Count != storage.bakedRenderers.Count)
            throw new InvalidOperationException("Original Bakery data is incomplete.");

        // 사용자 배치/활성 상태/머티리얼과 미저장 상태는 그대로 유지.
        var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var placement = transforms.Select(t => t.localToWorldMatrix).ToArray();
        var active = transforms.Select(t => t.gameObject.activeSelf).ToArray();
        var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Where(r => r.gameObject.scene == scene).ToArray();
        var materials = renderers.Select(r => r.sharedMaterials).ToArray();
        bool dirty = scene.isDirty;
        Inspect();
        File.Copy(Output + "/lighting-audit.txt", Output + "/lighting-before-restore.txt", true);
        CaptureView(Output + "/lighting-before-restore.png");

        // 재베이크나 씬 재로드 없이 Bakery 원본 텍스처/인덱스/UV 연결을 다시 적용.
        ftLightmaps.RefreshScene(scene, storage);
        var maps = LightmapSettings.lightmaps;
        int verified = 0;
        for (int i = 0; i < storage.bakedRenderers.Count; i++)
        {
            var renderer = storage.bakedRenderers[i];
            int id = storage.bakedIDs[i];
            if (!renderer || id < 0 || id >= storage.maps.Count) continue;
            int index = renderer.lightmapIndex;
            if (index < 0 || index >= maps.Length || maps[index].lightmapColor != storage.maps[id])
                throw new InvalidOperationException("Lightmap restoration failed: " + renderer.name);
            if (!renderer.isPartOfStaticBatch && renderer.lightmapScaleOffset != storage.bakedScaleOffset[i])
                throw new InvalidOperationException("Lightmap UV restoration failed: " + renderer.name);
            verified++;
        }
        for (int i = 0; i < transforms.Length; i++)
            if (transforms[i].localToWorldMatrix != placement[i] || transforms[i].gameObject.activeSelf != active[i])
                throw new InvalidOperationException("Unrelated scene state changed.");
        for (int i = 0; i < renderers.Length; i++)
            if (!renderers[i].sharedMaterials.SequenceEqual(materials[i]))
                throw new InvalidOperationException("Unrelated material changed.");
        if (scene.isDirty != dirty) throw new InvalidOperationException("Scene save state changed.");
        Inspect();
        CaptureView(Output + "/lighting-after-restore.png");
        SceneView.RepaintAll();
        File.WriteAllText(Output + "/lighting-restore.done", "PASS: " + verified + " renderer lightmap/UV connections restored; transforms, active states, materials, and scene save state preserved. No bake or scene reload.");
    }

    private static void CaptureView(string path)
    {
        var view = SceneView.lastActiveSceneView;
        if (!view || !view.camera) return;
        var temporary = new GameObject("Lighting verification camera") { hideFlags = HideFlags.HideAndDontSave };
        var target = new RenderTexture(1440, 810, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            var camera = temporary.AddComponent<Camera>();
            camera.CopyFrom(view.camera);
            camera.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(temporary);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
