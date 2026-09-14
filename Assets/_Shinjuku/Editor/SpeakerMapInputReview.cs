using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using VRC.SDK3.Components;
using VRC.SDKBase;
using UdonSharpEditor;

[InitializeOnLoad]
public static class SpeakerMapInputReview
{
    const string Output="output/speaker-map-ui-20260912/";
    const string PrefabPath="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
    static SpeakerMapInputReview(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        foreach(string action in new[]{"map-input-inspect","map-input-fix"})
        {
            string request=Output+action+".request";if(!File.Exists(request))continue;File.Delete(request);
            try{Run(action=="map-input-fix");}catch(Exception e){File.WriteAllText(Output+action+".failed",e.ToString());Debug.LogException(e);}return;
        }
    }
    static void Dirty(UnityEngine.Object obj){EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
    static void FixLayers(GameObject root)
    {
        var canvas=root.transform.Find("MapCanvas");if(!canvas||!canvas.GetComponent<VRCUiShape>())throw new Exception("Expected existing map UI shape");
        // UI is reserved for menu-open interaction in VRChat. Keep unrelated custom layers unchanged.
        foreach(var t in canvas.GetComponentsInChildren<Transform>(true))if(t.gameObject.layer==5){Undo.RecordObject(t.gameObject,"Correct VRChat map input layer");t.gameObject.layer=0;Dirty(t.gameObject);}
    }
    static void Run(bool fix)
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        var panels=UnityEngine.Object.FindObjectsOfType<SpeakerMapPanel>(true).Where(p=>p.gameObject.scene==scene).ToArray();if(panels.Length!=2)throw new Exception("Expected two maps");
        var report=new StringBuilder();report.AppendLine(DateTime.Now+" active EventSystems="+UnityEngine.Object.FindObjectsOfType<EventSystem>().Length);
        foreach(var p in panels)Inspect(p,report,false);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);report.AppendLine("PREFAB MapCanvas layer="+prefab.transform.Find("MapCanvas").gameObject.layer);
        if(fix)
        {
            var contents=PrefabUtility.LoadPrefabContents(PrefabPath);
            try{FixLayers(contents);PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath);}finally{PrefabUtility.UnloadPrefabContents(contents);}
            foreach(var p in panels)FixLayers(p.gameObject);
            Physics.SyncTransforms();Canvas.ForceUpdateCanvases();report.AppendLine("AFTER FIX:");
            foreach(var p in panels)Inspect(p,report,true);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"TEST.with-map-input-fix.unity",true);
            report.AppendLine("Saved prefab layer fix and recovery copy; original TEST left unsaved. VRChat client rebuild/retest still required.");
        }
        File.WriteAllText(Output+(fix?"map-input-fix.done":"map-input-inspect.txt"),report.ToString());
    }
    static void Inspect(SpeakerMapPanel panel,StringBuilder report,bool enforce)
    {
        var canvas=panel.transform.Find("MapCanvas").GetComponent<Canvas>();var shape=canvas.GetComponent<VRCUiShape>();var box=canvas.GetComponent<BoxCollider>();var ray=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        int mask=~(1<<5)&~(1<<12)&~(1<<10)&~(1<<18);
        var descriptor=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();if(descriptor)mask&=~(descriptor.interactThruLayers<<22);
        report.AppendLine(panel.name+" canvasLayer="+canvas.gameObject.layer+"/"+LayerMask.LayerToName(canvas.gameObject.layer)+" normalVrchatInput="+((mask&(1<<canvas.gameObject.layer))!=0)+" shape="+(shape!=null)+" raycaster="+(ray&&ray.enabled)+" mode="+canvas.renderMode+" settings="+(panel.settings!=null)+" traffic="+(panel.settings&&panel.settings.traffic));
        report.AppendLine(" collider="+(box?box.size.ToString():"missing")+" center="+(box?box.center.ToString():"")+" trigger="+(box&&box.isTrigger)+" queriesHitTriggers="+Physics.queriesHitTriggers);
        var oldCamera=canvas.worldCamera;var cameraObject=new GameObject("Temporary map input check"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();var rt=new RenderTexture(1200,800,24);camera.targetTexture=rt;canvas.worldCamera=camera;
        try
        {
            foreach(var button in panel.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                var serialized=new SerializedObject(button);var events=serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                string expected=button.name=="Vehicles"?"_ToggleVehicles":"_ToggleAmbient";bool wired=false;
                for(int i=0;i<events.arraySize;i++)
                {
                    var e=events.GetArrayElementAtIndex(i);var target=e.FindPropertyRelative("m_Target").objectReferenceValue;
                    string method=e.FindPropertyRelative("m_MethodName").stringValue;string argument=e.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    int state=e.FindPropertyRelative("m_CallState").enumValueIndex;
                    report.AppendLine(" EVENT "+button.name+" target="+target+" method="+method+" argument="+argument+" state="+state);
                    wired|=target==UdonSharpEditorUtility.GetBackingUdonBehaviour(panel)&&method=="SendCustomEvent"&&argument==expected&&state!=0;
                }
                Vector3 targetPosition=button.transform.Find("Switch").position;
                foreach(float offset in new[]{0f,-.45f,.45f})
                {
                    camera.transform.position=targetPosition-canvas.transform.forward*1.5f+canvas.transform.right*offset+canvas.transform.up*.3f;camera.transform.LookAt(targetPosition);camera.Render();Canvas.ForceUpdateCanvases();
                    var pointer=new PointerEventData(EventSystem.current){position=camera.WorldToScreenPoint(targetPosition)};var hits=new System.Collections.Generic.List<RaycastResult>();if(ray)ray.Raycast(pointer,hits);
                    bool uiHit=hits.Any(h=>h.gameObject==button.gameObject||h.gameObject.transform.IsChildOf(button.transform));
                    var physical=Physics.RaycastAll(camera.transform.position,(targetPosition-camera.transform.position).normalized,Vector3.Distance(targetPosition,camera.transform.position)+.05f,mask,QueryTriggerInteraction.Collide).OrderBy(h=>h.distance).ToArray();
                    Collider first=null;foreach(var h in physical){if(h.collider.GetComponent<VRCUiShape>()||!h.collider.isTrigger){first=h.collider;break;}}
                    bool shapeFirst=first==box;bool inside=box&&Vector3.Distance(box.ClosestPoint(targetPosition),targetPosition)<.01f;
                    report.AppendLine(" TEST "+button.name+" offset="+offset+" interactable="+button.IsInteractable()+" UIHit="+uiHit+" shapeFirst="+shapeFirst+" insideShape="+inside+" first="+(first?first.name+" layer="+first.gameObject.layer:"none"));
                    if(enforce&&(!wired||!button.IsInteractable()||!uiHit||!shapeFirst||!inside))throw new Exception("Map input verification failed: "+panel.name+"/"+button.name+" see input report");
                }
            }
        }
        finally{canvas.worldCamera=oldCamera;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(rt);}
    }
}
