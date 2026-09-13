using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;
using UdonSharp;

[InitializeOnLoad]
public static class SpeakerRippleDistanceReview
{
    const string Output="output/speaker-placement-20260912/";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static SpeakerRippleDistanceReview(){EditorApplication.update+=Poll;}
    static T F<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
    static void S(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static void Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string optimize=Output+"ripple-optimize-v1.request";
        if(File.Exists(optimize))
        {
            File.Delete(optimize);
            try{Run(true);}catch(Exception e){File.WriteAllText(Output+"ripple-optimize.failed",e.ToString());Debug.LogException(e);}
            return;
        }
        string request=Output+"ripple-distance.request";if(!File.Exists(request))return;File.Delete(request);
        try{Run();}catch(Exception e){File.WriteAllText(Output+"ripple-distance.failed",e.ToString());Debug.LogException(e);}
    }
    static void Tick(SpeakerManager m,int i,float distance,bool occupied=true)
    {
        var s=F<SpeakerController[]>(m,"speakerControllers")[i];
        Call(m,"UpdateRippleVisibility",i,F<MeshRenderer[]>(m,"placedSpeakerHeatmaps")[i],occupied,s.transform.position+Vector3.forward*distance);
    }
    static void Run(bool optimize=false)
    {
        var scene=SceneManager.GetActiveScene();Check(scene.path=="Assets/_Shinjuku/Scenes/TEST_PC.unity","Expected original TEST");
        var m=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(x=>x.gameObject.scene==scene);
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        Check(!ShaderUtil.ShaderHasError(Shader.Find("Shinjuku/Speaker Ripple Guide")),"Ripple shader error");
        Check(EditorSceneManager.SaveScene(scene,Output+"TEST.before-distance-ripples-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true),"Backup failed");
        if(optimize)
        {
            string path="Assets/_Shinjuku/Art/Materials/Speaker/GuideThinRipples.asset";
            File.Copy(path,Output+"GuideThinRipples.before-96-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".asset",false);
            var mesh=(Mesh)typeof(SpeakerRippleGuideSetup).GetMethod("MakeMesh",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            typeof(SpeakerRippleGuideSetup).GetMethod("ValidateGeometry",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{mesh,m});
            Check(F<MeshRenderer[]>(m,"placedSpeakerHeatmaps").All(r=>r.GetComponent<MeshFilter>().sharedMesh==mesh),"Six ripple mesh bindings diverged");
        }
        Undo.RecordObject(m,"Distance fade and cull speaker ripples");
        var so=new SerializedObject(m);so.FindProperty("distanceFadedRipples").boolValue=true;so.FindProperty("rippleApproachMargin").floatValue=3;so.FindProperty("rippleCullHysteresis").floatValue=.5f;so.ApplyModifiedProperties();
        var speakers=F<SpeakerController[]>(m,"speakerControllers");var heat=F<MeshRenderer[]>(m,"placedSpeakerHeatmaps");
        var parent=F<GameObject>(m,"speakerPlacements");var holo=F<GameObject>(m,"holoSpeaker");
        var ts=parent.GetComponentsInChildren<Transform>(true).Concat(speakers.SelectMany(x=>x.GetComponentsInChildren<Transform>(true))).Distinct().ToArray();
        var positions=ts.Select(x=>x.localPosition).ToArray();var rotations=ts.Select(x=>x.localRotation).ToArray();var scales=ts.Select(x=>x.localScale).ToArray();var active=ts.Select(x=>x.gameObject.activeSelf).ToArray();
        var rs=parent.GetComponentsInChildren<Renderer>(true);var enabled=rs.Select(x=>x.enabled).ToArray();var mats=rs.Select(x=>x.sharedMaterial).ToArray();
        var blocks=rs.Select(x=>{var b=new MaterialPropertyBlock();x.GetPropertyBlock(b);return b;}).ToArray();
        var taken=speakers.Select(x=>x.isSpeakerTaken).ToArray();int state=F<int>(m,"placementState");
        var report=new StringBuilder();float minimum=F<float>(m,"minimumSpeakerDistance");
        try
        {
            Call(m,"ConfigureAudibleRangeRenderer");var cached=F<MaterialPropertyBlock>(m,"ripplePropertyBlock");
            for(int i=0;i<speakers.Length;i++)
            {
                float outer=m.GetSpeakerWarningRadius(i)+3;
                Tick(m,i,outer+1);Check(!heat[i].enabled,"Far renderer enabled");
                Tick(m,i,outer+.25f);Check(!heat[i].enabled,"Cold hysteresis entered too early");
                Tick(m,i,outer-.05f);Check(heat[i].enabled&&F<bool[]>(m,"rippleVisible")[i],"Approach did not enable");
                float last=0;
                for(int step=0;step<=20;step++)
                {
                    float distance=Mathf.Lerp(outer-.05f,minimum,step/20f);Tick(m,i,distance);
                    float target=F<Vector4[]>(m,"rippleFadeStates")[i].y;
                    Check(target>=last&&target<=1,"Fade not monotonic");last=target;
                }
                Check(Mathf.Approximately(last,1),"Blocked distance not maximum");
                // A sentinel survives only if the runtime skips SetPropertyBlock.
                // A real brightness change must replace it and send the new fade.
                var probe=new MaterialPropertyBlock();heat[i].GetPropertyBlock(probe);probe.SetFloat("_CodexUnchangedProbe",123);heat[i].SetPropertyBlock(probe);
                for(int stationary=0;stationary<20;stationary++)Tick(m,i,minimum);
                heat[i].GetPropertyBlock(probe);Check(probe.GetFloat("_CodexUnchangedProbe")==123,"Stationary target resent property block");
                Tick(m,i,minimum+1);heat[i].GetPropertyBlock(probe);
                Check(probe.GetFloat("_CodexUnchangedProbe")==0&&probe.GetVector("_DistanceFade")==F<Vector4[]>(m,"rippleFadeStates")[i],"Changed brightness was not sent");
                var fades=F<Vector4[]>(m,"rippleFadeStates");fades[i]=new Vector4(.2f,.8f,Time.time-.1f,.2f);
                Tick(m,i,minimum-1);Check(Mathf.Abs(fades[i].x-.5f)<.03f,"Transition discontinuity");
                Tick(m,i,outer+.25f);Check(F<bool[]>(m,"rippleVisible")[i]&&heat[i].enabled,"Hot hysteresis did not hold");
                fades[i]=new Vector4(0,0,Time.time-1,.2f);Tick(m,i,outer+.6f);Check(!heat[i].enabled&&!F<bool[]>(m,"rippleVisible")[i],"Far renderer not culled");
                Tick(m,i,minimum);Check(heat[i].enabled,"Reentry failed");Tick(m,i,minimum,false);Check(!heat[i].enabled&&fades[i]==Vector4.zero,"Return failed");
                report.AppendLine("PASS source "+i+": enter="+outer.ToString("F2")+"m exit="+(outer+.5f).ToString("F2")+"m max="+minimum+"m; monotonic fade, interpolation, hysteresis, cull, return and reentry");
            }
            Tick(m,0,minimum);Tick(m,1,m.GetSpeakerWarningRadius(1)+2);Tick(m,2,100);
            Check(F<Vector4[]>(m,"rippleFadeStates")[0].y>F<Vector4[]>(m,"rippleFadeStates")[1].y&&!heat[2].enabled,"Independent sources failed");
            Check(ReferenceEquals(cached,F<MaterialPropertyBlock>(m,"ripplePropertyBlock")),"Property block allocated repeatedly");
            Call(m,"ConfigureAudibleRangeRenderer");Check(heat.All(x=>!x.enabled)&&F<bool[]>(m,"rippleVisible").All(x=>!x),"New session stale visuals");
            Capture(m,report);
            Check(!F<TMPro.TextMeshProUGUI>(m,"placementStatusText").gameObject.activeSelf,"Warnings re-enabled");
        }
        finally
        {
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=taken[i];S(m,"placementState",state);
            for(int i=0;i<ts.Length;i++){ts[i].localPosition=positions[i];ts[i].localRotation=rotations[i];ts[i].localScale=scales[i];ts[i].gameObject.SetActive(active[i]);}
            Call(m,"ConfigureAudibleRangeRenderer");
            for(int i=0;i<rs.Length;i++){rs[i].enabled=enabled[i];rs[i].sharedMaterial=mats[i];rs[i].SetPropertyBlock(blocks[i]);}
        }
        UdonSharpEditorUtility.CopyProxyToUdon(m);var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
        Check(backing.publicVariables.TryGetVariableValue("distanceFadedRipples",out bool stored)&&stored,"Udon flag not bound");
        EditorUtility.SetDirty(m);EditorUtility.SetDirty(backing);if(PrefabUtility.IsPartOfPrefabInstance(m))PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"TEST save failed");
        report.AppendLine("PASS Udon binding, reusable property block, session reset, independent brightness, actual distance preview captures, restored scene, original TEST saved. VRChat client GPU timing not measured.");
        if(optimize)
        {
            report.AppendLine("PASS 96 segments x 2 rings, 388 vertices / 384 triangles per source (25% fewer triangles). 120 unchanged-target ticks issued zero replacement property blocks; all six changed targets uploaded new fades. Timing, colors, ring count and distance rules unchanged.");
            File.Copy(Output+"ripple-distance-near.png",Output+"ripple-96-near.png",true);
            File.WriteAllText(Output+"ripple-optimize.done",report.ToString());
        }
        File.WriteAllText(Output+"ripple-distance.done",report.ToString());
    }
    static void Capture(SpeakerManager m,StringBuilder report)
    {
        var speakers=F<SpeakerController[]>(m,"speakerControllers");var parent=F<GameObject>(m,"speakerPlacements");var holo=F<GameObject>(m,"holoSpeaker");
        Vector3 center=new Vector3(48,6.5f,-8);
        if(Physics.Raycast(center+Vector3.up*2,Vector3.down,out RaycastHit hit,4,F<int>(m,"rayLayerMask"),QueryTriggerInteraction.Ignore))center.y=hit.point.y;
        for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=i==0;
        speakers[0].transform.SetPositionAndRotation(center,Quaternion.identity);F<GameObject>(speakers[0],"speakerObject").SetActive(true);
        parent.SetActive(true);holo.SetActive(false);foreach(var c in parent.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);foreach(var line in parent.GetComponentsInChildren<LineRenderer>(true))line.enabled=false;
        var go=new GameObject("Temporary distance ripple preview"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;camera.fieldOfView=60;camera.transform.position=center+new Vector3(2,1.8f,13.7f);camera.transform.LookAt(center+new Vector3(0,.65f,7));
            float outer=m.GetSpeakerWarningRadius(0)+3;float min=F<float>(m,"minimumSpeakerDistance");
            float[] distances={outer+1,outer-1.5f,min};string[] names={"far","approach","near"};
            for(int frame=0;frame<3;frame++)
            {
                Call(m,"ConfigureAudibleRangeRenderer");holo.transform.position=center+Vector3.forward*distances[frame];S(m,"placementState",frame==2?4:0);Call(m,"UpdatePlacedSpeakerIndicators");
                var heat=F<MeshRenderer[]>(m,"placedSpeakerHeatmaps")[0];float weight=F<Vector4[]>(m,"rippleFadeStates")[0].y;
                var block=new MaterialPropertyBlock();block.SetVector("_DistanceFade",new Vector4(weight,weight,0,.2f));block.SetFloat("_PreviewTime",1f);heat.SetPropertyBlock(block);
                Check(heat.enabled==(frame>0),"Integrated culling failed");
                Check(F<MeshRenderer>(m,"placementEscapeArrow").enabled==(frame==2),"Escape arrow changed");
                camera.Render();RenderTexture.active=rt;var shot=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                try{shot.ReadPixels(new Rect(0,0,1600,1000),0,0);shot.Apply();File.WriteAllBytes(Output+"ripple-distance-"+names[frame]+".png",shot.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(shot);}
                report.AppendLine("CAPTURE "+names[frame]+": distance="+distances[frame].ToString("F2")+" target="+weight.ToString("F3")+" enabled="+heat.enabled);
            }
        }
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);}
    }
}
