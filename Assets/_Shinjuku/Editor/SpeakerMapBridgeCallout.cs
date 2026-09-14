using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Targeted editor-only placement of the bridge caption and its leader.
[InitializeOnLoad]
public static class SpeakerMapBridgeCallout
{
    private const string Output = "output/speaker-map-ui-20260912";
    static SpeakerMapBridgeCallout() { EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        string request = Output + "/bridge-callout-road-v2.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText(Output + "/bridge-callout-road-v2.failed", e.ToString()); Debug.LogException(e); }
    }

    private static void Place(Transform root, StringBuilder report)
    {
        var floor = root.Find("MapCanvas/FloorPlan");
        var label = floor ? floor.Find("BridgeLabel") as RectTransform : null;
        if (!label || !label.GetComponent<TextMeshProUGUI>()) throw new InvalidOperationException("Missing bridge caption: " + root.name);
        report.AppendLine(root.name + " before=" + label.anchoredPosition);
        label.anchoredPosition = new Vector2(550, 130); // map x=1450, y=470: road, clear of speaker placement on the pavement
        var leader = floor.Find("BridgeLabelLeader") as RectTransform;
        if (!leader)
        {
            leader = new GameObject("BridgeLabelLeader", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            leader.SetParent(floor, false);
        }
        leader.anchorMin = leader.anchorMax = leader.pivot = new Vector2(.5f, .5f);
        leader.anchoredPosition = new Vector2(705.5f, 140); // map x=1580..1631, ending at the bridge edge
        leader.sizeDelta = new Vector2(51, 1.5f);
        var graphic = leader.GetComponent<UnityEngine.UI.Image>();
        graphic.color = new Color32(155, 171, 190, 255);
        graphic.raycastTarget = false;
        foreach (var component in new Component[] { label, leader, graphic })
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        if (label.anchoredPosition != new Vector2(550, 130) || leader.sizeDelta != new Vector2(51, 1.5f)) throw new InvalidOperationException("Bridge callout placement failed.");
        report.AppendLine("after=" + label.anchoredPosition + "; leader=" + leader.anchoredPosition);
    }

    private static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST in edit mode.");
        var roots = scene.GetRootGameObjects().Where(r => r.name == "Entrance Speaker Map" || r.name == "Opposite Gate Speaker Map").ToArray();
        if (roots.Length != 2) throw new InvalidOperationException("Expected two maps.");
        var configurations = roots.Select(r => EditorJsonUtility.ToJson(r.GetComponent<SpeakerMap>())).ToArray();
        var report = new StringBuilder();
        string path = "Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { Place(prefab.transform, report); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        for (int i = 0; i < roots.Length; i++)
        {
            Undo.RegisterFullObjectHierarchyUndo(roots[i], "Align bridge map caption");
            Place(roots[i].transform, report);
            if (configurations[i] != EditorJsonUtility.ToJson(roots[i].GetComponent<SpeakerMap>())) throw new InvalidOperationException("Map configuration changed.");
            SpeakerMapSetup.CapturePanel((RectTransform)roots[i].transform.Find("MapCanvas"), Output + "/" + (i == 0 ? "bridge-callout-road-first" : "bridge-callout-road-second") + ".png");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/bridge-callout-road-v2.done", report + "PASS: prefab and both active maps updated; recovery copy saved; original TEST unsaved.");
    }
}
