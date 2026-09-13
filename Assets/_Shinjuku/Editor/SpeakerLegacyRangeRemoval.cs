using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

[InitializeOnLoad]
public static class SpeakerLegacyRangeRemoval
{
    const string Output="output/speaker-placement-20260912/";
    static SpeakerLegacyRangeRemoval(){EditorApplication.update+=Poll;}
    static T F<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"remove-legacy-range-v2.request";if(!File.Exists(request))return;File.Delete(request);
        try{Remove();}catch(Exception e){File.WriteAllText(Output+"remove-legacy-range.failed",e.ToString());Debug.LogException(e);}
    }
    static void Remove()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
        var placements=F<GameObject>(manager,"speakerPlacements");var obsolete=placements.transform.Find("AudibleRangeIndicator");
        var placementRay=F<LineRenderer>(manager,"lineRenderer");var rayMaterial=placementRay.sharedMaterial;
        var circles=F<MeshRenderer[]>(manager,"placedSpeakerHeatmaps");var badges=F<MeshRenderer[]>(manager,"placedSpeakerBadges");
        if(obsolete&&(obsolete.childCount!=0||obsolete.GetComponents<Component>().Any(c=>!(c is Transform)&&!(c is LineRenderer))))throw new Exception("Unexpected content on obsolete range object");
        string backup=Output+"TEST.before-legacy-range-removal-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
        if(circles==null||badges==null||circles.Length!=6||badges.Length!=6||circles.Any(c=>!c)||badges.Any(b=>!b))
        {
            // This loaded scene predates the approved quiet-guide setup. Rebind only the speaker guides.
            if(F<SpeakerController[]>(manager,"speakerControllers").Length!=6)throw new Exception("Expected existing six speaker slots");
            string verification=Output+"verify-review.request";
            if(File.Exists(verification))throw new Exception("Another placement verification is pending");
            try{typeof(SpeakerPlacementReview).GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}
            finally{if(File.Exists(verification))File.Delete(verification);}
            typeof(SpeakerPlacementReview).GetMethod("ApplyHeatmap",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            typeof(SpeakerQuietGuideSetup).GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        }
        Undo.RecordObject(manager,"Disconnect obsolete speaker range circle");var so=new SerializedObject(manager);so.FindProperty("audibleRangeRenderer").objectReferenceValue=null;so.ApplyModifiedProperties();
        if(obsolete)Undo.DestroyObjectImmediate(obsolete.gameObject);
        // Regression: the actual runtime update must work without the retired renderer.
        typeof(SpeakerManager).GetMethod("ConfigureAudibleRangeRenderer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);
        typeof(SpeakerManager).GetMethod("UpdateAudibleRange",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{F<GameObject>(manager,"holoSpeaker").transform.position});
        typeof(SpeakerQuietGuideSetup).GetMethod("CaptureAndTest",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{manager,false});
        if(placements.transform.Find("AudibleRangeIndicator")||F<LineRenderer>(manager,"audibleRangeRenderer")||!placementRay||placementRay.sharedMaterial!=rayMaterial)throw new Exception("Removal or placement ray preservation failed");
        UdonSharpEditorUtility.CopyProxyToUdon(manager);EditorUtility.SetDirty(manager);EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager));
        if(PrefabUtility.IsPartOfPrefabInstance(manager))PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new Exception("Original TEST save failed");
        File.WriteAllText(Output+"remove-legacy-range.done",DateTime.Now+" Removed only SpeakerPlacements/AudibleRangeIndicator; detached legacy reference; preserved ray and shared material; modern six-speaker quiet/focus/arrow tests passed with null legacy reference. Saved original TEST. Recovery: "+backup);
    }
}
