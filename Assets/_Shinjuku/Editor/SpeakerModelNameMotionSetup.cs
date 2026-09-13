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
public static class SpeakerModelNameMotionSetup
{
    const string Output="output/speaker-model-name-20260914/";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly string[] Paths={"Assets/_Shinjuku/Scenes/TEST_PC.unity","Assets/_Shinjuku/Scenes/TEST_Quest.unity","Assets/_ShinjukuExhibition/Scenes/TechnicalExhibition.unity"};
    static SpeakerModelNameMotionSetup(){EditorApplication.update+=Poll;}
    static object Get(SpeakerManager m,string n)=>typeof(SpeakerManager).GetField(n,Flags).GetValue(m);
    static void Set(SpeakerManager m,string n,object value)=>typeof(SpeakerManager).GetField(n,Flags).SetValue(m,value);
    static object Call(SpeakerManager m,string n,params object[] args)=>typeof(SpeakerManager).GetMethod(n,Flags).Invoke(m,args);
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"apply-v2.request";if(!File.Exists(request))return;File.Delete(request);
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"apply.failed",e.ToString());Debug.LogException(e);}
    }
    static void Apply()
    {
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        Check(!UdonSharpProgramAsset.AnyUdonSharpScriptHasError(),"Udon compilation failed");
        var original=SceneManager.GetActiveScene();var report=new StringBuilder();
        try
        {
            foreach(string path in Paths)
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                bool saved=false;
                try
                {
                    var m=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                    var selector=(RectTransform)Get(m,"modelSelector");Check(selector,"Missing model selector");
                    var child=selector.Find("ModelName");Check(child,"Missing existing ModelName");
                    var label=child.GetComponent<TextMeshProUGUI>();Check(label,"Missing model name text");
                    Check(!selector.GetComponent<UnityEngine.UI.LayoutGroup>(),"Layout group would override animation");
                    Check(EditorSceneManager.SaveScene(scene,Output+scene.name+".before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
                    Set(m,"modelNameLabel",label);Set(m,"modelNameRect",label.rectTransform);Verify(m,label,report);
                    UdonSharpEditorUtility.CopyProxyToUdon(m);
                    var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
                    Check(backing.publicVariables.TryGetVariableValue("modelNameLabel",out TextMeshProUGUI linked)&&linked==label,"Udon label reference not linked");
                    Check(backing.publicVariables.TryGetVariableValue("modelNameRect",out RectTransform linkedRect)&&linkedRect==label.rectTransform,"Udon rect reference not linked");
                    EditorUtility.SetDirty(m);EditorUtility.SetDirty(backing);EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene),"Save failed");saved=true;
                    report.AppendLine("SAVED "+path);File.WriteAllText(Output+"apply.progress",report.ToString());
                }
                finally{if(opened&&saved)EditorSceneManager.CloseScene(scene,true);}
            }
            File.WriteAllText(Output+"apply.done",report+"PASS all three scenes; Editor method tests, not VRChat client tests.");
        }
        finally{if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);}
    }
    static void Verify(SpeakerManager m,TextMeshProUGUI label,StringBuilder report)
    {
        string[] fields={"isPlacingSpeaker","placementPending","isVrUser","modelStickArmed","modelVisualCached","modelVisualScale","modelVisualRotation","modelNameCached","modelNamePosition","modelNameColor","modelSwitchActive","modelSwitchMidpoint","modelSwitchDirection","modelSwitchElapsed","selectedPreviewModel","currentHoldTime","waitForPlacementRelease"};
        var values=fields.Select(n=>Get(m,n)).ToArray();
        var preview=(Transform)Get(m,"modelPreviewVisual");var scale=preview.localScale;var rotation=preview.localRotation;
        var position=label.rectTransform.anchoredPosition;var color=label.color;
        var selector=(RectTransform)Get(m,"modelSelector");var selectorScale=selector.localScale;
        var accents=((UnityEngine.UI.Image[])Get(m,"previousModelAccents")).Concat((UnityEngine.UI.Image[])Get(m,"nextModelAccents")).ToArray();var colors=accents.Select(i=>i.color).ToArray();
        var placement=(GameObject)Get(m,"speakerPlacements");var ring=(GameObject)Get(m,"ringObject");var animator=(Animator)Get(m,"ringAnimator");
        bool placementActive=placement.activeSelf,ringActive=ring.activeSelf;float ringTime=animator.GetFloat("RingTime");
        try
        {
            Set(m,"placementPending",false);Set(m,"modelVisualCached",false);Set(m,"modelNameCached",false);
            foreach(bool vr in new[]{false,true})foreach(int direction in new[]{-1,1})
            {
                Call(m,"ResetModelTransition");Set(m,"isPlacingSpeaker",true);Set(m,"isVrUser",vr);
                if(vr){Call(m,"HandleModelStick",0f);Call(m,"HandleModelStick",direction<0?1f:-1f);}
                else Call(m,"HandleDesktopModelKeys",direction<0,direction>0);
                Check((bool)Get(m,"modelSwitchActive"),"Input did not start switch");
                Call(m,"AdvanceModelTransition",.08f);
                Check((label.rectTransform.anchoredPosition.x-position.x)*direction<0,"Wrong outgoing direction");
                Check(Mathf.Abs(label.color.a-color.a*.5f)<.001f,"Outgoing fade failed");
                Call(m,"RequestModelSwitch",-direction);
                Check((int)Get(m,"modelSwitchDirection")==direction,"Repeated input restarted transition");
                Call(m,"AdvanceModelTransition",.08f);Check(label.color.a<.001f,"Midpoint not invisible");
                Call(m,"AdvanceModelTransition",.08f);
                Check((label.rectTransform.anchoredPosition.x-position.x)*direction>0,"Wrong incoming direction");
                Check(Mathf.Abs(label.color.a-color.a*.5f)<.001f,"Incoming fade failed");
                Call(m,"AdvanceModelTransition",1f);
                Check(label.rectTransform.anchoredPosition==position&&label.color==color,"Completion did not restore name");
                Check(!(bool)Get(m,"modelSwitchActive")&&(int)Get(m,"selectedPreviewModel")==0,"Single-model wrap failed");
                Call(m,"RequestModelSwitch",direction);Call(m,"AdvanceModelTransition",.12f);Call(m,"CancelPlacement");
                Check(label.rectTransform.anchoredPosition==position&&label.color==color,"Cancel did not restore name");
                Check(selector.localScale==selectorScale,"Canvas scale changed");
                report.AppendLine(m.gameObject.scene.name+": "+(vr?"VR":"PC")+" direction="+direction+" slide/fade/midpoint/repeat/cancel/completion PASS");
            }
        }
        finally
        {
            Call(m,"ResetModelTransition");for(int i=0;i<fields.Length;i++)Set(m,fields[i],values[i]);
            preview.localScale=scale;preview.localRotation=rotation;label.rectTransform.anchoredPosition=position;label.color=color;
            for(int i=0;i<accents.Length;i++)accents[i].color=colors[i];
            placement.SetActive(placementActive);ring.SetActive(ringActive);animator.SetFloat("RingTime",ringTime);
        }
    }
}
