using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Only relocates the smoking caption/icon to the authored elbow leader.</summary>
[InitializeOnLoad]
public static class SpeakerMapSmokingCallout
{
    private const string Output="output/speaker-map-ui-20260912";
    static SpeakerMapSmokingCallout(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"/smoking-elbow.request";if(!File.Exists(request))return;File.Delete(request);
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"/smoking-elbow.failed",DateTime.Now+"\n"+e);Debug.LogException(e);}
    }
    private static void Place(Transform root)
    {
        var floor=root.Find("MapCanvas/FloorPlan");if(!floor)throw new Exception("Missing floor");
        var marker=(RectTransform)floor.Find("SpeakerMarkers");
        var label=(RectTransform)floor.Find("SmokingRoomLabel");var icon=(RectTransform)floor.Find("SmokingAreaIcon");
        if(!marker||!label||!icon)throw new Exception("Missing smoking caption, icon or calibration");
        label.anchoredPosition=SpeakerMapViewportSetup.SourceOffset((RectTransform)floor)+new Vector2(1545-900,600-198);
        icon.anchoredPosition=label.anchoredPosition+new Vector2(57,22);
        foreach(var item in new[]{label,icon}){EditorUtility.SetDirty(item);if(PrefabUtility.IsPartOfPrefabInstance(item))PrefabUtility.RecordPrefabInstancePropertyModifications(item);}
    }
    private static void Apply()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open TEST in edit mode");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).OrderBy(m=>m.name).ToArray();if(maps.Length!=2)throw new Exception("Expected two maps");
        var states=maps.Select(m=>EditorJsonUtility.ToJson(m)).ToArray();
        var protectedRects=maps.SelectMany(m=>m.GetComponentsInChildren<RectTransform>(true)).Where(r=>r.name!="SmokingRoomLabel"&&r.name!="SmokingAreaIcon").ToArray();
        var positions=protectedRects.Select(r=>r.position).ToArray();var sizes=protectedRects.Select(r=>r.sizeDelta).ToArray();
        AssetDatabase.ImportAsset("Assets/_Shinjuku/UI/SpeakerMap/FloorPlan.png",ImportAssetOptions.ForceUpdate);
        const string path="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Place(prefab.transform);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps){Undo.RegisterFullObjectHierarchyUndo(map.gameObject,"Move smoking room elbow callout");Place(map.transform);}
        for(int i=0;i<protectedRects.Length;i++)if(Vector3.Distance(positions[i],protectedRects[i].position)>.0001f||sizes[i]!=protectedRects[i].sizeDelta)throw new Exception("Unrelated layout changed: "+protectedRects[i].name);
        var report=new StringBuilder();
        for(int i=0;i<maps.Length;i++)
        {
            if(states[i]!=EditorJsonUtility.ToJson(maps[i]))throw new Exception("Runtime map bindings changed");
            var canvas=(RectTransform)maps[i].transform.Find("MapCanvas");
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+maps[i].name+"-smoking-elbow-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+maps[i].name+"-smoking-elbow.png");
            report.AppendLine(maps[i].name+": smoking label source(1545,198), existing icon offset(57,22), 1.5px leader (1455,210)->(1482,183)->(1492,183); other UI and runtime refs unchanged PASS.");
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/smoking-elbow.done",DateTime.Now+"\n"+report+"Original TEST unsaved; recovery copy saved.");
    }
}
