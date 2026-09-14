using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpeakerCancelReview
{
    const string Output="output/speaker-placement-20260912/";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static SpeakerCancelReview(){EditorApplication.update+=Poll;}
    static object Get(SpeakerManager m,string n)=>typeof(SpeakerManager).GetField(n,Flags).GetValue(m);
    static void Set(SpeakerManager m,string n,object v)=>typeof(SpeakerManager).GetField(n,Flags).SetValue(m,v);
    static object Call(SpeakerManager m,string n,params object[] args)=>typeof(SpeakerManager).GetMethod(n,Flags).Invoke(m,args);
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"placement-copy-tone-v2.request";if(!File.Exists(request))return;File.Delete(request);
        try { Run(); } catch(Exception e){File.WriteAllText(Output+"cancel-and-copy.failed",e.ToString());Debug.LogException(e);}
    }
    static void Run()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        var report=new StringBuilder();
        var placement=(GameObject)Get(manager,"speakerPlacements");var ring=(GameObject)Get(manager,"ringObject");var animator=(Animator)Get(manager,"ringAnimator");
        var status=(TextMeshProUGUI)Get(manager,"placementStatusText");var guide=(TextMeshProUGUI)Get(manager,"placementGuideText");
        if(!status||!guide||!animator)throw new Exception("Missing existing guidance UI");
        report.AppendLine("Existing UI: "+status.name+" rect="+status.rectTransform.rect+" font="+status.fontSize+"; "+guide.name);
        string[] fields={"isPlacingSpeaker","currentHoldTime","waitForPlacementRelease","isVrUser","languageIndex"};
        object[] saved=fields.Select(n=>Get(manager,n)).ToArray();bool placementActive=placement.activeSelf,ringActive=ring.activeSelf;float ringTime=animator.GetFloat("RingTime");
        string oldStatus=status.text,oldGuide=guide.text;
        try
        {
            foreach(bool charging in new[]{false,true})
            {
                Set(manager,"isVrUser",false);Set(manager,"isPlacingSpeaker",!charging);Set(manager,"currentHoldTime",.8f);Set(manager,"waitForPlacementRelease",false);
                placement.SetActive(!charging);ring.SetActive(true);
                if(!(bool)Call(manager,"TryCancelDesktop",true)||(bool)Get(manager,"isPlacingSpeaker")||(float)Get(manager,"currentHoldTime")!=0f||!(bool)Get(manager,"waitForPlacementRelease")||placement.activeSelf||ring.activeSelf)throw new Exception("Q cancel state failed");
                report.AppendLine("PASS Q cancel "+(charging?"during hold":"during placement")+": preview/ring hidden, timer reset, release latch set");
            }
            Set(manager,"isVrUser",false);Set(manager,"isPlacingSpeaker",true);placement.SetActive(true);
            manager.InputDrop(true,default(VRC.Udon.Common.UdonInputEventArgs));
            if((bool)Get(manager,"isPlacingSpeaker")||placement.activeSelf)throw new Exception("Right-click fallback failed");
            Set(manager,"isVrUser",true);Set(manager,"isPlacingSpeaker",true);
            if((bool)Call(manager,"TryCancelDesktop",true)||!(bool)Get(manager,"isPlacingSpeaker"))throw new Exception("Desktop key affected VR");
            report.AppendLine("PASS right-click fallback; desktop cancel ignored for VR");
            for(int language=0;language<4;language++)
            {
                Set(manager,"languageIndex",language);
                foreach(bool vr in new[]{false,true})
                {
                    Set(manager,"isVrUser",vr);
                    guide.text=(string)Call(manager,"LocalizedGuide");guide.ForceMeshUpdate(true);
                    if(guide.textInfo.lineCount>1||guide.isTextOverflowing||!guide.text.Contains(" | "))throw new Exception("Guide formatting failed");
                    report.AppendLine("PASS guide "+language+"/"+vr+": "+guide.text);
                }
                for(int state=0;state<6;state++)
                {
                    status.text=(string)Call(manager,"LocalizedStatus",state);status.ForceMeshUpdate(true);
                    if(status.textInfo.lineCount>1||status.isTextOverflowing)throw new Exception("Status overflow language="+language+" state="+state);
                    if(status.text.Contains("·")||(state>=4&&!status.text.Contains(" | ")))throw new Exception("Status separator mismatch");
                    foreach(char ch in status.text+guide.text)if(!char.IsWhiteSpace(ch)&&!status.font.HasCharacter(ch,true,true))throw new Exception("Missing translated glyph: "+ch);
                    report.AppendLine("PASS one line "+language+"/"+state+": "+status.text);
                }
            }
        }
        finally
        {
            for(int i=0;i<fields.Length;i++)Set(manager,fields[i],saved[i]);placement.SetActive(placementActive);ring.SetActive(ringActive);animator.SetFloat("RingTime",ringTime);status.text=oldStatus;guide.text=oldGuide;
        }
        EditorSceneManager.SaveScene(scene,Output+"TEST.before-cancel-copy-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
        manager._OnWorldLanguageChanged();
        EditorUtility.SetDirty(status);EditorUtility.SetDirty(guide);
        UdonSharpEditorUtility.CopyProxyToUdon(manager);
        EditorUtility.SetDirty(manager);EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager));
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        report.AppendLine("Original TEST saved. Tests invoke cancel branch, not physical VRChat keyboard input; client Q/G retest required.");
        File.WriteAllText(Output+"placement-copy-tone.done",report.ToString());
    }
}
