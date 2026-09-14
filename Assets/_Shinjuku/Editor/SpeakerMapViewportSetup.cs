using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

/// <summary>Fit marker canvas to visible map without changing source-world calibration.</summary>
[InitializeOnLoad]
public static class SpeakerMapViewportSetup
{
    const string Output="output/speaker-map-ui-20260912";
    static SpeakerMapViewportSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(!File.Exists(Output+"/viewport-fit.request"))return;
        File.Delete(Output+"/viewport-fit.request");
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"/viewport-fit.failed",DateTime.Now+"\n"+e);Debug.LogException(e);}
    }
    static void Record(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);
        if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }
    // Authoring labels use source texture coordinates, not the marker Canvas center.
    public static Vector2 SourceOffset(RectTransform floor)
    {
        var uv=floor.GetComponent<UnityEngine.UI.RawImage>().uvRect;
        return new Vector2((.5f-uv.center.x)*floor.rect.width/uv.width,(.5f-uv.center.y)*floor.rect.height/uv.height);
    }
    public static void Fit(SpeakerMap map)
    {
        var floor=(RectTransform)map.transform.Find("MapCanvas/FloorPlan");
        var overlay=(RectTransform)floor.Find("SpeakerMarkers");
        var uv=floor.GetComponent<UnityEngine.UI.RawImage>().uvRect;
        if(uv.width<=0||uv.height<=0)throw new Exception("Invalid map UV crop");
        overlay.anchorMin=Vector2.zero;overlay.anchorMax=Vector2.one;overlay.pivot=Vector2.one*.5f;
        overlay.sizeDelta=Vector2.zero;overlay.anchoredPosition3D=Vector3.zero;
        map.mapViewport=new Vector4(uv.x,uv.y,uv.width,uv.height);
        Record(overlay);Record(map);UdonSharpEditorUtility.CopyProxyToUdon(map);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(map));
    }
    static Vector3 Point(SpeakerMap map,Vector3 world)
    {
        var overlay=(RectTransform)map.transform.Find("MapCanvas/FloorPlan/SpeakerMarkers");
        var a=map.WorldToMapAnchor(world);
        return overlay.TransformPoint(new Vector3((a.x-overlay.pivot.x)*overlay.rect.width,(a.y-overlay.pivot.y)*overlay.rect.height,0));
    }
    static void Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open original TEST in edit mode");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();
        if(maps.Length!=2)throw new Exception("Expected two map boards");
        UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms();
        var speakers=UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Where(s=>s.gameObject.scene==scene).ToArray();
        var samples=speakers.Select(s=>s.transform.position).ToList();
        for(int x=0;x<=20;x++)for(int z=0;z<=20;z++)samples.Add(new Vector3(-70+x*9,0,-67+z*6));
        samples.AddRange(new[]{new Vector3(-51,0,-64),new Vector3(110,0,44),new Vector3(-51.1f,0,0),new Vector3(110.1f,0,0),new Vector3(0,0,-64.1f),new Vector3(0,0,44.1f)});
        var before=maps.Select(m=>samples.Select(w=>Point(m,w)).ToArray()).ToArray();
        var protectedRects=maps.SelectMany(m=>m.GetComponentsInChildren<RectTransform>(true)).Where(r=>!r.name.Equals("SpeakerMarkers")&&!r.IsChildOf(r.root.Find("MapCanvas/FloorPlan/SpeakerMarkers"))).ToArray();
        var protectedPositions=protectedRects.Select(r=>r.position).ToArray();
        foreach(var map in maps)Undo.RegisterFullObjectHierarchyUndo(map.gameObject,"Fit map marker canvas preserving speaker projection");
        var report=new StringBuilder();
        const string prefabPath="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try{Fit(prefab.GetComponent<SpeakerMap>());PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        for(int i=0;i<maps.Length;i++)
        {
            Fit(maps[i]);Canvas.ForceUpdateCanvases();
            float maxError=0;
            for(int j=0;j<samples.Count;j++)maxError=Mathf.Max(maxError,Vector3.Distance(before[i][j],Point(maps[i],samples[j])));
            if(maxError>.00002f)throw new Exception("Speaker projection drift: "+maxError);
            var floor=(RectTransform)maps[i].transform.Find("MapCanvas/FloorPlan");var overlay=(RectTransform)floor.Find("SpeakerMarkers");
            var a=new Vector3[4];var b=new Vector3[4];floor.GetWorldCorners(a);overlay.GetWorldCorners(b);
            if(a.Where((p,j)=>Vector3.Distance(p,b[j])>.00001f).Any())throw new Exception("Visible map and overlay corners differ");
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(maps[i]);
            if(!backing.publicVariables.TryGetVariableValue("mapViewport",out Vector4 viewport)||viewport!=maps[i].mapViewport)throw new Exception("Missing compiled Udon viewport binding");
            foreach(var outside in samples.Skip(samples.Count-4)){var anchor=maps[i].WorldToMapAnchor(outside);if(anchor.x>=0&&anchor.x<=1&&anchor.y>=0&&anchor.y<=1)throw new Exception("Outside-crop point not excluded");}
            report.AppendLine(maps[i].name+": "+samples.Count+" samples (six speakers, grid, crop boundaries); max world-space UI drift="+maxError+"; matching floor/overlay corners; outside crop excluded; Udon viewport binding PASS");
            SpeakerMapSetup.CapturePanel((RectTransform)floor.parent,Output+"/"+maps[i].name+"-viewport-fit.png");
        }
        for(int i=0;i<protectedRects.Length;i++)if(Vector3.Distance(protectedRects[i].position,protectedPositions[i])>.00002f)throw new Exception("Unrelated UI moved: "+protectedRects[i].name);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/viewport-fit.done",DateTime.Now+"\n"+report+"UI, source world origin/range and physical speakers unchanged. Active TEST unsaved; recovery copy saved.\n");
    }
}
