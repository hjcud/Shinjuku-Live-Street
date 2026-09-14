using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharp;
using UdonSharpEditor;

[InitializeOnLoad]
public static class SpeakerMapSideNameSetup
{
    const string Output="output/speaker-map-ui-20260912/";
    const string Prefab="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
    static SpeakerMapSideNameSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"side-name-layout-v10.request";if(!File.Exists(request))return;File.Delete(request);
        try{Run();}catch(Exception e){File.WriteAllText(Output+"side-name-layout.failed",e.ToString());Debug.LogException(e);}
    }
    static T F<T>(object o,string n)=>(T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    static void Dirty(UnityEngine.Object o){EditorUtility.SetDirty(o);if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);}
    static RectTransform RectChild(Transform parent,string name)
    {
        var found=parent.Find(name);if(found)return (RectTransform)found;
        var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Map label collision UI");go.layer=0;go.transform.SetParent(parent,false);return (RectTransform)go.transform;
    }
    static TextMeshProUGUI TextChild(Transform parent,string name,TextMeshProUGUI source)
    {
        var found=parent.Find(name);var text=found?found.GetComponent<TextMeshProUGUI>():UnityEngine.Object.Instantiate(source.gameObject,parent).GetComponent<TextMeshProUGUI>();
        text.name=name;text.text="";text.richText=false;text.raycastTarget=false;text.enableWordWrapping=false;text.enableAutoSizing=false;text.overflowMode=TextOverflowModes.Ellipsis;
        text.transform.localScale=Vector3.one;return text;
    }
    static void Configure(SpeakerMap map)
    {
        var overlay=F<RectTransform>(map,"mapRect");var template=F<RectTransform>(map,"markerTemplate");var source=template.Find("PerformerName").GetComponent<TextMeshProUGUI>();
        SpeakerMapMarkerReview.ConfigureEdgeLabels(source);
        var line=RectChild(template,"NameLeader");line.anchorMin=line.anchorMax=line.pivot=new Vector2(.5f,.5f);line.sizeDelta=new Vector2(45,1.5f);
        var image=line.GetComponent<UnityEngine.UI.Image>();if(!image)image=line.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.44f,.53f,.45f,.75f);image.raycastTarget=false;line.gameObject.SetActive(false);
        var number=TextChild(template,"NameNumber",source);number.fontSize=24;number.margin=Vector4.zero;number.alignment=TextAlignmentOptions.Midline;number.rectTransform.anchorMin=number.rectTransform.anchorMax=number.rectTransform.pivot=new Vector2(.5f,.5f);number.gameObject.SetActive(false);
        var panel=RectChild(overlay,"OverflowNames");panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.zero;panel.sizeDelta=new Vector2(320,316);
        var bg=panel.GetComponent<UnityEngine.UI.Image>();if(!bg)bg=panel.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.89f,.88f,.81f,.95f);bg.raycastTarget=false;
        var rows=new TextMeshProUGUI[6];for(int i=0;i<6;i++)
        {
            rows[i]=TextChild(panel,"Row"+i,source);rows[i].alignment=TextAlignmentOptions.MidlineLeft;var r=rows[i].rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.sizeDelta=new Vector2(300,50);r.anchoredPosition=new Vector2(10,-8-i*50);rows[i].gameObject.SetActive(false);Dirty(rows[i]);Dirty(r);
        }
        panel.gameObject.SetActive(false);
        var layout=map.GetComponent<SpeakerMapNameLayout>();if(!layout)layout=map.gameObject.AddUdonSharpComponent<SpeakerMapNameLayout>();
        layout.mapRect=overlay;layout.overflowPanel=panel;layout.overflowRows=rows;
        layout.nameFontSize=source.fontSize;
        var here=map.GetComponentsInChildren<RectTransform>(true).Where(x=>x.name=="Here").ToArray();
        var settings=map.GetComponentsInChildren<RectTransform>(true).Where(x=>x.name=="SettingsOutline").ToArray();
        layout.protectedAreas=here.Concat(settings).Concat(here.SelectMany(x=>x.GetComponentsInChildren<TextMeshProUGUI>(true)).Select(x=>x.rectTransform)).Distinct().ToArray();
        map.nameLayout=layout;
        foreach(var obj in new UnityEngine.Object[]{source,line,image,number,panel,bg,layout,map})Dirty(obj);
        // An interrupted first AddUdonSharpComponent can leave its backing program null.
        var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(layout);
        var program=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/_Shinjuku/Scripts/UI/SpeakerMapNameLayout.asset");
        backing.programSource=program;program.UpdateProgram();
        var backingSO=new SerializedObject(backing);backingSO.FindProperty("serializedProgramAsset").objectReferenceValue=program.SerializedProgramAsset;backingSO.ApplyModifiedProperties();Dirty(backing);
        try{UdonSharpEditorUtility.CopyProxyToUdon(layout);}catch(Exception e){throw new Exception("Layout binding: "+FormatterInfo(typeof(SpeakerMapNameLayout)),e);}
        try{UdonSharpEditorUtility.CopyProxyToUdon(map);}catch(Exception e){throw new Exception("Map binding: "+FormatterInfo(typeof(SpeakerMap)),e);}
        Dirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(layout));Dirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(map));
    }
    static string FormatterInfo(Type target)
    {
        var sb=new StringBuilder();
        foreach(var t in typeof(UdonSharpEditorUtility).Assembly.GetTypes().Where(x=>x.Name=="UdonSharpBehaviourFormatterManager"))
        {
            var type=t.ContainsGenericParameters?t.MakeGenericType(target):t;
            var f=type.GetField("fieldLayout",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            var names=(string[])f.GetValue(null);sb.AppendLine(type+" "+(names==null?"null":string.Join(",",names.Select((x,i)=>i+":"+(x??"NULL")))));
        }
        return sb.ToString();
    }
    static void Run()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected original TEST");
        const string programPath="Assets/_Shinjuku/Scripts/UI/SpeakerMapNameLayout.asset";
        if(!AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath))
        {
            var program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Shinjuku/Scripts/UI/SpeakerMapNameLayout.cs");
            AssetDatabase.CreateAsset(program,programPath);AssetDatabase.SaveAssets();
        }
        if(UdonSharpProgramAsset.AnyUdonSharpScriptHasError())throw new Exception("Resolve Udon compiler errors before applying UI");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(x=>x.gameObject.scene==scene).ToArray();if(maps.Length!=2)throw new Exception("Expected two maps");
        string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss");EditorSceneManager.SaveScene(scene,Output+"TEST.before-side-labels-"+stamp+".unity",true);File.Copy(Prefab,Output+"SpeakerMap.before-side-labels-"+stamp+".prefab",false);
        var report=new StringBuilder();
        foreach(var map in maps){Configure(map);Verify(map,report);}
        var root=PrefabUtility.LoadPrefabContents(Prefab);try{Configure(root.GetComponent<SpeakerMap>());PrefabUtility.SaveAsPrefabAsset(root,Prefab);}finally{PrefabUtility.UnloadPrefabContents(root);}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        report.AppendLine("PASS both original map instances and shared prefab saved; source anchors/font styles preserved; runtime client not exercised.");File.WriteAllText(Output+"side-name-layout.done",report.ToString());
    }
    static void Verify(SpeakerMap map,StringBuilder report)
    {
        var layout=map.nameLayout;var overlay=F<RectTransform>(map,"mapRect");var template=F<RectTransform>(map,"markerTemplate");
        var markers=new RectTransform[6];var names=new TextMeshProUGUI[6];
        string[] values={"Cuding","한국어이름","新宿音楽家","VeryVeryVeryLongPerformerNameWWWWWW","みどりの音楽","Mixed 한글 漢字"};
        var cases=new[]{
            new[]{new Vector2(.15f,.7f),new Vector2(.98f,.7f),new Vector2(.4f,.38f),new Vector2(.65f,.4f),new Vector2(.2f,.4f),new Vector2(.8f,.8f)},
            new[]{new Vector2(.5f,.5f),new Vector2(.53f,.5f),new Vector2(.5f,.54f),new Vector2(.53f,.54f),new Vector2(.51f,.49f),new Vector2(.49f,.52f)},
            Enumerable.Repeat(new Vector2(.02f,.98f),6).ToArray(),
            new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,0),new Vector2(.5f,1)}
        };
        try
        {
            for(int i=0;i<6;i++){markers[i]=(RectTransform)UnityEngine.Object.Instantiate(template.gameObject,overlay).transform;markers[i].gameObject.hideFlags=HideFlags.DontSave;names[i]=markers[i].Find("PerformerName").GetComponent<TextMeshProUGUI>();names[i].text=values[i];markers[i].gameObject.SetActive(true);}
            for(int c=0;c<cases.Length;c++)
            {
                for(int i=0;i<6;i++){markers[i].anchorMin=markers[i].anchorMax=cases[c][i];markers[i].anchoredPosition3D=Vector3.zero;}
                layout.Layout(markers,names);Validate(layout,markers,names);
                var old=(int[])F<int[]>(layout,"choices").Clone();layout.Layout(markers,names);
                if(!old.SequenceEqual(F<int[]>(layout,"choices")))throw new Exception("Unchanged layout flipped candidates");
                foreach(var marker in markers)
                {
                    var badge=marker.Find("NameNumber").GetComponent<TextMeshProUGUI>();
                    if(!badge.gameObject.activeSelf)continue;
                    badge.ForceMeshUpdate(true);
                    if(badge.textInfo.characterInfo.Take(badge.textInfo.characterCount).Count(x=>x.isVisible)<badge.text.Length)throw new Exception("Overflow number badge clipped: "+badge.text);
                    if(!BoundsInside(overlay,badge.rectTransform))throw new Exception("Overflow number badge outside map");
                    report.AppendLine("BADGE "+badge.text+" font="+badge.fontSize+" rect="+badge.rectTransform.rect+" pos="+badge.rectTransform.anchoredPosition+" visible="+badge.textInfo.characterInfo.Take(badge.textInfo.characterCount).Count(x=>x.isVisible));
                    File.WriteAllText(Output+"side-name-badge-diagnostic.txt",report.ToString());
                }
                SpeakerMapSetup.CapturePanel((RectTransform)map.transform.Find("MapCanvas"),Output+map.name.Replace(" ","-")+"-side-names-"+c+".png");
                report.AppendLine(map.name+" case "+c+": candidates="+string.Join(",",old)+" overflow="+layout.overflowPanel.gameObject.activeSelf);
            }
            // Explicit current-location label avoidance, with unchanged marker positions.
            var here=layout.protectedAreas[0];Vector3 p=overlay.InverseTransformPoint(here.position);Vector2 anchor=new Vector2((p.x-overlay.rect.xMin)/overlay.rect.width,(p.y-overlay.rect.yMin)/overlay.rect.height);
            for(int i=0;i<6;i++){markers[i].gameObject.SetActive(i==0);markers[i].anchorMin=markers[i].anchorMax=anchor;markers[i].anchoredPosition3D=Vector3.zero;}
            layout.Layout(markers,names);Validate(layout,markers,names);
            SpeakerMapSetup.CapturePanel((RectTransform)map.transform.Find("MapCanvas"),Output+map.name.Replace(" ","-")+"-side-names-here.png");
            markers[0].gameObject.SetActive(false);layout.Layout(markers,names);if(layout.overflowPanel.gameObject.activeSelf)throw new Exception("Stale overflow after return");
        }
        finally
        {
            foreach(var marker in markers)if(marker)UnityEngine.Object.DestroyImmediate(marker.gameObject);
            layout.overflowPanel.gameObject.SetActive(false);
            typeof(SpeakerMapNameLayout).GetField("choices",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(layout,null);
        }
    }
    static void Validate(SpeakerMapNameLayout layout,RectTransform[] markers,TextMeshProUGUI[] names)
    {
        var choices=F<int[]>(layout,"choices");int overflow=0;
        for(int i=0;i<markers.Length;i++)
        {
            if(!markers[i].gameObject.activeSelf)continue;
            if(markers[i].anchoredPosition3D!=Vector3.zero)throw new Exception("Icon moved");
            if(choices[i]<0){overflow++;continue;}
            var text=markers[i].Find(choices[i]%2==0?"PerformerNameLeft":"PerformerNameRight").GetComponent<TextMeshProUGUI>();text.ForceMeshUpdate(true);
            if(text.richText||text.enableAutoSizing||text.enableWordWrapping||text.fontStyle!=FontStyles.Bold||text.overflowMode!=TextOverflowModes.Ellipsis)throw new Exception("Name style changed");
            if(!text.textInfo.characterInfo.Take(text.textInfo.characterCount).Any(x=>x.isVisible))throw new Exception("No name glyphs");
            if(!BoundsInside(layout.mapRect,text.rectTransform))throw new Exception("Label outside map");
            foreach(var area in layout.protectedAreas)if(RectOverlap(layout.mapRect,text.rectTransform,area))throw new Exception("Label overlaps protected location/settings");
            for(int j=0;j<i;j++)if(markers[j].gameObject.activeSelf&&choices[j]>=0)
            {
                var other=markers[j].Find(choices[j]%2==0?"PerformerNameLeft":"PerformerNameRight").GetComponent<RectTransform>();if(RectOverlap(layout.mapRect,text.rectTransform,other))throw new Exception("Names overlap");
            }
        }
        if(overflow>0)
        {
            if(!layout.overflowPanel.gameObject.activeSelf||layout.overflowRows.Count(x=>x.gameObject.activeSelf)!=overflow)throw new Exception("Overflow names lost");
            if(!BoundsInside(layout.mapRect,layout.overflowPanel))throw new Exception("Overflow panel outside map");
        }
    }
    static Rect Bounds(RectTransform map,RectTransform r){var c=new Vector3[4];r.GetWorldCorners(c);var a=map.InverseTransformPoint(c[0]);var b=map.InverseTransformPoint(c[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
    static bool RectOverlap(RectTransform map,RectTransform a,RectTransform b)=>Bounds(map,a).Overlaps(Bounds(map,b));
    static bool BoundsInside(RectTransform map,RectTransform r){var b=Bounds(map,r);return b.xMin>=map.rect.xMin+7.8f&&b.xMax<=map.rect.xMax-7.8f&&b.yMin>=map.rect.yMin+7.8f&&b.yMax<=map.rect.yMax-7.8f;}
}
