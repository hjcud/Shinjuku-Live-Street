using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A board's "You are here" denotes that board's physical XZ installation position.
// Never copy one instance's installation point into the shared prefab.
[InitializeOnLoad]
public static class SpeakerMapHereSetup
{
    const string Output="output/speaker-map-ui-20260912";
    static SpeakerMapHereSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        foreach(string action in new[]{"inspect","apply","opposite-inspect","opposite-apply"})
        {
            var path=Output+"/here-location-"+action+".request";
            if(!File.Exists(path))continue;File.Delete(path);
            try{Run(action.EndsWith("apply"),action.StartsWith("opposite-")?"Opposite Gate Speaker Map":null);}
            catch(Exception e){File.WriteAllText(Output+"/here-location-"+action+".failed",e.ToString());Debug.LogException(e);}
            return;
        }
    }
    static void Record(UnityEngine.Object obj)
    {
        EditorUtility.SetDirty(obj);
        if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    }
    [MenuItem("Tools/Shinjuku/Speaker Map/Align current locations to installed boards")]
    public static void Align(){Run(true);}
    static void Run(bool apply,string targetName=null)
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST scene");
        var allMaps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();
        var maps=allMaps.Where(m=>targetName==null||m.name==targetName).ToArray();
        if(maps.Length==0)throw new Exception("No existing maps");
        if(targetName!=null&&maps.Length!=1)throw new Exception("Target map name is not unique");
        var untouched=allMaps.Except(maps).SelectMany(m=>m.GetComponentsInChildren<RectTransform>(true)).ToArray();
        var untouchedState=untouched.Select(r=>EditorJsonUtility.ToJson(r)).ToArray();
        var report=new StringBuilder();
        foreach(var map in maps)
        {
            var canvas=(RectTransform)map.transform.Find("MapCanvas");
            var floor=(RectTransform)canvas.Find("FloorPlan");
            var here=(RectTransform)floor.Find("Here");
            var label=here.Find("Label").GetComponent<TMPro.TextMeshProUGUI>();
            var raw=floor.GetComponent<UnityEngine.UI.RawImage>();
            var uv=raw.uvRect;
            if(map.mapViewport!=new Vector4(uv.x,uv.y,uv.width,uv.height))throw new Exception("Map/texture viewport mismatch");
            Vector2 anchor=map.WorldToMapAnchor(canvas.position);
            if(anchor.x<0||anchor.x>1||anchor.y<0||anchor.y>1)throw new Exception("Board installation outside calibrated map: "+map.name);
            Vector2 target=new Vector2((anchor.x-floor.pivot.x)*floor.rect.width,(anchor.y-floor.pivot.y)*floor.rect.height);
            report.AppendLine(map.name+" installation="+canvas.position.ToString("F3")+" old="+here.anchoredPosition.ToString("F3")+" target="+target.ToString("F3")+" normalized="+anchor.ToString("F6"));
            if(!apply)continue;
            var otherRects=canvas.GetComponentsInChildren<RectTransform>(true).Where(r=>r!=here&&!r.IsChildOf(here)).ToArray();
            var oldPositions=otherRects.Select(r=>r.position).ToArray();
            Undo.RecordObjects(new UnityEngine.Object[]{here,label,label.rectTransform},"Recalculate board current location");
            // Center anchors retain existing source-caption conventions; crop-aware projection supplies the offset.
            here.anchorMin=here.anchorMax=new Vector2(.5f,.5f);here.anchoredPosition=target;
            // Place text left of each point, away from the lower-right station signature.
            label.rectTransform.anchoredPosition=new Vector2(-122,0);
            label.alignment=TMPro.TextAlignmentOptions.MidlineRight;
            Record(here);Record(label);Record(label.rectTransform);
            UnityEngine.Canvas.ForceUpdateCanvases();
            var actual=(Vector2)floor.InverseTransformPoint(here.position);
            if(Vector2.Distance(actual,target)>.01f)throw new Exception("Current location projection error");
            for(int i=0;i<otherRects.Length;i++)if(Vector3.Distance(otherRects[i].position,oldPositions[i])>.00001f)throw new Exception("Unrelated UI moved: "+otherRects[i].name);
            var corners=new Vector3[4];label.rectTransform.GetWorldCorners(corners);
            foreach(var corner in corners)if(!floor.rect.Contains((Vector2)floor.InverseTransformPoint(corner)))throw new Exception("Current-location label outside map");
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-here-location-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-here-location.png");
            report.AppendLine("PASS: projected point error < .01 map pixel; label within map; other UI unchanged.");
        }
        if(apply)
        {
            for(int i=0;i<untouched.Length;i++)if(EditorJsonUtility.ToJson(untouched[i])!=untouchedState[i])throw new Exception("Other board changed: "+untouched[i].name);
            Undo.FlushUndoRecordObjects();EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        }
        File.WriteAllText(Output+"/here-location-"+(targetName==null?"":"opposite-")+(apply?"apply.done":"inspect.txt"),DateTime.Now+"\n"+report+"Maps="+maps.Length+"; other boards, shared prefab, world geometry and original scene file unchanged.");
    }
}
