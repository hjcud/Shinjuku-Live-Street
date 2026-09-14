using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>Small static sign-atlas signature; no runtime behaviours or original-material edits.</summary>
[InitializeOnLoad]
public static class SpeakerMapStationSignature
{
    const string Output="output/speaker-map-ui-20260912";
    const string Art="Assets/_Shinjuku/UI/SpeakerMap";
    const string Source="Assets/model/Texture_Main/2-Shinjuku-1024_BaseColor.png";
    static SpeakerMapStationSignature(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(!File.Exists(Output+"/station-signature.request"))return;
        File.Delete(Output+"/station-signature.request");
        try{Apply();}catch(Exception e){File.WriteAllText(Output+"/station-signature.failed",DateTime.Now+"\n"+e);Debug.LogException(e);}
    }
    static void Record(UnityEngine.Object o){EditorUtility.SetDirty(o);if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);}
    static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var rect=parent.Find(name) as RectTransform;
        if(!rect){rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);}
        rect.gameObject.layer=parent.gameObject.layer;rect.anchorMin=rect.anchorMax=new Vector2(1,0);rect.pivot=Vector2.one*.5f;
        rect.anchoredPosition=position;rect.sizeDelta=size;Record(rect);return rect;
    }
    static void Glyph(Transform group,string name,Vector2 position,Vector2 size,UnityEngine.Rect uv,Color tint,Material material,Texture texture)
    {
        var rect=Rect(group,name,position,size);var image=rect.GetComponent<UnityEngine.UI.RawImage>();if(!image)image=rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture=texture;image.uvRect=uv;image.material=material;image.color=tint;image.raycastTarget=false;Record(image);
    }
    static void Place(Transform root,Material material,Texture texture)
    {
        var floor=root.Find("MapCanvas/FloorPlan") as RectTransform;
        if(!floor||!floor.Find("SpawnGate"))throw new Exception("Missing existing map/side label");
        var group=Rect(floor,"StationSignature",Vector2.zero,new Vector2(240,104));
        group.anchorMin=group.anchorMax=group.pivot=new Vector2(1,0);group.anchoredPosition=new Vector2(-24,24);Record(group);
        // Atlas coordinates are the original artwork, excluding its English heading and unrelated glyphs.
        Glyph(group,"VrcSymbol",new Vector2(-198,59),new Vector2(80,36.5f),new UnityEngine.Rect(4f/1024,746f/1024,322f/1024,146f/1024),new Color(.27f,.48f,.37f,1),material,texture);
        // Keep the old atlas glyphs recoverable, but use real Japanese text for the station name.
        var oldName=group.Find("StationName");if(oldName){oldName.gameObject.SetActive(false);Record(oldName.gameObject);}
        var model=floor.Find("SpawnGate").GetComponent<TextMeshProUGUI>();
        var nameRect=Rect(group,"StationNameText",new Vector2(-75,60),new Vector2(144,66));
        var name=nameRect.GetComponent<TextMeshProUGUI>();if(!name)name=nameRect.gameObject.AddComponent<TextMeshProUGUI>();
        name.font=model.font;name.fontSharedMaterial=model.fontSharedMaterial;name.text="新宿駅";
        name.fontSize=44;name.fontStyle=FontStyles.Bold;name.color=new Color(.39f,.46f,.50f,1);name.alignment=TextAlignmentOptions.MidlineRight;
        name.enableAutoSizing=false;name.enableWordWrapping=false;name.richText=false;name.raycastTarget=false;Record(name);
        var textRect=Rect(group,"English",new Vector2(-120,14),new Vector2(240,28));
        var text=textRect.GetComponent<TextMeshProUGUI>();if(!text)text=textRect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=model.font;text.fontSharedMaterial=model.fontSharedMaterial;
        text.text="VRC Shinjuku Station";text.fontSize=18;text.color=new Color(.45f,.52f,.55f,1);text.alignment=TextAlignmentOptions.MidlineRight;
        text.enableAutoSizing=false;text.enableWordWrapping=false;text.richText=false;text.raycastTarget=false;Record(text);
    }
    static void Apply()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Open original TEST");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();if(maps.Length!=2)throw new Exception("Expected two boards");
        var existing=maps.SelectMany(m=>m.GetComponentsInChildren<RectTransform>(true)).Where(r=>!r.name.Equals("StationSignature")&&!HasSignatureParent(r)).ToArray();
        var positions=existing.Select(r=>r.position).ToArray();var sizes=existing.Select(r=>r.sizeDelta).ToArray();var states=maps.Select(m=>EditorJsonUtility.ToJson(m)).ToArray();
        var sourceBytes=File.ReadAllBytes(Source);var importerBytes=File.ReadAllBytes(Source+".meta");
        AssetDatabase.ImportAsset(Art+"/StationLogoMask.shader",ImportAssetOptions.ForceUpdate);
        var shader=Shader.Find("Shinjuku/UI/StationLogoMask");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Logo shader not ready");
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Source);if(!texture)throw new Exception("Original logo atlas missing");
        var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"/StationLogoMask.mat");
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,Art+"/StationLogoMask.mat");}
        material.shader=shader;material.mainTexture=texture;EditorUtility.SetDirty(material);
        foreach(var map in maps)Undo.RegisterFullObjectHierarchyUndo(map.gameObject,"Add bottom-right station signature");
        string path=Art+"/SpeakerMap.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Place(prefab.transform,material,texture);PrefabUtility.SaveAsPrefabAsset(prefab,path);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
        var report=new StringBuilder();
        foreach(var map in maps)
        {
            Place(map.transform,material,texture);Canvas.ForceUpdateCanvases();
            var canvas=(RectTransform)map.transform.Find("MapCanvas");var floor=(RectTransform)canvas.Find("FloorPlan");var group=(RectTransform)floor.Find("StationSignature");
            if(group.anchoredPosition!=new Vector2(-24,24)||group.sizeDelta!=new Vector2(240,104))throw new Exception("Incorrect signature insets");
            var text=group.Find("English").GetComponent<TextMeshProUGUI>();text.ForceMeshUpdate();
            var stationName=group.Find("StationNameText").GetComponent<TextMeshProUGUI>();stationName.ForceMeshUpdate();
            if(stationName.text!="新宿駅"||stationName.isTextOverflowing||!stationName.font.HasCharacters(stationName.text)||stationName.textInfo.characterCount!=3)
                throw new Exception("Japanese name invalid: "+stationName.text+" overflow="+stationName.isTextOverflowing+" preferred="+stationName.GetPreferredValues());
            if(text.isTextOverflowing||!text.font.HasCharacters(text.text)||group.GetComponentsInChildren<UnityEngine.UI.Graphic>().Any(g=>g.raycastTarget))
            {
                SpeakerMapSetup.CapturePanel(canvas,Output+"/signature-diagnostic.png");
                throw new Exception("Signature check: overflow="+text.isTextOverflowing+" glyphs="+text.font.HasCharacters(text.text)+" preferred="+text.GetPreferredValues()+" rect="+text.rectTransform.rect+" raycasts="+string.Join(",",group.GetComponentsInChildren<UnityEngine.UI.Graphic>().Where(g=>g.raycastTarget).Select(g=>g.name)));
            }
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-station-signature-tight.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-station-signature-tight.png");
            report.AppendLine(map.name+": one 240x104 signature; right/bottom 24px; real Japanese glyphs 新宿駅; glyphs/overlap/raycast PASS");
        }
        for(int i=0;i<existing.Length;i++)if(Vector3.Distance(existing[i].position,positions[i])>.00001f||existing[i].sizeDelta!=sizes[i])throw new Exception("Existing UI moved: "+existing[i].name);
        for(int i=0;i<maps.Length;i++)if(states[i]!=EditorJsonUtility.ToJson(maps[i]))throw new Exception("Speaker map runtime state changed");
        if(!sourceBytes.SequenceEqual(File.ReadAllBytes(Source))||!importerBytes.SequenceEqual(File.ReadAllBytes(Source+".meta")))throw new Exception("Original logo texture/importer changed");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/station-signature.done",DateTime.Now+"\n"+report+"Original texture/importer, existing rects, side label and runtime calibration unchanged. Active TEST unsaved; recovery saved.\n");
    }
    static bool HasSignatureParent(Transform t){for(var p=t.parent;p;p=p.parent)if(p.name=="StationSignature")return true;return false;}
}
