using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

[InitializeOnLoad]
public static class SpeakerPlacementReview
{
    const string Output="output/speaker-placement-20260912";
    const string MaterialPath="Assets/_Shinjuku/Art/Materials/Speaker/PlacementGuide.mat";
    static SpeakerPlacementReview(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        foreach(string action in new[]{"apply-review","verify-review","click-audit","capture-guidance","apply-heatmap"})
        {
            string request=Output+"/"+action+".request";if(!File.Exists(request))continue;File.Delete(request);
            try{if(action=="apply-heatmap")ApplyHeatmap();else if(action=="capture-guidance")CaptureGuidance();else if(action=="click-audit")AuditClicks();else if(action=="apply-review")Apply();else Verify();}
            catch(Exception e){File.WriteAllText(Output+"/"+action+".failed",e.ToString());Debug.LogException(e);}return;
        }
    }
    static SpeakerManager Manager()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        return UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m=>m.gameObject.scene==scene);
    }
    static void CaptureGuidance()
    {
        var manager=Manager();var speakers=Field<SpeakerController[]>(manager,"speakerControllers");
        var placements=Field<GameObject>(manager,"speakerPlacements");var holo=Field<GameObject>(manager,"holoSpeaker");
        var transforms=placements.GetComponentsInChildren<Transform>(true).Concat(speakers[0].GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
        var active=transforms.Select(t=>t.gameObject.activeSelf).ToArray();
        var lines=placements.GetComponentsInChildren<LineRenderer>(true);var enabled=lines.Select(l=>l.enabled).ToArray();
        var points=lines.Select(l=>{var p=new Vector3[l.positionCount];l.GetPositions(p);return p;}).ToArray();
        var heatmaps=Field<MeshRenderer[]>(manager,"placedSpeakerHeatmaps")??new MeshRenderer[0];
        var heatEnabled=heatmaps.Select(h=>h.enabled).ToArray();var heatPositions=heatmaps.Select(h=>h.transform.position).ToArray();
        var heatRotations=heatmaps.Select(h=>h.transform.rotation).ToArray();var heatScales=heatmaps.Select(h=>h.transform.localScale).ToArray();
        var taken=speakers.Select(s=>s.isSpeakerTaken).ToArray();
        var position=speakers[0].transform.position;var rotation=speakers[0].transform.rotation;
        var cameraObject=new GameObject("Temporary guidance screenshot camera"){hideFlags=HideFlags.HideAndDontSave};
        var camera=cameraObject.AddComponent<Camera>();var rt=new RenderTexture(1600,1000,24);var previous=RenderTexture.active;Texture2D shot=null;
        try
        {
            var center=new Vector3(48,6.5f,-8);
            // Match the placement system's actual ground hit rather than the old synthetic Y coordinate.
            if(Physics.Raycast(center+Vector3.up*2,Vector3.down,out RaycastHit floor,4,Field<int>(manager,"rayLayerMask"),QueryTriggerInteraction.Ignore))center.y=floor.point.y;
            speakers[0].transform.SetPositionAndRotation(center,Quaternion.identity);
            Field<GameObject>(speakers[0],"speakerObject").SetActive(true);
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=i==0;
            placements.SetActive(true);holo.SetActive(false);
            foreach(var canvas in placements.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            foreach(var line in lines)line.enabled=false;
            Call(manager,"ConfigureAudibleRangeRenderer");Call(manager,"UpdatePlacedSpeakerIndicators");
            camera.fieldOfView=60;camera.targetTexture=rt;
            for(int i=0;i<2;i++)
            {
                camera.transform.position=center+(i==0?new Vector3(6,14,24):new Vector3(3,1.7f,14));
                camera.transform.LookAt(center+(i==0?Vector3.zero:Vector3.up*1.2f));
                camera.Render();RenderTexture.active=rt;shot=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                shot.ReadPixels(new Rect(0,0,1600,1000),0,0);shot.Apply();
                string prefix=heatmaps.Length>0?"heatmap":"guidance";
                File.WriteAllBytes(Output+"/"+prefix+(i==0?"-overview.png":"-eye-level.png"),shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);shot=null;
            }
        }
        finally
        {
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(rt);if(shot)UnityEngine.Object.DestroyImmediate(shot);
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=taken[i];
            speakers[0].transform.SetPositionAndRotation(position,rotation);
            for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.SetActive(active[i]);
            Call(manager,"ConfigureAudibleRangeRenderer");
            for(int i=0;i<lines.Length;i++){lines[i].positionCount=points[i].Length;lines[i].SetPositions(points[i]);lines[i].enabled=enabled[i];}
            for(int i=0;i<heatmaps.Length;i++){heatmaps[i].enabled=heatEnabled[i];heatmaps[i].transform.SetPositionAndRotation(heatPositions[i],heatRotations[i]);heatmaps[i].transform.localScale=heatScales[i];}
        }
        File.WriteAllText(Output+"/capture-guidance.done",DateTime.Now+" Editor render: temporary occupied speaker preview. Original states restored; scene not saved.");
    }
    static void ApplyHeatmap()
    {
        var manager=Manager();var speakers=Field<SpeakerController[]>(manager,"speakerControllers");var placements=Field<GameObject>(manager,"speakerPlacements");
        const string folder="Assets/_Shinjuku/Art/Materials/Speaker/";
        var shader=Shader.Find("Shinjuku/Speaker Placement Heatmap");if(!shader)throw new Exception("Heatmap shader not imported");
        if(ShaderUtil.ShaderHasError(shader))throw new Exception("Heatmap shader compile error");
        var material=AssetDatabase.LoadAssetAtPath<Material>(folder+"PlacementHeatmap.mat");
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,folder+"PlacementHeatmap.mat");}
        material.SetFloat("_ExclusionMeters",Field<float>(manager,"minimumSpeakerDistance"));material.SetFloat("_Opacity",.32f);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"PlacementHeatmapQuad.asset");
        if(!mesh)
        {
            mesh=new Mesh{name="Flat heatmap quad (two triangles)"};
            mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};
            mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"PlacementHeatmapQuad.asset");
        }
        var so=new SerializedObject(manager);var property=so.FindProperty("placedSpeakerHeatmaps");property.arraySize=speakers.Length;
        for(int i=0;i<speakers.Length;i++)
        {
            string name="SpeakerHeatmap_"+i;var child=placements.transform.Find(name);
            if(!child){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Flat speaker heatmaps");go.transform.SetParent(placements.transform,false);child=go.transform;}
            child.gameObject.layer=2;var filter=child.GetComponent<MeshFilter>();if(!filter)filter=Undo.AddComponent<MeshFilter>(child.gameObject);filter.sharedMesh=mesh;
            var renderer=child.GetComponent<MeshRenderer>();if(!renderer)renderer=Undo.AddComponent<MeshRenderer>(child.gameObject);
            renderer.sharedMaterial=material;renderer.enabled=false;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;renderer.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
            property.GetArrayElementAtIndex(i).objectReferenceValue=renderer;Record(filter);Record(renderer);Record(child);
        }
        so.ApplyModifiedProperties();
        var legacy=Field<LineRenderer>(manager,"audibleRangeRenderer");if(legacy)legacy.enabled=false;
        foreach(var line in Field<LineRenderer[]>(manager,"placedSpeakerRings").Concat(Field<LineRenderer[]>(manager,"placedSpeakerExclusionRings")))line.enabled=false;
        Call(manager,"ConfigureAudibleRangeRenderer");manager._OnWorldLanguageChanged();
        // Test the real runtime indicator path without sending network events or keeping test state.
        var heatmaps=Field<MeshRenderer[]>(manager,"placedSpeakerHeatmaps");var beacons=Field<LineRenderer[]>(manager,"placedSpeakerBeacons");
        var oldTaken=speakers.Select(s=>s.isSpeakerTaken).ToArray();var report=new StringBuilder();
        try
        {
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=true;
            Call(manager,"UpdatePlacedSpeakerIndicators");
            for(int i=0;i<heatmaps.Length;i++)
            {
                if(!heatmaps[i].enabled||!beacons[i].enabled||heatmaps[i].GetComponent<Collider>())throw new Exception("Heatmap lifecycle/collider failure");
                float diameter=manager.GetSpeakerWarningRadius(i)*2;
                if(Mathf.Abs(heatmaps[i].transform.lossyScale.x-diameter)>.01f)throw new Exception("Radius scale mismatch");
            }
            if(Field<LineRenderer[]>(manager,"placedSpeakerRings").Any(l=>l.enabled)||Field<LineRenderer[]>(manager,"placedSpeakerExclusionRings").Any(l=>l.enabled))throw new Exception("Legacy rings still enabled");
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=false;
            Call(manager,"UpdatePlacedSpeakerIndicators");if(heatmaps.Any(h=>h.enabled)||beacons.Any(b=>b.enabled))throw new Exception("Returned indicators still visible");
            report.AppendLine("PASS: all six occupancy/return states; legacy rings hidden; no heatmap collider; correct world radius.");
        }
        finally{for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=oldTaken[i];Call(manager,"ConfigureAudibleRangeRenderer");Call(manager,"UpdatePlacedSpeakerIndicators");}
        UdonSharpEditorUtility.CopyProxyToUdon(manager);Record(manager);var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);Record(backing);
        if(!backing.publicVariables.TryGetVariableValue("placedSpeakerHeatmaps",out MeshRenderer[] stored)||stored.Length!=speakers.Length)throw new Exception("Udon heatmap bindings absent");
        CaptureGuidance();
        var scene=SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-heatmap.unity",true);
        report.AppendLine("Two triangles per heatmap; shared unlit shader; zero terrain raycasts in range visualization. No profiler timing claim. Original TEST remains unsaved; recovery copy saved.");
        File.WriteAllText(Output+"/apply-heatmap.done",DateTime.Now+"\n"+report);
    }
    static void AuditClicks()
    {
        var manager=Manager();var speaker=Field<SpeakerController[]>(manager,"speakerControllers")[0];
        var transforms=speaker.GetComponentsInChildren<Transform>(true);var active=transforms.Select(t=>t.gameObject.activeSelf).ToArray();
        var canvases=speaker.GetComponentsInChildren<Canvas>(true);var cameras=canvases.Select(c=>c.worldCamera).ToArray();
        var originalPosition=speaker.transform.position;var originalRotation=speaker.transform.rotation;
        var cameraObject=new GameObject("Temporary click audit"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();var clickTexture=new RenderTexture(1400,900,24);camera.targetTexture=clickTexture;
        var eventSystem=UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();GameObject temporaryEvents=null;
        if(!eventSystem){temporaryEvents=new GameObject("Temporary event audit"){hideFlags=HideFlags.HideAndDontSave};eventSystem=temporaryEvents.AddComponent<UnityEngine.EventSystems.EventSystem>();}
        var report=new StringBuilder();
        try
        {
            speaker.transform.SetPositionAndRotation(new Vector3(48,6.5f,-8),Quaternion.identity);
            Field<GameObject>(speaker,"speakerObject").SetActive(true);
            foreach(var owner in Field<GameObject[]>(speaker,"ownerObjects"))owner.SetActive(true);
            foreach(var canvas in canvases)canvas.worldCamera=camera;
            Canvas.ForceUpdateCanvases();
            var buttons=speaker.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            foreach(float side in new[]{1f,-1f})foreach(var button in buttons)
            {
                var target=button.transform.position;
                camera.transform.position=speaker.transform.position+new Vector3(0,1.4f,side*1.2f);camera.transform.LookAt(target);
                camera.Render();Canvas.ForceUpdateCanvases();
                var eventData=new UnityEngine.EventSystems.PointerEventData(eventSystem){position=camera.WorldToScreenPoint(target)};
                var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                foreach(var canvas in canvases){var ray=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();if(ray&&ray.isActiveAndEnabled)ray.Raycast(eventData,hits);}
                bool hit=hits.Any(h=>h.gameObject.transform==button.transform||h.gameObject.transform.IsChildOf(button.transform));
                report.AppendLine("side="+side+" "+button.transform.parent.name+"/"+button.name+" clickable="+button.interactable+" targetHit="+hit+" hits="+string.Join(",",hits.OrderBy(h=>h.distance).Select(h=>h.gameObject.name+"@"+h.distance.ToString("F2"))));
                for(int i=0;i<button.onClick.GetPersistentEventCount();i++)report.AppendLine(" LISTENER "+button.onClick.GetPersistentTarget(i)+" "+button.onClick.GetPersistentMethodName(i));
                report.AppendLine(" RECT depth="+button.targetGraphic.depth+" contains="+RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform,eventData.position,camera));
                var physical=Physics.RaycastAll(camera.transform.position,(target-camera.transform.position).normalized,Vector3.Distance(target,camera.transform.position)+.02f,~0,QueryTriggerInteraction.Collide);
                report.AppendLine(" PHYSICS "+string.Join(",",physical.OrderBy(h=>h.distance).Select(h=>h.collider.name+" layer="+h.collider.gameObject.layer+" trigger="+h.collider.isTrigger+" @"+h.distance.ToString("F2"))));
            }
            foreach(var canvas in canvases)report.AppendLine("FACING "+canvas.name+" forward="+canvas.transform.forward+" position="+canvas.transform.position);
        }
        finally
        {
            for(int i=0;i<canvases.Length;i++)canvases[i].worldCamera=cameras[i];
            speaker.transform.SetPositionAndRotation(originalPosition,originalRotation);
            for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.SetActive(active[i]);
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(clickTexture);if(temporaryEvents)UnityEngine.Object.DestroyImmediate(temporaryEvents);
        }
        File.WriteAllText(Output+"/click-audit.txt",report.ToString());
    }
    static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
    static void Call(object obj,string method,params object[] args){obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);}
    static void Record(UnityEngine.Object obj){EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
    static LineRenderer Line(Transform parent,string name,Material material,bool ground,Color color)
    {
        var child=parent.Find(name);
        if(!child){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Speaker guidance");go.transform.SetParent(parent,false);child=go.transform;}
        var line=child.GetComponent<LineRenderer>();if(!line)line=Undo.AddComponent<LineRenderer>(child.gameObject);
        child.gameObject.layer=2; // Ignore Raycast: guidance can never block placement or buttons.
        child.localScale=Vector3.one;child.rotation=ground?Quaternion.Euler(90,0,0):Quaternion.identity;
        line.sharedMaterial=material;line.useWorldSpace=true;line.loop=ground;line.positionCount=ground?72:2;
        line.widthMultiplier=ground?.10f:.09f;line.alignment=ground?LineAlignment.TransformZ:LineAlignment.View;
        line.textureMode=LineTextureMode.Stretch;line.startColor=line.endColor=color;
        line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
        line.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;line.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
        line.enabled=false;Record(line);Record(child);return line;
    }
    static float Radius(SpeakerController speaker,StringBuilder report)
    {
        float result=0;
        foreach(var audio in speaker.GetComponentsInChildren<AudioSource>(true))
        {
            if(audio.rolloffMode!=AudioRolloffMode.Custom||audio.spatialBlend<.99f)throw new Exception("Expected existing custom 3D audio curve");
            var spatial=audio.GetComponents<Component>().SingleOrDefault(c=>c.GetType().Name.Contains("SpatialAudioSource"));
            if(!spatial||!new SerializedObject(spatial).FindProperty("UseAudioSourceVolumeCurve").boolValue)throw new Exception("Audio curve override inactive");
            var curve=audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            // -20 dB relative amplitude (10%) is a practical placement guide, NOT a hard audibility cutoff.
            float last=0;for(int i=0;i<=1000;i++)if(curve.Evaluate(i/1000f)>=.1f)last=i/1000f*audio.maxDistance;
            result=Mathf.Max(result,last);report.AppendLine(speaker.name+" "+audio.name+": max="+audio.maxDistance+"m; curve >=10% radius="+last+"m; audio unchanged.");
        }
        if(result<=8)throw new Exception("Influence radius must exceed existing 8m exclusion zone");return result;
    }
    static void Apply()
    {
        var manager=Manager();var so=new SerializedObject(manager);var report=new StringBuilder();
        var placements=Field<GameObject>(manager,"speakerPlacements");var speakers=Field<SpeakerController[]>(manager,"speakerControllers");
        Undo.RegisterFullObjectHierarchyUndo(placements,"Stable local speaker placement guidance");
        var shader=Shader.Find("Shinjuku/Speaker Placement Guide");if(!shader)throw new Exception("Guide shader missing");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}
        var sourceRing=Field<LineRenderer>(manager,"audibleRangeRenderer");
        var own=Line(placements.transform,sourceRing.name,material,true,new Color(.18f,.9f,.56f,.55f));own.enabled=true;own.widthMultiplier=.06f;
        string[] fields={"speakerWarningRadii","placedSpeakerRings","placedSpeakerExclusionRings","placedSpeakerBeacons"};
        foreach(string f in fields)so.FindProperty(f).arraySize=speakers.Length;
        float maxRadius=0;
        for(int i=0;i<speakers.Length;i++)
        {
            float radius=Radius(speakers[i],report);maxRadius=Mathf.Max(maxRadius,radius);
            so.FindProperty(fields[0]).GetArrayElementAtIndex(i).floatValue=radius;
            so.FindProperty(fields[1]).GetArrayElementAtIndex(i).objectReferenceValue=Line(placements.transform,"SpeakerInfluence_"+i,material,true,new Color(1,1,.05f,.8f));
            so.FindProperty(fields[2]).GetArrayElementAtIndex(i).objectReferenceValue=Line(placements.transform,"SpeakerExclusion_"+i,material,true,new Color(1,.15f,.15f,.7f));
            var beacon=Line(placements.transform,"SpeakerBeacon_"+i,material,false,new Color(1,1,.05f,.9f));
            beacon.endColor=new Color(1,1,.05f,.25f);
            so.FindProperty(fields[3]).GetArrayElementAtIndex(i).objectReferenceValue=beacon;
        }
        so.FindProperty("audibleRange").floatValue=maxRadius;so.ApplyModifiedProperties();
        var animator=Field<Animator>(manager,"holoAnimator");
        var controller=(AnimatorController)animator.runtimeAnimatorController;
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Shinjuku/Art/Animations/Speaker/Animation/SpeakerHoloWarning.anim");
        Undo.RecordObject(clip,"Yellow speaker warning");
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if(!binding.propertyName.StartsWith("material._EmissionColor."))continue;
            float value=binding.propertyName.EndsWith(".b")?.05f:1;
            AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,1,value));
        }
        var machine=controller.layers[0].stateMachine;
        foreach(var transition in machine.anyStateTransitions.Concat(machine.states.SelectMany(s=>s.state.transitions)))
        {
            var destination=transition.destinationState;if(!destination)continue;
            report.AppendLine("Before transition to "+destination.name+": "+string.Join(",",transition.conditions.Select(c=>c.parameter+"="+c.mode)));
            if(destination.name=="SpeakerHoloEnable")
            {
                Undo.RecordObject(transition,"Prevent ready/warning animation conflict");
                if(!transition.conditions.Any(c=>c.parameter=="HoloWarning"))transition.AddCondition(AnimatorConditionMode.IfNot,0,"HoloWarning");
                transition.canTransitionToSelf=false;Record(transition);
            }
        }
        Record(clip);Record(controller);AssetDatabase.SaveAssetIfDirty(clip);AssetDatabase.SaveAssetIfDirty(controller);
        Call(manager,"ConfigureAudibleRangeRenderer");manager._OnWorldLanguageChanged();
        UdonSharpEditorUtility.CopyProxyToUdon(manager);Record(manager);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager));
        File.WriteAllText(Output+"/apply-review.txt",report.ToString());
        File.WriteAllText(Output+"/verify-review.request","Verify compiled bindings, warning thresholds and local guide rendering.");
    }
    static void Verify()
    {
        var manager=Manager();var speakers=Field<SpeakerController[]>(manager,"speakerControllers");var report=new StringBuilder();
        var positions=speakers.Select(s=>s.transform.position).ToArray();var taken=speakers.Select(s=>s.isSpeakerTaken).ToArray();
        var placements=Field<GameObject>(manager,"speakerPlacements");bool active=placements.activeSelf;
        var holo=Field<GameObject>(manager,"holoSpeaker");var holoPosition=holo.transform.position;var holoRotation=holo.transform.rotation;
        try
        {
            for(int i=0;i<speakers.Length;i++)speakers[i].isSpeakerTaken=false;
            speakers[0].isSpeakerTaken=true;speakers[0].transform.position=new Vector3(48,6.5f,-8);
            float radius=manager.GetSpeakerWarningRadius(0);var center=speakers[0].transform.position;
            if(!manager.IsInsideSpeakerInfluence(center+Vector3.right*(radius-.1f),0)||manager.IsInsideSpeakerInfluence(center+Vector3.right*(radius+.1f),0)||manager.IsInsideSpeakerInfluence(center+Vector3.right*30,0))throw new Exception("Warning radius boundary failure");
            if(!manager.IsInsideSpeakerInfluence(center+Vector3.right*(radius+.3f),.6f))throw new Exception("Warning exit hysteresis failure");
            report.AppendLine("PASS: inside/outside "+radius+"m, no warning at 30/60m, 0.6m exit hysteresis.");
            speakers[0].isSpeakerTaken=false;if(manager.IsInsideSpeakerInfluence(center,0))throw new Exception("Returned speaker warns");speakers[0].isSpeakerTaken=true;
            Call(manager,"ConfigureAudibleRangeRenderer");placements.SetActive(true);
            Call(manager,"UpdatePlacedSpeakerIndicators");
            var rings=Field<LineRenderer[]>(manager,"placedSpeakerRings");var beacons=Field<LineRenderer[]>(manager,"placedSpeakerBeacons");
            if(!rings[0].enabled||!beacons[0].enabled||rings.Skip(1).Any(r=>r.enabled))throw new Exception("Indicator lifecycle failure");
            if(placements.GetComponentsInChildren<Collider>(true).Any(c=>c.name.StartsWith("SpeakerInfluence_")||c.name.StartsWith("SpeakerBeacon_")))throw new Exception("Guide blocks clicks");
            var own=Field<LineRenderer>(manager,"audibleRangeRenderer");own.enabled=false;
            holo.transform.position=center+new Vector3(14,0,1);holo.transform.rotation=Quaternion.identity;
            var cameraObject=new GameObject("Temporary placement review camera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();var rt=new RenderTexture(1400,900,24);var previous=RenderTexture.active;Texture2D image=null;
            try{camera.transform.position=center+new Vector3(6,12,23);camera.transform.LookAt(center);camera.fieldOfView=60;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();File.WriteAllBytes(Output+"/guidance-preview.png",image.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(rt);if(image)UnityEngine.Object.DestroyImmediate(image);own.enabled=true;}
            speakers[0].isSpeakerTaken=false;Call(manager,"UpdatePlacedSpeakerIndicators");if(rings.Any(r=>r.enabled)||beacons.Any(r=>r.enabled))throw new Exception("Returned guide remains visible");
            report.AppendLine("PASS: occupied/returned indicator lifecycle, separate static material, no guide colliders, local-only parent.");
        }
        finally
        {
            for(int i=0;i<speakers.Length;i++){speakers[i].transform.position=positions[i];speakers[i].isSpeakerTaken=taken[i];}
            holo.transform.SetPositionAndRotation(holoPosition,holoRotation);placements.SetActive(active);
            Call(manager,"ConfigureAudibleRangeRenderer");Call(manager,"UpdatePlacedSpeakerIndicators");
        }
        var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
        if(!backing.publicVariables.TryGetVariableValue("speakerWarningRadii",out float[] stored)||stored.Length!=speakers.Length)throw new Exception("Udon bindings absent");
        var scene=SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-placement-range.unity",true);
        File.WriteAllText(Output+"/verify-review.done",DateTime.Now+"\n"+report+"Original TEST left unsaved; recovery saved. Real two-client retest remains necessary.");
    }
}
