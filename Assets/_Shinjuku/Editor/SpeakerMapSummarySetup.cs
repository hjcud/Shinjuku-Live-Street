using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;

[InitializeOnLoad]
public static class SpeakerMapSummarySetup
{
    private const string Output="output/speaker-map-ui-20260912";
    static SpeakerMapSummarySetup(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(File.Exists(Output+"/map-top-crop.request"))
        {
            File.Delete(Output+"/map-top-crop.request");
            try{ApplyTopCrop();}catch(Exception e){File.WriteAllText(Output+"/map-top-crop.failed",e.ToString());Debug.LogException(e);}
            return;
        }
        if(File.Exists(Output+"/summary-apply.request"))
        {
            File.Delete(Output+"/summary-apply.request");
            try{Apply();}catch(Exception e){File.WriteAllText(Output+"/summary-apply.failed",e.ToString());Debug.LogException(e);}
            return;
        }
        if(!File.Exists(Output+"/summary-inspect.request"))return;
        File.Delete(Output+"/summary-inspect.request");
        var scene=SceneManager.GetActiveScene();
        var report=new StringBuilder(scene.path+"\n");
        report.AppendLine("SELECTED "+(Selection.activeTransform?Path(Selection.activeTransform):"none"));
        var speakers=UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Where(s=>s.gameObject.scene==scene).ToArray();
        report.AppendLine("CONTROLLERS "+speakers.Length);
        foreach(var s in speakers)
        {
            report.AppendLine("SPEAKER "+Path(s.transform)+" active="+s.gameObject.activeInHierarchy+" "+EditorJsonUtility.ToJson(s));
            if(s!=speakers.Last())continue;
            foreach(var c in s.GetComponentsInChildren<Component>(true))
            {
                if(!c)continue;
                var so=new SerializedObject(c); var p=so.GetIterator();
                while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue)
                {
                    var value=p.objectReferenceValue;
                    report.AppendLine("REF "+Path(c.transform)+" "+c.GetType().Name+"."+p.propertyPath+" => "+value.name+" ("+value.GetType().Name+") "+AssetDatabase.GetAssetPath(value));
                }
            }
        }
        foreach(var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene))
        {
            report.AppendLine("MAP "+map.name+" "+EditorJsonUtility.ToJson(map));
            foreach(var p in new[]{"MapCanvas","MapCanvas/Board","MapCanvas/MetalFrame","MapCanvas/LocalSettings","MapCanvas/SpeakerUsage","MapCanvas/FloorPlan","MapCanvas/FloorPlan/SpeakerMarkers"})
            {
                var r=(RectTransform)map.transform.Find(p);report.AppendLine("RECT "+p+" pos="+r.anchoredPosition+" size="+r.sizeDelta+" anchors="+r.anchorMin+"/"+r.anchorMax+" pivot="+r.pivot+" world="+r.position);
                var canvas=map.transform.Find("MapCanvas");var corners=new Vector3[4];r.GetWorldCorners(corners);
                report.AppendLine("CORNERS "+p+" "+string.Join(" | ",corners.Select(c=>canvas.InverseTransformPoint(c).ToString("F2"))));
            }
            foreach(var c in map.GetComponentsInChildren<BoxCollider>(true))report.AppendLine("BOX "+Path(c.transform)+" center="+c.center+" size="+c.size);
            var floor=map.transform.Find("MapCanvas/FloorPlan");
            foreach(RectTransform child in floor)report.AppendLine("CHILD "+child.name+" active="+child.gameObject.activeSelf+" pos="+child.anchoredPosition+" size="+child.sizeDelta+" anchor="+child.anchorMin+"/"+child.anchorMax);
        }
        File.WriteAllText(Output+"/summary-inspect.txt",report.ToString());
    }
    private static string Path(Transform t){return t.parent?Path(t.parent)+"/"+t.name:t.name;}

    private static void Record(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);
        if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }
    private static void Sync(UdonSharpBehaviour b){UdonSharpEditorUtility.CopyProxyToUdon(b);Record(b);Record(UdonSharpEditorUtility.GetBackingUdonBehaviour(b));}
    private static void SetSources(UdonSharpBehaviour b,SpeakerController[] sources)
    {
        var so=new SerializedObject(b);var a=so.FindProperty("speakerControllers");a.arraySize=sources.Length;
        for(int i=0;i<sources.Length;i++)a.GetArrayElementAtIndex(i).objectReferenceValue=sources[i];
        so.ApplyModifiedPropertiesWithoutUndo();Sync(b);
    }
    private static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
    {
        var r=parent.Find(name) as RectTransform;
        if(!r)r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;
        r.anchoredPosition=pos;r.sizeDelta=size;r.localScale=Vector3.one;r.localRotation=Quaternion.identity;
        r.gameObject.layer=parent.gameObject.layer;Record(r);return r;
    }
    private static Sprite DotSprite()
    {
        const string path="Assets/_Shinjuku/UI/SpeakerMap/UsageDot.png";
        if(!File.Exists(path))
        {
            var t=new Texture2D(64,64,TextureFormat.RGBA32,false);var pixels=new Color[64*64];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(30.5f-Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32))));
            t.SetPixels(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.textureType=TextureImporterType.Sprite;imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static void Layout(SpeakerMap map,Sprite dot)
    {
        var compactPanel=map.GetComponent<SpeakerMapPanel>();
        if(compactPanel&&compactPanel.settingsTitle){SpeakerMapCompactSettings.RefreshLayout(compactPanel);map._RefreshMap();return;}
        var canvas=(RectTransform)map.transform.Find("MapCanvas");var floor=(RectTransform)canvas.Find("FloorPlan");
        var raw=floor.GetComponent<RawImage>();
        if(Mathf.Approximately(floor.sizeDelta.x,1800))
        {
            // Preserve original 1800px marker calibration. Crop only the texture viewport.
            var children=canvas.Cast<RectTransform>().ToArray();var positions=children.Select(t=>t.position).ToArray();
            canvas.position+=canvas.TransformVector(new Vector3(95,0,0));
            for(int i=0;i<children.Length;i++)children[i].position=positions[i];
            floor.anchoredPosition=Vector2.zero;floor.sizeDelta=new Vector2(1610,floor.sizeDelta.y);
            foreach(RectTransform c in floor){c.anchoredPosition-=new Vector2(95,0);Record(c);}
        }
        if(floor.sizeDelta.x!=1610||(floor.sizeDelta.y!=1200&&floor.sizeDelta.y!=1110))throw new Exception("Unexpected floor size");
        float boardHeight=floor.sizeDelta.y+40;
        float topOffset=(1200-floor.sizeDelta.y)*.5f;
        raw.uvRect=new UnityEngine.Rect(190f/1800,0,1610f/1800,floor.sizeDelta.y/1200);raw.raycastTarget=false;
        if(!floor.GetComponent<RectMask2D>())floor.gameObject.AddComponent<RectMask2D>();
        canvas.sizeDelta=new Vector2(1650,boardHeight);
        var board=(RectTransform)canvas.Find("Board");var frame=(RectTransform)canvas.Find("MetalFrame");
        board.anchoredPosition=frame.anchoredPosition=Vector2.zero;
        board.sizeDelta=new Vector2(1650,boardHeight);frame.sizeDelta=new Vector2(1690,boardHeight+40);
        canvas.GetComponent<BoxCollider>().size=new Vector3(1650,boardHeight,2);
        var footer=(RectTransform)canvas.Find("LocalSettings");footer.anchoredPosition=new Vector2(95,-480+topOffset);
        var music=(RectTransform)floor.Find("MusicShopLabel");music.anchoredPosition=new Vector2(35,-249+topOffset);
        var icon=(RectTransform)floor.Find("MusicShopIcon");icon.anchoredPosition=music.anchoredPosition+new Vector2(-100,15);
        var group=Rect(canvas,"SpeakerUsage",new Vector2(-286,-485+topOffset),new Vector2(250,90));
        var labelRect=Rect(group,"Summary",new Vector2(26,0),new Vector2(200,80));
        var label=labelRect.GetComponent<TextMeshProUGUI>();if(!label)label=labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        var model=music.GetComponent<TextMeshProUGUI>();
        const string required="0123456789 / 使用中Speakers in use";
        if(!model.font.HasCharacters(required))
        {
            model.font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            try{if(!model.font.TryAddCharacters(required,out string missing))throw new Exception("Summary glyphs missing: "+missing);}
            finally{model.font.atlasPopulationMode=AtlasPopulationMode.Static;EditorUtility.SetDirty(model.font);}
        }
        label.font=model.font;label.fontSharedMaterial=model.fontSharedMaterial;
        label.fontSize=28;label.enableAutoSizing=false;label.alignment=TextAlignmentOptions.MidlineLeft;
        label.color=new Color(.40f,.47f,.50f);label.richText=true;label.enableWordWrapping=false;label.raycastTarget=false;
        var dotRect=Rect(group,"StatusDot",new Vector2(-105,15),new Vector2(18,18));
        var indicator=dotRect.GetComponent<Image>();if(!indicator)indicator=dotRect.gameObject.AddComponent<Image>();
        indicator.sprite=dot;indicator.raycastTarget=false;
        var so=new SerializedObject(map);so.FindProperty("usageSummary").objectReferenceValue=label;so.FindProperty("usageDot").objectReferenceValue=indicator;so.ApplyModifiedPropertiesWithoutUndo();
        // A prefab update can introduce inherited copies beside scene-added UI. Keep the wired group only.
        foreach(Transform child in canvas)if(child.name=="SpeakerUsage"){child.gameObject.SetActive(child==group);Record(child.gameObject);}
        map._RefreshMap();
        foreach(var o in new UnityEngine.Object[]{canvas,floor,raw,board,frame,canvas.GetComponent<BoxCollider>(),footer,music,icon,label,indicator})Record(o);
        Sync(map);
    }
    private static void CropTop(SpeakerMap map)
    {
        var canvas=(RectTransform)map.transform.Find("MapCanvas");
        var floor=(RectTransform)canvas.Find("FloorPlan");
        if(floor.sizeDelta.x!=1610||(floor.sizeDelta.y!=1200&&floor.sizeDelta.y!=1110))throw new Exception("Unexpected floor before top crop");
        if(floor.sizeDelta.y==1200)
        {
            // Lower the board center by half the crop, preserving its bottom edge and all content in world space.
            var children=canvas.Cast<RectTransform>().ToArray();var world=children.Select(c=>c.position).ToArray();
            canvas.position-=canvas.TransformVector(new Vector3(0,45,0));
            for(int i=0;i<children.Length;i++){children[i].position=world[i];Record(children[i]);}
            floor.anchoredPosition=Vector2.zero;floor.sizeDelta=new Vector2(1610,1110);
            foreach(RectTransform child in floor){child.anchoredPosition+=new Vector2(0,45);Record(child);}
        }
        var west=(RectTransform)floor.Find("WestStairsLabel");west.anchoredPosition=new Vector2(475-995,645-162);
        var smoke=(RectTransform)floor.Find("SmokingRoomLabel");smoke.anchoredPosition=new Vector2(1428-995,645-160);
        var smokeIcon=(RectTransform)floor.Find("SmokingAreaIcon");smokeIcon.anchoredPosition=smoke.anchoredPosition+new Vector2(57,22);
        floor.GetComponent<UnityEngine.UI.RawImage>().uvRect=new UnityEngine.Rect(190f/1800,0,1610f/1800,1110f/1200);
        canvas.sizeDelta=new Vector2(1650,1150);
        var board=(RectTransform)canvas.Find("Board");var frame=(RectTransform)canvas.Find("MetalFrame");
        board.anchoredPosition=frame.anchoredPosition=Vector2.zero;board.sizeDelta=new Vector2(1650,1150);frame.sizeDelta=new Vector2(1690,1190);
        canvas.GetComponent<BoxCollider>().size=new Vector3(1650,1150,2);
        var footer=(RectTransform)canvas.Find("LocalSettings");footer.anchoredPosition=new Vector2(95,-435);Record(footer);
        foreach(var o in new UnityEngine.Object[]{canvas,floor,west,smoke,smokeIcon,board,frame,floor.GetComponent<UnityEngine.UI.RawImage>(),canvas.GetComponent<BoxCollider>()})Record(o);
    }
    private static void ApplyTopCrop()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open TEST in edit mode");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).OrderBy(m=>m.name).ToArray();
        if(maps.Length!=2)throw new Exception("Expected two map boards");
        var report=new StringBuilder();
        // Restore the footer offset reset by the previous inherited-prefab refresh before retrying.
        foreach(var map in maps)
        {
            var floor=(RectTransform)map.transform.Find("MapCanvas/FloorPlan");var footer=(RectTransform)map.transform.Find("MapCanvas/LocalSettings");
            if(floor.sizeDelta.y==1110&&Mathf.Approximately(footer.anchoredPosition.y,-480)){footer.anchoredPosition+=new Vector2(0,45);Record(footer);}
        }
        var protectedPaths=new[]{"MapCanvas/FloorPlan/SpeakerMarkers","MapCanvas/FloorPlan/Here","MapCanvas/LocalSettings","MapCanvas/SpeakerUsage","MapOrigin"};
        var world=maps.Select(m=>protectedPaths.Select(p=>m.transform.Find(p).position).ToArray()).ToArray();
        var data=maps.Select(m=>EditorJsonUtility.ToJson(m)).ToArray();
        AssetDatabase.ImportAsset("Assets/_Shinjuku/UI/SpeakerMap/FloorPlan.png",ImportAssetOptions.ForceUpdate);
        foreach(var map in maps){Undo.RegisterFullObjectHierarchyUndo(map.gameObject,"Reduce map top margin");CropTop(map);}
        const string prefabPath="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try{CropTop(prefab.GetComponent<SpeakerMap>());PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        for(int i=0;i<maps.Length;i++)
        {
            var map=maps[i];
            CropTop(map); // Explicit final overrides after prefab propagation; no additional crop when already 1110px tall.
            if(data[i]!=EditorJsonUtility.ToJson(map))throw new Exception("Runtime map references changed");
            for(int j=0;j<protectedPaths.Length;j++)if(Vector3.Distance(world[i][j],map.transform.Find(protectedPaths[j]).position)>.0001f)throw new Exception("Protected world position changed: "+protectedPaths[j]);
            var floor=(RectTransform)map.transform.Find("MapCanvas/FloorPlan");
            var canvas=(RectTransform)floor.parent;
            foreach(var label in floor.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if(label.transform.IsChildOf(floor.Find("SpeakerMarkers")))continue;
                label.ForceMeshUpdate();
                var b=label.textBounds;
                float top=floor.InverseTransformPoint(label.transform.TransformPoint(b.max)).y;
                if(top>floor.rect.yMax-8)throw new Exception("Top label clipped: "+label.name);
            }
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-top-crop-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-top-crop.png");
            report.AppendLine(map.name+": board 1650x1150; floor 1610x1110; top-only crop 90px; geometry top clearance 20px; protected world positions and runtime references unchanged; labels unclipped and no overlaps.");
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/map-top-crop.done",DateTime.Now+"\n"+report);
    }
    private static void Apply()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open TEST in edit mode");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).OrderBy(m=>m.name).ToArray();
        if(maps.Length!=2)throw new Exception("Expected two maps");
        var speakers=UnityEngine.Object.FindObjectsOfType<SpeakerController>(true).Where(s=>s.gameObject.scene==scene).OrderBy(s=>s.name).ToArray();
        var manager=new SerializedObject(speakers[0]).FindProperty("speakerManager").objectReferenceValue as SpeakerManager;
        if(!manager||speakers.Any(s=>new SerializedObject(s).FindProperty("speakerManager").objectReferenceValue!=manager))throw new Exception("Speaker manager mismatch");
        UdonSharpProgramAsset.CompileAllCsPrograms();
        if(speakers.Length==5)
        {
            var source=speakers.Last();
            var clone=UnityEngine.Object.Instantiate(source.gameObject,source.transform.parent);
            clone.name="Speaker (5)";Undo.RegisterCreatedObjectUndo(clone,"Add sixth speaker slot");
            // Speakers are a placement pool: keep the duplicated slot in its hidden, unplaced state.
            foreach(var b in clone.GetComponentsInChildren<UdonSharpBehaviour>(true))Sync(b);
            foreach(var c in clone.GetComponentsInChildren<Component>(true))
            {
                if(!c)throw new Exception("Missing script in sixth speaker");
                var so=new SerializedObject(c);var p=so.GetIterator();
                while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference)
                {
                    var value=p.objectReferenceValue;var t=value is Component component?component.transform:value is GameObject go?go.transform:null;
                    if(t&&t.IsChildOf(source.transform))throw new Exception("Sixth speaker reference still points to source: "+c.GetType().Name+"."+p.propertyPath);
                }
            }
            speakers=speakers.Concat(new[]{clone.GetComponent<SpeakerController>()}).ToArray();
        }
        if(speakers.Length!=6)throw new Exception("Expected six speaker slots");
        Undo.RecordObject(manager,"Connect sixth speaker");SetSources(manager,speakers);
        if(!UdonSharpEditorUtility.GetBackingUdonBehaviour(manager).publicVariables.TryGetVariableValue("speakerControllers",out Component[] managerRefs)||managerRefs.Length!=6||managerRefs.Distinct().Count()!=6||managerRefs.Any(r=>!r))throw new Exception("Manager six-slot binding invalid");
        var dot=DotSprite();
        var markerWorld=maps.Select(m=>m.transform.Find("MapCanvas/FloorPlan/SpeakerMarkers").position).ToArray();
        for(int i=0;i<maps.Length;i++)
        {
            Undo.RegisterFullObjectHierarchyUndo(maps[i].gameObject,"Crop map and add speaker usage");
            SetSources(maps[i],speakers);Layout(maps[i],dot);
            if(Vector3.Distance(markerWorld[i],maps[i].transform.Find("MapCanvas/FloorPlan/SpeakerMarkers").position)>.0001f)throw new Exception("Marker world calibration changed");
        }
        const string prefabPath="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try{Layout(prefab.GetComponent<SpeakerMap>(),dot);PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps){Layout(map,dot);SetSources(map,speakers);}
        var report=new StringBuilder("Six controllers, same manager; clone local references remapped; original marker world calibration preserved.\n");
        foreach(var map in maps)
        {
            var so=new SerializedObject(map);var label=(TextMeshProUGUI)so.FindProperty("usageSummary").objectReferenceValue;
            if(label.text!=map.FormatUsage(0,6))throw new Exception("Initial summary mismatch: "+label.text);
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(map);
            if(!backing.publicVariables.TryGetVariableValue("usageSummary",out TextMeshProUGUI connected)||connected!=label)throw new Exception("Summary Udon binding missing");
            if(!backing.publicVariables.TryGetVariableValue("speakerControllers",out Component[] refs)||refs.Length!=6||refs.Any(r=>!r))throw new Exception("Six backing speaker refs missing");
            var canvas=(RectTransform)map.transform.Find("MapCanvas");
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-summary-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-summary.png");
            report.AppendLine(map.name+": 1610px cropped floor, 1800px marker overlay, summary 0/6 and Udon references PASS");
        }
        TestSummary(report);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/summary-apply.done",DateTime.Now+"\n"+report);
    }
    private static void TestSummary(StringBuilder report)
    {
        // Isolated proxies only: never mutate live occupancy or trigger network messages.
        var go=new GameObject("Isolated summary check"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);
        try
        {
            var test=go.AddComponent<SpeakerMap>();var slots=new SpeakerController[6];
            for(int i=0;i<slots.Length;i++){var child=new GameObject("Mock "+i);child.transform.SetParent(go.transform);slots[i]=child.AddComponent<SpeakerController>();}
            var managerObject=new GameObject("Mock manager");managerObject.transform.SetParent(go.transform);var manager=managerObject.AddComponent<SpeakerManager>();
            var managerData=new SerializedObject(manager);var managerSlots=managerData.FindProperty("speakerControllers");managerSlots.arraySize=6;
            for(int i=0;i<6;i++)managerSlots.GetArrayElementAtIndex(i).objectReferenceValue=slots[i];managerData.ApplyModifiedPropertiesWithoutUndo();
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(SpeakerManager).GetMethod("EnsureAllocationTable",flags).Invoke(manager,null);
            if(((int[])typeof(SpeakerManager).GetField("allocatedPlayerIds",flags).GetValue(manager)).Length!=6)throw new Exception("Allocation table did not expand to six slots");
            var label=Rect(go.transform,"Summary",Vector2.zero,new Vector2(250,80)).gameObject.AddComponent<TextMeshProUGUI>();
            var indicator=Rect(go.transform,"Dot",Vector2.zero,new Vector2(18,18)).gameObject.AddComponent<Image>();
            var so=new SerializedObject(test);so.FindProperty("usageSummary").objectReferenceValue=label;so.FindProperty("usageDot").objectReferenceValue=indicator;
            var a=so.FindProperty("speakerControllers");a.arraySize=6;for(int i=0;i<6;i++)a.GetArrayElementAtIndex(i).objectReferenceValue=slots[i];so.ApplyModifiedPropertiesWithoutUndo();
            foreach(int count in new[]{0,2,6,0})
            {
                for(int i=0;i<6;i++)slots[i].isSpeakerTaken=i<count;test._RefreshMap();
                if(label.text!="Speakers "+count+" / 6 in use")throw new Exception("Isolated count mismatch");
                if(count==0&&indicator.color.g>.71f||count>0&&indicator.color.r>.2f)throw new Exception("Dot state mismatch");
                manager.RecalculateUsableCount();if((int)typeof(SpeakerManager).GetField("UsableSpeakerCount",flags).GetValue(manager)!=6-count)throw new Exception("Manager available capacity mismatch");
            }
            report.AppendLine("Isolated runtime summary: 0/6 -> 2/6 -> 6/6 -> 0/6, green/gray transitions PASS; no Update polling added.");
            report.AppendLine("Manager six-slot allocation table and available capacity 6 -> 4 -> 0 -> 6 PASS.");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
}
