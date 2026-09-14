using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Targeted editor-only relocation; no runtime behaviour or unrelated layout edits.
[InitializeOnLoad]
public static class SpeakerMapMidoriCallout
{
    private const string Output = "output/speaker-map-ui-20260912";
    static SpeakerMapMidoriCallout() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        string request = Output + "/midori-up.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception error) { File.WriteAllText(Output + "/midori-up.failed", DateTime.Now + "\n" + error); Debug.LogException(error); }
    }
    private static void Place(Transform root)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        if (!floor) throw new Exception("Missing floor");
        var marker = floor.Find("SpeakerMarkers") as RectTransform;
        var label = floor.Find("MusicShopLabel") as RectTransform;
        var icon = floor.Find("MusicShopIcon") as RectTransform;
        if (!marker || !label || !icon) throw new Exception("Missing existing Midori caption/icon/calibration");
        // Original source centre (757,953): raise by 43 map pixels, retaining X and icon offset.
        label.anchoredPosition = SpeakerMapViewportSetup.SourceOffset((RectTransform)floor) + new Vector2(757 - 900, 600 - 910);
        icon.anchoredPosition = label.anchoredPosition + new Vector2(-100, 15);
        foreach (var rect in new[] { label, icon })
        {
            EditorUtility.SetDirty(rect);
            if (PrefabUtility.IsPartOfPrefabInstance(rect)) PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        }
    }
    private static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new Exception("Expected active TEST scene");
        var maps = UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m => m.gameObject.scene == scene).ToArray();
        if (maps.Length != 2) throw new Exception("Expected two map instances");
        var states = maps.Select(m => EditorJsonUtility.ToJson(m)).ToArray();
        var protectedRects = maps.SelectMany(m => m.GetComponentsInChildren<RectTransform>(true)).Where(r => r.name != "MusicShopLabel" && r.name != "MusicShopIcon").ToArray();
        var positions = protectedRects.Select(r => r.position).ToArray();
        var sizes = protectedRects.Select(r => r.sizeDelta).ToArray();
        AssetDatabase.ImportAsset("Assets/_Shinjuku/UI/SpeakerMap/FloorPlan.png", ImportAssetOptions.ForceUpdate);
        const string path = "Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { Place(prefab.transform); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var map in maps) { Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Raise Midori map callout"); Place(map.transform); }
        for (int i = 0; i < protectedRects.Length; i++)
            if (Vector3.Distance(positions[i], protectedRects[i].position) > .0001f || sizes[i] != protectedRects[i].sizeDelta) throw new Exception("Unrelated layout changed: " + protectedRects[i].name);
        for (int i = 0; i < maps.Length; i++)
        {
            if (states[i] != EditorJsonUtility.ToJson(maps[i])) throw new Exception("Runtime map references changed");
            var canvas = (RectTransform)maps[i].transform.Find("MapCanvas");
            SpeakerMapSetup.VerifyPresentation(canvas, Output + "/" + maps[i].name + "-midori-up-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas, Output + "/" + maps[i].name + "-midori-up.png");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/midori-up.done", DateTime.Now + " PASS: both map captions/icons raised 43px to source(757,910); other rects and runtime refs unchanged. Prefab and recovery copy saved; original TEST unsaved.");
    }
}
