using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Targeted styling only: never rebuild, move, or resize the calibrated map hierarchy.
[InitializeOnLoad]
public static class SpeakerMapWarmStyle
{
    const string Output = "output/speaker-map-ui-20260912";
    const string Prefab = "Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
    static SpeakerMapWarmStyle() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach(string action in new[]{"inspect","apply","verify","bridge-title"})
        {
            string request=Output+"/warm-style-"+action+".request";
            if(!File.Exists(request))continue;
            File.Delete(request);
            try { if(action=="bridge-title")ShortenBridgeTitle(); else if(action=="inspect")Inspect(); else if(action=="apply")Apply(); else Verify(); }
            catch(Exception e){File.WriteAllText(Output+"/warm-style-"+action+".failed",e.ToString());Debug.LogException(e);}
            return;
        }
    }
    static SpeakerMap[] Maps()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST scene");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();
        if(maps.Length!=2)throw new Exception("Expected two existing maps");return maps;
    }
    static string ShortenBridgeTitleOn(SpeakerMap map)
    {
        var panel=map.GetComponent<SpeakerMapPanel>();
        var label=Canvas(map).Find("FloorPlan/BridgeLabel").GetComponent<TMPro.TextMeshProUGUI>();
        int index=Array.IndexOf(panel.labels,label);
        if(index<0||panel.japanese==null||panel.english==null)throw new Exception("Bridge bilingual binding missing");
        string old=panel.japanese[index];
        if(old!="歩道橋・上階"&&old!="歩道橋")throw new Exception("Unexpected bridge title: "+old);
        string english=panel.english[index];
        Undo.RecordObjects(new UnityEngine.Object[]{panel,label},"Remove upper-level Japanese suffix");
        panel.japanese[index]="歩道橋";
        label.text=panel.Bilingual(panel.japanese[index],english);
        foreach(var obj in new UnityEngine.Object[]{panel,label})
        {
            EditorUtility.SetDirty(obj);
            if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(panel);
        var backing=UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
        EditorUtility.SetDirty(backing);
        if(PrefabUtility.IsPartOfPrefabInstance(backing))PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
        if(!backing.publicVariables.TryGetVariableValue("japanese",out string[] stored)||stored[index]!="歩道橋")throw new Exception("Runtime title not updated");
        return map.name+": "+old+" => "+panel.japanese[index]+"; English unchanged: "+english;
    }
    static void ShortenBridgeTitle()
    {
        var maps=Maps();var report=new StringBuilder();
        var prefab=PrefabUtility.LoadPrefabContents(Prefab);
        try{report.AppendLine(ShortenBridgeTitleOn(prefab.GetComponent<SpeakerMap>()));PrefabUtility.SaveAsPrefabAsset(prefab,Prefab);}
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps)
        {
            report.AppendLine(ShortenBridgeTitleOn(map));
            SpeakerMapSetup.VerifyPresentation(Canvas(map),Output+"/"+map.name+"-bridge-title-layout.txt");
            SpeakerMapSetup.CapturePanel(Canvas(map),Output+"/"+map.name+"-bridge-title.png");
        }
        Undo.FlushUndoRecordObjects();var scene=SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/warm-style-bridge-title.done",DateTime.Now+"\n"+report+"Prefab and recovery saved; original TEST left unsaved.");
    }
    static RectTransform Canvas(SpeakerMap map){return (RectTransform)map.transform.Find("MapCanvas");}
    static void Wall(RectTransform canvas,string path)
    {
        typeof(SpeakerMapSetup).GetMethod("CaptureEntrance",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{canvas,path,(Vector3?)(canvas.position-canvas.forward*5+canvas.right*1.3f)});
    }
    static void Inspect()
    {
        var report=new StringBuilder();
        foreach(var map in Maps())
        {
            var canvas=Canvas(map);
            report.AppendLine(map.name+" world="+canvas.position+" scale="+canvas.lossyScale);
            foreach(var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                report.AppendLine(g.name+" parent="+g.transform.parent.name+" type="+g.GetType().Name+" color="+g.color+" shader="+(g.material?g.material.shader.name:"none")+" pos="+g.rectTransform.anchoredPosition+" size="+g.rectTransform.rect.size);
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-warm-before.png");
            Wall(canvas,Output+"/"+map.name+"-warm-wall-before.png");
        }
        File.WriteAllText(Output+"/warm-style-inspect.txt",report.ToString());
    }
    static Color Hex(string hex){Color c;ColorUtility.TryParseHtmlString("#"+hex,out c);return c;}
    static void Paint(UnityEngine.UI.Graphic graphic,string hex)
    {
        if(!graphic)return;graphic.color=Hex(hex);EditorUtility.SetDirty(graphic);
        if(PrefabUtility.IsPartOfPrefabInstance(graphic))PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
    }
    static void Style(SpeakerMap map)
    {
        var canvas=Canvas(map);
        if(!canvas.Find("Board")||!canvas.Find("MetalFrame")||!canvas.Find("FloorPlan"))throw new Exception("Missing board surfaces");
        Paint(canvas.Find("Board").GetComponent<UnityEngine.UI.Image>(),"B6AF9E");
        Paint(canvas.Find("MetalFrame").GetComponent<UnityEngine.UI.Image>(),"36372F");
        // Native floor texture provides the colors; preserve UV, scale, material and all child positions.
        Paint(canvas.Find("FloorPlan").GetComponent<UnityEngine.UI.RawImage>(),"FFFFFF");
        foreach(var label in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            Color old=label.color;
            if(label.transform.parent.name=="Here")continue; // current-location blue only
            Paint(label,old.g>old.r+.035f && old.g>old.b+.015f?"45634B":"50564B");
        }
        foreach(var icon in canvas.Find("FloorPlan").GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if(icon.name.Contains("Icon"))Paint(icon,"626D5F");
            if(icon.name=="BridgeLabelLeader")Paint(icon,"899582");
        }
        var footer=canvas.Find("LocalSettings");
        foreach(var graphic in footer.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if(graphic.name=="SettingsOutline"||graphic.name=="HeadingRule")Paint(graphic,"8D9685");
            if(graphic.name=="UsageDivider")Paint(graphic,"A2AA99");
            if(graphic.name=="Thumb")Paint(graphic,"E3DDCE");
        }
        var panel=map.GetComponent<SpeakerMapPanel>();panel._RefreshSettings();
        foreach(var track in new[]{panel.vehicleSwitchTrack,panel.ambientSwitchTrack})
        {
            EditorUtility.SetDirty(track);if(PrefabUtility.IsPartOfPrefabInstance(track))PrefabUtility.RecordPrefabInstancePropertyModifications(track);
        }
    }
    static void Apply()
    {
        var maps=Maps();
        AssetDatabase.ImportAsset("Assets/_Shinjuku/UI/SpeakerMap/FloorPlan.png",ImportAssetOptions.ForceUpdate);
        var prefab=PrefabUtility.LoadPrefabContents(Prefab);
        try{Style(prefab.GetComponent<SpeakerMap>());PrefabUtility.SaveAsPrefabAsset(prefab,Prefab);}
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps)
        {
            Undo.RegisterFullObjectHierarchyUndo(Canvas(map).gameObject,"Warm station map palette");
            var rects=Canvas(map).GetComponentsInChildren<RectTransform>(true);
            var positions=rects.Select(r=>r.localPosition).ToArray();var sizes=rects.Select(r=>r.sizeDelta).ToArray();
            Style(map);
            for(int i=0;i<rects.Length;i++)if(rects[i].localPosition!=positions[i]||rects[i].sizeDelta!=sizes[i])throw new Exception("Styling changed geometry: "+rects[i].name);
        }
        Undo.FlushUndoRecordObjects();
        File.WriteAllText(Output+"/warm-style-verify.request","Verify styling after prefab import and script reload.");
    }
    static void Verify()
    {
        var report=new StringBuilder();
        foreach(var map in Maps())
        {
            var canvas=Canvas(map);map.GetComponent<SpeakerMapPanel>()._RefreshSettings();
            foreach(var rect in canvas.Find("LocalSettings").GetComponentsInChildren<RectTransform>(false))
            {
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                foreach(var corner in corners)if(!canvas.rect.Contains((Vector2)canvas.InverseTransformPoint(corner)))throw new Exception("Settings outside board: "+rect.name);
            }
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-warm-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-warm-after.png");
            Wall(canvas,Output+"/"+map.name+"-warm-wall-after.png");
            report.AppendLine(map.name+": existing layout retained; settings inside board; callbacks retain green switch palette.");
        }
        var scene=SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/warm-style-verify.done",DateTime.Now+"\n"+report+"Original TEST left unsaved; prefab and recovery saved.");
    }
}
