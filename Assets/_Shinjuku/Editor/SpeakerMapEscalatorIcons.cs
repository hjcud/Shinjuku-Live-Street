using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Static escalator pictograms only; does not restyle or recrop the map.</summary>
[InitializeOnLoad]
public static class SpeakerMapEscalatorIcons
{
    private const string Output="output/speaker-map-ui-20260912";
    private const string AssetPath="Assets/_Shinjuku/UI/SpeakerMap/EscalatorIcon.png";
    static SpeakerMapEscalatorIcons(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"/escalator-icons.request";
        if(!File.Exists(request))return;
        File.Delete(request);
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"/escalator-icons.failed",DateTime.Now+"\n"+e);Debug.LogException(e);}
    }
    private static float Segment(Vector2 p,Vector2 a,Vector2 b)
    {
        var ab=b-a;return Vector2.Distance(p,a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude));
    }
    private static Sprite CreateSprite()
    {
        if(!File.Exists(AssetPath))
        {
            const int size=256;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color32[size*size];
            var body=new[]{new Vector2(37,183),new Vector2(80,183),new Vector2(173,92),new Vector2(215,92),new Vector2(215,123),new Vector2(186,123),new Vector2(94,214),new Vector2(37,214)};
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                // Top-left coordinates. Transparent background, rounded frame and person-on-escalator symbol.
                var p=new Vector2(x+.5f,y+.5f);var q=new Vector2(Mathf.Abs(p.x-128)-91,Mathf.Abs(p.y-128)-91);
                float rounded= new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-31;
                float distance=Mathf.Abs(rounded)-5;
                for(int i=0;i<body.Length;i++)distance=Mathf.Min(distance,Segment(p,body[i],body[(i+1)%body.Length])-5);
                distance=Mathf.Min(distance,Vector2.Distance(p,new Vector2(112,60))-13);
                distance=Mathf.Min(distance,Segment(p,new Vector2(112,88),new Vector2(112,143))-9);
                pixels[(size-1-y)*size+x]=new Color32(88,103,112,(byte)Mathf.RoundToInt(Mathf.Clamp01(.5f-distance)*255));
            }
            texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(AssetPath,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(AssetPath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetPath);importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=256;
            importer.SaveAndReimport();
        }
        // World-space signs minify this 256px artwork to about 20px. Prefiltering keeps thin borders continuous.
        var settings=(TextureImporter)AssetImporter.GetAtPath(AssetPath);
        if(!settings.mipmapEnabled||settings.filterMode!=FilterMode.Trilinear)
        {settings.mipmapEnabled=true;settings.filterMode=FilterMode.Trilinear;settings.SaveAndReimport();}
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath);if(!sprite)throw new Exception("Escalator sprite missing");return sprite;
    }
    private static void Record(UnityEngine.Object o)
    {
        EditorUtility.SetDirty(o);if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }
    private static void Place(Transform root,Sprite sprite)
    {
        var floor=root.Find("MapCanvas/FloorPlan");
        var smoking=floor?floor.Find("SmokingAreaIcon") as RectTransform:null;
        if(!smoking)throw new Exception("Existing smoking icon missing");
        foreach(string name in new[]{"WestStairsLabel","StairsLabel"})
        {
            var label=floor.Find(name).GetComponent<TextMeshProUGUI>();label.ForceMeshUpdate();
            float left=float.PositiveInfinity,right=float.NegativeInfinity,bottom=float.PositiveInfinity,top=float.NegativeInfinity;
            foreach(var c in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c=>c.isVisible&&c.lineNumber==0))
            {left=Mathf.Min(left,c.bottomLeft.x);right=Mathf.Max(right,c.topRight.x);bottom=Mathf.Min(bottom,c.bottomLeft.y);top=Mathf.Max(top,c.topRight.y);}
            if(float.IsInfinity(right))throw new Exception("First Japanese line missing: "+name);
            if(name=="WestStairsLabel")
            {
                // Source-map escalator right edge x=339. Keep a compact 12px gap to the actual Japanese glyphs.
                var markers=(RectTransform)floor.Find("SpeakerMarkers");
                var position=label.rectTransform.anchoredPosition;
                var sourceOffset=SpeakerMapViewportSetup.SourceOffset((RectTransform)floor);
                position.x=sourceOffset.x+339-900+12-left;
                position.y=sourceOffset.y+600-186; // 24px below the previous source y=162 caption.
                label.rectTransform.anchoredPosition=position;Record(label.rectTransform);
            }
            var icon=label.transform.Find("EscalatorIcon") as RectTransform;
            if(!icon){icon=new GameObject("EscalatorIcon",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();icon.SetParent(label.transform,false);}
            icon.anchorMin=icon.anchorMax=icon.pivot=Vector2.one*.5f;icon.localScale=Vector3.one;icon.localRotation=Quaternion.identity;
            // Escalator labels use 20pt versus the larger smoking caption. Scale the icon to its own label.
            icon.sizeDelta=Vector2.one*label.fontSize;icon.anchoredPosition=new Vector2(right+3+icon.sizeDelta.x*.5f,(bottom+top)*.5f);
            icon.gameObject.layer=label.gameObject.layer;
            var graphic=icon.GetComponent<UnityEngine.UI.Image>();graphic.sprite=sprite;graphic.color=Color.white;graphic.preserveAspect=true;graphic.raycastTarget=false;
            Record(icon);Record(graphic);Record(icon.gameObject);
        }
    }
    private static void Apply()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open TEST in edit mode");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).OrderBy(m=>m.name).ToArray();if(maps.Length!=2)throw new Exception("Expected two maps");
        var states=maps.Select(m=>EditorJsonUtility.ToJson(m)).ToArray();
        var originalRects=maps.SelectMany(m=>m.GetComponentsInChildren<RectTransform>(true)).Where(r=>r.name!="WestStairsLabel"&&r.name!="EscalatorIcon").Distinct().ToArray();
        var positions=originalRects.Select(r=>r.position).ToArray();var sizes=originalRects.Select(r=>r.sizeDelta).ToArray();
        var sprite=CreateSprite();
        // Prefab first: instances inherit the icons, avoiding scene-added duplicate pictograms.
        const string path="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Place(prefab.transform,sprite);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps){Undo.RegisterFullObjectHierarchyUndo(map.gameObject,"Add escalator pictograms");Place(map.transform,sprite);}
        var report=new StringBuilder();
        for(int i=0;i<originalRects.Length;i++)if(Vector3.Distance(positions[i],originalRects[i].position)>.0001f||originalRects[i].sizeDelta!=sizes[i])throw new Exception("Existing layout changed: "+originalRects[i].name);
        for(int i=0;i<maps.Length;i++)
        {
            var map=maps[i];if(states[i]!=EditorJsonUtility.ToJson(map))throw new Exception("Runtime map references changed");
            var canvas=(RectTransform)map.transform.Find("MapCanvas");var floor=(RectTransform)canvas.Find("FloorPlan");
            foreach(string name in new[]{"WestStairsLabel","StairsLabel"})
            {
                var label=floor.Find(name);var icons=label.GetComponentsInChildren<UnityEngine.UI.Image>().Where(g=>g.name=="EscalatorIcon").ToArray();
                if(icons.Length!=1||icons[0].raycastTarget||icons[0].sprite!=sprite)throw new Exception("Invalid escalator icon: "+name);
                var corners=new Vector3[4];icons[0].rectTransform.GetWorldCorners(corners);
                if(corners.Any(c=>!floor.rect.Contains((Vector2)floor.InverseTransformPoint(c))))throw new Exception("Icon clipped by map viewport");
                if(icons[0].rectTransform.sizeDelta!=Vector2.one*label.GetComponent<TextMeshProUGUI>().fontSize)throw new Exception("Icon size does not match its label");
                if(name=="WestStairsLabel")
                {
                    var text=label.GetComponent<TextMeshProUGUI>();text.ForceMeshUpdate();
                    float left=text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible&&c.lineNumber==0).Min(c=>c.bottomLeft.x);
                    float edge=SpeakerMapViewportSetup.SourceOffset((RectTransform)floor).x+339-900;
                    float gap=text.rectTransform.anchoredPosition.x+left-edge;
                    if(Mathf.Abs(gap-12)>.01f)throw new Exception("Escalator-to-label gap mismatch");
                    report.AppendLine(map.name+": west label actual glyph gap to escalator = "+gap+"px PASS");
                }
                report.AppendLine(map.name+" / "+name+": icon "+icons[0].rectTransform.sizeDelta.x+"px matches label size; 3px Japanese-line gap, no raycast, within viewport PASS");
            }
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-escalator-icons-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-escalator-icons.png");
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/escalator-icons.done",DateTime.Now+"\n"+report+"Only west escalator label position and escalator icon rects changed; crop, other labels, markers, settings and runtime map bindings unchanged. TEST remains unsaved; recovery copy saved.\n");
    }
}
