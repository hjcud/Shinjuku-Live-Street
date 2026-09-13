using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

[InitializeOnLoad]
public static class SpeakerQuietGuideSetup
{
    const string Folder="Assets/_Shinjuku/Art/Materials/Speaker/";
    const string Output="output/speaker-placement-20260912/";
    static SpeakerQuietGuideSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string minimalRequest=Output+"minimal-placement-guide.request";
        if(File.Exists(minimalRequest))
        {
            File.Delete(minimalRequest);
            try { ApplyMinimalGuide(); }
            catch(Exception e) { File.WriteAllText(Output+"minimal-placement-guide.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string removeIconRequest=Output+"remove-ground-icon-v2.request";
        if(File.Exists(removeIconRequest))
        {
            File.Delete(removeIconRequest);
            try { RemoveGroundIcon(); }
            catch(Exception e) { File.WriteAllText(Output+"remove-ground-icon.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string multiRequest=Output+"quiet-guide-multiple.request";
        if(File.Exists(multiRequest))
        {
            File.Delete(multiRequest);
            try
            {
                var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
                var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
                CaptureAndTest(manager,true);
                File.WriteAllText(Output+"quiet-guide-multiple.done",DateTime.Now+" Three-speaker editor preview captured. Original state restored; no scene save or guide changes.");
            }
            catch(Exception e){File.WriteAllText(Output+"quiet-guide-multiple.failed",e.ToString());Debug.LogException(e);}
            return;
        }
        string request=Output+"quiet-guide.request";if(!File.Exists(request))return;File.Delete(request);
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"quiet-guide.failed",e.ToString());Debug.LogException(e);}
    }
    static T F<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    static void S(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static void Call(object obj,string name)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    static void Dirty(UnityEngine.Object obj){EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
    sealed class Shape
    {
        public readonly List<Vector3> v=new List<Vector3>();public readonly List<int> t=new List<int>();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=v.Count;v.AddRange(new[]{a,b,c,d});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
        public void Stroke(Vector2 a,Vector2 b,float width,bool ground=false)
        {
            Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
            Quad(P(a+n,ground),P(b+n,ground),P(b-n,ground),P(a-n,ground));
        }
        public static Vector3 P(Vector2 p,bool ground)=>ground?new Vector3(p.x,0,p.y):new Vector3(p.x,p.y,0);
        public void Arc(float radius,float start,float end,int segments,float width,bool ground,Vector2 offset)
        {
            for(int i=0;i<segments;i++)
            {
                float a=Mathf.Lerp(start,end,i/(float)segments),b=Mathf.Lerp(start,end,(i+1)/(float)segments);
                Stroke(offset+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,offset+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,width,ground);
            }
        }
        public void Icon(Vector2 offset,float scale,bool ground)
        {
            var outline=new[]{new Vector2(-.22f,-.08f),new Vector2(-.10f,-.08f),new Vector2(.02f,-.20f),new Vector2(.02f,.20f),new Vector2(-.10f,.08f),new Vector2(-.22f,.08f),new Vector2(-.22f,-.08f)};
            for(int i=1;i<outline.Length;i++)Stroke(offset+outline[i-1]*scale,offset+outline[i]*scale,.024f*scale,ground);
            Arc(.16f*scale,-.8f,.8f,10,.020f*scale,ground,offset+new Vector2(.03f,0)*scale);
            Arc(.26f*scale,-.85f,.85f,12,.020f*scale,ground,offset+new Vector2(.03f,0)*scale);
        }
        public Mesh Asset(string name)
        {
            string path=Folder+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh{name=name};AssetDatabase.CreateAsset(mesh,path);}else Undo.RecordObject(mesh,"Update quiet guidance mesh");
            mesh.Clear();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Dirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);return mesh;
        }
    }
    static Material Mat(string name,Color color,bool billboard=false)
    {
        string path=Folder+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=Shader.Find("Shinjuku/Quiet Placement Guide");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Quiet guide shader unavailable");
        if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else Undo.RecordObject(mat,"Quiet guide material");
        mat.SetColor("_Color",color);mat.SetFloat("_Billboard",billboard?1:0);Dirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
    }
    static MeshRenderer Object(Transform parent,string name,Mesh mesh,Material material)
    {
        var child=parent.Find(name);if(!child){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Quiet speaker guidance");go.transform.SetParent(parent,false);child=go.transform;}
        child.gameObject.layer=2;child.localScale=Vector3.one;child.rotation=Quaternion.identity;
        var filter=child.GetComponent<MeshFilter>();if(!filter)filter=Undo.AddComponent<MeshFilter>(child.gameObject);filter.sharedMesh=mesh;
        var renderer=child.GetComponent<MeshRenderer>();if(!renderer)renderer=Undo.AddComponent<MeshRenderer>(child.gameObject);
        renderer.sharedMaterial=material;renderer.enabled=false;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        renderer.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;renderer.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
        Dirty(filter);Dirty(renderer);Dirty(child);return renderer;
    }
    static void Apply()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
        var parent=F<GameObject>(manager,"speakerPlacements").transform;
        if((parent.lossyScale-Vector3.one).sqrMagnitude>.001f)throw new Exception("Guide parent scale must remain one");
        var speakers=F<SpeakerController[]>(manager,"speakerControllers");var boundaries=F<MeshRenderer[]>(manager,"placedSpeakerHeatmaps");
        if(boundaries==null||boundaries.Length!=speakers.Length)throw new Exception("Expected six existing range renderers");
        Undo.RegisterFullObjectHierarchyUndo(parent.gameObject,"Quiet placement guidance");Undo.RecordObject(manager,"Bind quiet guidance");
        var quiet=Mat("GuideBoundaryQuiet",new Color(.87f,.82f,.62f,.32f));
        var focus=Mat("GuideBoundaryFocused",new Color(.97f,.88f,.51f,.82f));
        var badgeMat=Mat("GuideSpeakerBadge",new Color(.94f,.91f,.80f,.80f),true);
        var groundMat=Mat("GuideGroundMark",new Color(.87f,.84f,.71f,.38f));
        var arrowMat=Mat("GuideEscapeArrow",new Color(.98f,.90f,.60f,.85f));
        var dashed=new Shape();for(int i=0;i<72;i++)dashed.Arc(.5f,i*Mathf.PI/36,(i+.38f)*Mathf.PI/36,3,.0028f,true,Vector2.zero);
        var boundaryMesh=dashed.Asset("GuideDashedBoundary");
        var badge=new Shape();badge.Icon(Vector2.zero,1,false);badge.Arc(.39f,0,Mathf.PI*2,56,.012f,false,Vector2.zero);var badgeMesh=badge.Asset("GuideSpeakerIcon");
        var ground=new Shape();ground.Arc(.65f,0,Mathf.PI*2,64,.022f,true,Vector2.zero);var groundMesh=ground.Asset("GuideGroundIcon");
        var arrowMesh=BuildChevron();
        var so=new SerializedObject(manager);so.FindProperty("quietGuideMaterial").objectReferenceValue=quiet;so.FindProperty("focusedGuideMaterial").objectReferenceValue=focus;
        var badges=so.FindProperty("placedSpeakerBadges");var marks=so.FindProperty("placedSpeakerGroundMarks");badges.arraySize=marks.arraySize=speakers.Length;
        for(int i=0;i<speakers.Length;i++)
        {
            boundaries[i].GetComponent<MeshFilter>().sharedMesh=boundaryMesh;boundaries[i].sharedMaterial=quiet;boundaries[i].enabled=false;
            Dirty(boundaries[i]);Dirty(boundaries[i].GetComponent<MeshFilter>());
            badges.GetArrayElementAtIndex(i).objectReferenceValue=Object(parent,"SpeakerBadge_"+i,badgeMesh,badgeMat);
            marks.GetArrayElementAtIndex(i).objectReferenceValue=Object(parent,"SpeakerGroundMark_"+i,groundMesh,groundMat);
        }
        so.FindProperty("placementEscapeArrow").objectReferenceValue=Object(parent,"PlacementEscapeArrow",arrowMesh,arrowMat);so.ApplyModifiedProperties();
        foreach(var line in F<LineRenderer[]>(manager,"placedSpeakerBeacons"))line.enabled=false;
        var legacy=F<LineRenderer>(manager,"audibleRangeRenderer");if(legacy)legacy.enabled=false;Call(manager,"ConfigureAudibleRangeRenderer");Call(manager,"UpdatePlacedSpeakerIndicators");manager._OnWorldLanguageChanged();
        CaptureAndTest(manager);
        UdonSharpEditorUtility.CopyProxyToUdon(manager);Dirty(manager);var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);Dirty(backing);
        if(!backing.publicVariables.TryGetVariableValue("placedSpeakerBadges",out MeshRenderer[] stored)||stored.Length!=speakers.Length)throw new Exception("Udon badge bindings missing");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"TEST.with-quiet-speaker-guidance.unity",true);
        File.WriteAllText(Output+"quiet-guide.done",DateTime.Now+" PASS: quiet/focused materials, six occupancy/return lifecycles, no tall beams or heatmap fill, outward arrow, restored preview state, Udon bindings. Original scene unsaved; recovery copy saved.");
    }
    static Mesh BuildChevron()
    {
        var arrow=new Shape();
        // One connected V-shaped strip. The former two-quad version overlapped at
        // the tip, causing a protruding diamond and doubled transparent blending.
        const float half=.0275f;
        var p0=new Vector2(-.24f,.60f);var p1=new Vector2(0,.84f);var p2=new Vector2(.24f,.60f);
        var d0=(p1-p0).normalized;var d1=(p2-p1).normalized;
        var n0=new Vector2(-d0.y,d0.x)*half;var n1=new Vector2(-d1.y,d1.x)*half;
        var miter=(n0.normalized+n1.normalized).normalized;
        float miterLength=half/Vector2.Dot(miter,n0.normalized);
        arrow.v.AddRange(new[]{Shape.P(p0+n0,true),Shape.P(p1+miter*miterLength,true),Shape.P(p2+n1,true),Shape.P(p2-n1,true),Shape.P(p1-miter*miterLength,true),Shape.P(p0-n0,true)});
        arrow.t.AddRange(new[]{0,1,4,0,4,5,1,2,3,1,3,4});
        return arrow.Asset("GuideShortArrow");
    }
    static void ApplyMinimalGuide()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
        var status=F<TMPro.TextMeshProUGUI>(manager,"placementStatusText");var guide=F<TMPro.TextMeshProUGUI>(manager,"placementGuideText");
        var renderer=F<MeshRenderer>(manager,"placementEscapeArrow");var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
        if(!status||!guide||AssetDatabase.GetAssetPath(mesh)!=Folder+"GuideShortArrow.asset")throw new Exception("Expected existing status, controls and arrow");
        string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss");
        EditorSceneManager.SaveScene(scene,Output+"TEST.before-minimal-guide-"+stamp+".unity",true);
        File.Copy(AssetDatabase.GetAssetPath(mesh),Output+"GuideShortArrow.before-chevron-"+stamp+".asset",false);
        var report=new System.Text.StringBuilder();
        report.AppendLine("UI="+status.transform.parent.name+"/"+status.name+"; controls="+guide.name);
        mesh=BuildChevron();
        if(mesh.vertexCount!=6||mesh.triangles.Length!=12||mesh.bounds.min.z<.57f||mesh.bounds.max.z>.89f)throw new Exception("Unexpected chevron geometry");
        int oldState=F<int>(manager,"placementState");bool oldDisabled=F<bool>(manager,"isHoloDisabled");bool oldVr=F<bool>(manager,"isVrUser");int oldLanguage=F<int>(manager,"languageIndex");
        var anim=F<Animator>(manager,"holoAnimator");int message=anim.GetInteger("MessageStatus");bool disabled=anim.GetBool("HoloDisabled"),warning=anim.GetBool("HoloWarning");
        try
        {
            foreach(string language in new[]{"en","ko","ja","zh"})foreach(bool vr in new[]{false,true})
            {
                S(manager,"isVrUser",vr);
                typeof(SpeakerManager).GetMethod("ApplyLanguage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{language});
                if(string.IsNullOrWhiteSpace(guide.text)||!guide.gameObject.activeSelf)throw new Exception("Controls unavailable");
                for(int state=0;state<6;state++)
                {
                    bool blocked=state>=1&&state<=4;
                    typeof(SpeakerManager).GetMethod("SetPlacementState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{blocked,state});
                    if(status.gameObject.activeSelf||status.text!=""||F<bool>(manager,"isHoloDisabled")!=blocked)throw new Exception("Status suppression changed placement validation");
                }
                report.AppendLine("PASS "+language+" VR="+vr+" only controls: "+guide.text);
            }
        }
        finally
        {
            S(manager,"placementState",oldState);S(manager,"isHoloDisabled",oldDisabled);S(manager,"isVrUser",oldVr);S(manager,"languageIndex",oldLanguage);
            anim.SetInteger("MessageStatus",message);anim.SetBool("HoloDisabled",disabled);anim.SetBool("HoloWarning",warning);
            manager._OnWorldLanguageChanged();
        }
        Undo.RecordObject(status.gameObject,"Hide placement status text");Undo.RecordObject(status,"Clear placement status text");
        status.text="";status.gameObject.SetActive(false);Dirty(status);Dirty(status.gameObject);Dirty(guide);
        CaptureAndTest(manager);
        File.Copy(Output+"quiet-guide-eye-level.png",Output+"minimal-chevron-preview.png",true);
        UdonSharpEditorUtility.CopyProxyToUdon(manager);Dirty(manager);Dirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager));
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        report.AppendLine("PASS 48 state/language/device checks; validation unchanged; chevron=6 shared vertices, one connected strip, no shaft; quiet-guide lifecycle tests passed. TEST and mesh saved. VRChat client not exercised.");
        File.WriteAllText(Output+"minimal-placement-guide.done",report.ToString());
    }
    static void RemoveGroundIcon()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var manager=UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
        var marks=F<MeshRenderer[]>(manager,"placedSpeakerGroundMarks");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"GuideGroundIcon.asset");
        if(!mesh||marks==null||marks.Length!=6||marks.Any(m=>!m||m.GetComponent<MeshFilter>().sharedMesh!=mesh))throw new Exception("Expected six shared ground marks");
        string backup=Output+"GuideGroundIcon.before-removal-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".asset";
        File.Copy(AssetDatabase.GetAssetPath(mesh),backup,false);
        // Keep the location ring; remove only the redundant horizontal speaker pictogram.
        var ring=new Shape();ring.Arc(.65f,0,Mathf.PI*2,64,.022f,true,Vector2.zero);
        mesh=ring.Asset("GuideGroundIcon");
        if(mesh.vertexCount!=256||mesh.vertices.Any(v=>new Vector2(v.x,v.z).magnitude>.662f))throw new Exception("Unexpected geometry outside location ring");
        CaptureAndTest(manager);
        File.Copy(Output+"quiet-guide-eye-level.png",Output+"ground-icon-removed.png",true);
        File.WriteAllText(Output+"remove-ground-icon.done",DateTime.Now+" PASS: removed floor speaker pictogram from shared mesh for all six speakers; 256 ring vertices only. Overhead badges, boundary dashes and escape arrow preserved. Occupancy/return tests passed; preview state restored. Shared mesh saved; no scene layout changes. Recoverable mesh backup: "+backup);
    }
    static void CaptureAndTest(SpeakerManager manager,bool multiple=false)
    {
        var speakers=F<SpeakerController[]>(manager,"speakerControllers");var parent=F<GameObject>(manager,"speakerPlacements");var holo=F<GameObject>(manager,"holoSpeaker");
        var ts=parent.GetComponentsInChildren<Transform>(true).Concat(speakers.SelectMany(s=>s.GetComponentsInChildren<Transform>(true))).Distinct().ToArray();
        var active=ts.Select(t=>t.gameObject.activeSelf).ToArray();var pos=ts.Select(t=>t.localPosition).ToArray();var rot=ts.Select(t=>t.localRotation).ToArray();var scales=ts.Select(t=>t.localScale).ToArray();
        var rs=parent.GetComponentsInChildren<Renderer>(true);var en=rs.Select(r=>r.enabled).ToArray();var mats=rs.Select(r=>r.sharedMaterial).ToArray();
        var lines=parent.GetComponentsInChildren<LineRenderer>(true);var points=lines.Select(l=>{var p=new Vector3[l.positionCount];l.GetPositions(p);return p;}).ToArray();
        var taken=speakers.Select(s=>s.isSpeakerTaken).ToArray();var speakerPositions=speakers.Select(s=>s.transform.position).ToArray();int state=F<int>(manager,"placementState");
        var cameraObject=new GameObject("Temporary quiet guidance camera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;
        try
        {
            var center=new Vector3(48,6.5f,-8);if(Physics.Raycast(center+Vector3.up*2,Vector3.down,out RaycastHit floor,4,F<int>(manager,"rayLayerMask"),QueryTriggerInteraction.Ignore))center.y=floor.point.y;
            speakers[0].transform.SetPositionAndRotation(center,Quaternion.identity);F<GameObject>(speakers[0],"speakerObject").SetActive(true);
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=i==0;
            parent.SetActive(true);holo.SetActive(false);foreach(var c in parent.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);foreach(var l in lines)l.enabled=false;
            Call(manager,"ConfigureAudibleRangeRenderer");camera.targetTexture=rt;camera.fieldOfView=60;
            if(multiple)
            {
                var offsets=new[]{new Vector3(-8,0,0),new Vector3(8,0,0),new Vector3(0,0,-12)};
                for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=i<3;
                for(int i=0;i<3;i++)
                {
                    Vector3 point=center+offsets[i];
                    if(Physics.Raycast(point+Vector3.up*2,Vector3.down,out RaycastHit ground,4,F<int>(manager,"rayLayerMask"),QueryTriggerInteraction.Ignore))point.y=ground.point.y;
                    speakers[i].transform.SetPositionAndRotation(point,Quaternion.identity);F<GameObject>(speakers[i],"speakerObject").SetActive(true);
                }
                holo.transform.position=center+new Vector3(-.6f,0,5);S(manager,"placementState",5);
                Call(manager,"ConfigureAudibleRangeRenderer");Call(manager,"UpdatePlacedSpeakerIndicators");
                camera.transform.position=center+new Vector3(7,24,35);camera.transform.LookAt(center+Vector3.back*3);
                camera.Render();RenderTexture.active=rt;
                var shot=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                try{shot.ReadPixels(new Rect(0,0,1600,1000),0,0);shot.Apply();File.WriteAllBytes(Output+"quiet-guide-multiple.png",shot.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(shot);}
                return;
            }
            for(int frame=0;frame<3;frame++)
            {
                holo.transform.position=center+Vector3.forward*(frame==0?14:9.2f);S(manager,"placementState",frame==0?0:5);Call(manager,"UpdatePlacedSpeakerIndicators");
                var boundary=F<MeshRenderer[]>(manager,"placedSpeakerHeatmaps")[0];var arrow=F<MeshRenderer>(manager,"placementEscapeArrow");
                if(boundary.sharedMaterial!=F<Material>(manager,frame==0?"quietGuideMaterial":"focusedGuideMaterial")||arrow.enabled!=(frame!=0))throw new Exception("Focus or arrow state mismatch");
                if(frame>0&&Vector3.Dot(manager.GetGuideEscapeDirection(holo.transform.position),Vector3.forward)<.9f)throw new Exception("Arrow does not increase clearance");
                camera.transform.position=center+(frame==2?new Vector3(2,1.8f,13.7f):new Vector3(5,12,22));
                camera.transform.LookAt(center+(frame==2?new Vector3(0,.65f,7):Vector3.forward*2));camera.Render();RenderTexture.active=rt;
                var shot=new Texture2D(1600,1000,TextureFormat.RGB24,false);try{shot.ReadPixels(new Rect(0,0,1600,1000),0,0);shot.Apply();File.WriteAllBytes(Output+new[]{"quiet-guide-normal.png","quiet-guide-warning.png","quiet-guide-eye-level.png"}[frame],shot.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(shot);}
            }
            S(manager,"placementState",0);
            speakers[1].isSpeakerTaken=true;speakers[1].transform.position=center+Vector3.forward*18.4f;
            var between=center+Vector3.forward*9.2f;var escape=manager.GetGuideEscapeDirection(between);
            float before=Mathf.Min(Vector3.Distance(between,center),Vector3.Distance(between,speakers[1].transform.position));
            float after=Mathf.Min(Vector3.Distance(between+escape*2,center),Vector3.Distance(between+escape*2,speakers[1].transform.position));
            if(escape.sqrMagnitude<.5f||after<=before+.05f)throw new Exception("Overlapping speaker escape does not increase worst clearance");
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=true;Call(manager,"UpdatePlacedSpeakerIndicators");
            if(F<LineRenderer[]>(manager,"placedSpeakerBeacons").Any(l=>l.enabled)||F<MeshRenderer[]>(manager,"placedSpeakerBadges").Any(r=>!r.enabled))throw new Exception("Six-speaker guide state failed");
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=false;Call(manager,"UpdatePlacedSpeakerIndicators");
            if(F<MeshRenderer[]>(manager,"placedSpeakerHeatmaps").Concat(F<MeshRenderer[]>(manager,"placedSpeakerBadges")).Concat(F<MeshRenderer[]>(manager,"placedSpeakerGroundMarks")).Any(r=>r.enabled))throw new Exception("Returned guidance visible");
        }
        finally
        {
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(rt);
            for(int i=0;i<speakers.Length;i++){speakers[i].isSpeakerTaken=taken[i];speakers[i].transform.position=speakerPositions[i];}S(manager,"placementState",state);
            for(int i=0;i<ts.Length;i++){ts[i].localPosition=pos[i];ts[i].localRotation=rot[i];ts[i].localScale=scales[i];ts[i].gameObject.SetActive(active[i]);}
            for(int i=0;i<rs.Length;i++){rs[i].enabled=en[i];rs[i].sharedMaterial=mats[i];}
            for(int i=0;i<lines.Length;i++){lines[i].positionCount=points[i].Length;lines[i].SetPositions(points[i]);}
            Call(manager,"ConfigureAudibleRangeRenderer");
        }
    }
}
