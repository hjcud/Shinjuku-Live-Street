using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TMPro;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UdonSharpEditor;
using UdonSharp;

[InitializeOnLoad]
public static class SpeakerMapMarkerReview
{
    const string Output = "output/speaker-map-ui-20260912/";
    static SpeakerMapMarkerReview() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string apply = Output + "marker-edge-style-v5.request";
        if (File.Exists(apply))
        {
            File.Delete(apply);
            try { Apply(); } catch(Exception e) { File.WriteAllText(Output + "marker-layout.failed", e.ToString()); Debug.LogException(e); }
            return;
        }
        string request = Output + "marker-inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try
        {
            var report = new StringBuilder();
            foreach (var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true))
            {
                var so = new SerializedObject(map);
                var rect = (RectTransform)so.FindProperty("mapRect").objectReferenceValue;
                var template = (RectTransform)so.FindProperty("markerTemplate").objectReferenceValue;
                report.AppendLine(map.name + " scene=" + map.gameObject.scene.path + " map=" + rect.rect + " scale=" + rect.localScale);
                foreach (var t in template.GetComponentsInChildren<RectTransform>(true))
                {
                    report.AppendLine(t.name + " rect=" + t.rect + " pos=" + t.anchoredPosition + " scale=" + t.localScale);
                    var text = t.GetComponent<TextMeshProUGUI>();
                    if(text) report.AppendLine(" font=" + text.fontSize + " fontAsset="+text.font.name+" style="+text.fontStyle+" weight="+text.fontWeight+" mat=" + AssetDatabase.GetAssetPath(text.fontSharedMaterial) + " outline=" + text.fontSharedMaterial.GetFloat("_OutlineWidth") + " alignment=" + text.alignment);
                }
            }
            File.WriteAllText(Output + "marker-inspect.txt", report.ToString());
        }
        catch(Exception e) { File.WriteAllText(Output + "marker-inspect.failed", e.ToString()); }
    }

    static void Dirty(UnityEngine.Object obj)
    {
        EditorUtility.SetDirty(obj);
        if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    }
    static void Style(GameObject root, Material material)
    {
        var label = root.transform.Find("MapCanvas/FloorPlan/SpeakerMarkers/SpeakerMarkerTemplate/PerformerName").GetComponent<TextMeshProUGUI>();
        Undo.RecordObject(label, "Compact map speaker names");
        Undo.RecordObject(label.rectTransform, "Compact map speaker names");
        label.fontSharedMaterial = material;
        label.fontStyle = FontStyles.Bold;
        label.fontWeight = FontWeight.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Midline;
        label.extraPadding = true;
        label.enableWordWrapping = false;
        label.enableAutoSizing = false;
        label.richText = false;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.sizeDelta = new Vector2(280, 50);
        label.rectTransform.anchoredPosition = new Vector2(0, -38);
        label.UpdateMeshPadding();
        Dirty(label); Dirty(label.rectTransform);
        ConfigureEdgeLabels(label);
        var map = root.GetComponent<SpeakerMap>();
        UdonSharpEditorUtility.CopyProxyToUdon(map);
        Dirty(map); Dirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(map));
    }
    public static void ConfigureEdgeLabels(TextMeshProUGUI label)
    {
        label.gameObject.SetActive(true);
        string[] names={"PerformerNameLeft","PerformerNameRight"};
        for(int i=0;i<2;i++)
        {
            var existing=label.transform.parent.Find(names[i]);
            var edge=existing?existing.GetComponent<TextMeshProUGUI>():UnityEngine.Object.Instantiate(label.gameObject,label.transform.parent).GetComponent<TextMeshProUGUI>();
            edge.name=names[i];
            edge.font=label.font;edge.fontSharedMaterial=label.fontSharedMaterial;edge.color=label.color;
            edge.fontStyle=FontStyles.Bold;edge.fontWeight=FontWeight.Bold;edge.fontSize=label.fontSize;
            edge.enableAutoSizing=false;edge.enableWordWrapping=false;edge.richText=false;edge.raycastTarget=false;
            edge.overflowMode=TextOverflowModes.Ellipsis;edge.extraPadding=true;
            edge.alignment=i==0?TextAlignmentOptions.MidlineLeft:TextAlignmentOptions.MidlineRight;
            edge.UpdateMeshPadding();edge.gameObject.SetActive(false);
            Dirty(edge);Dirty(edge.gameObject);Dirty(edge.rectTransform);
        }
        Dirty(label.gameObject);
    }
    static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new Exception("Expected original TEST");
        var maps = UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m => m.gameObject.scene == scene).ToArray();
        if (maps.Length != 2) throw new Exception("Expected two live maps");
        UdonSharpProgramAsset.CompileAllCsPrograms();
        foreach (var map in maps)
        {
            var program = UdonSharpEditorUtility.GetBackingUdonBehaviour(map).programSource.SerializedProgramAsset.RetrieveProgram();
            if (!program.EntryPoints.GetExportedSymbols().Any(s => s.Contains("LayoutPerformerName"))) throw new Exception("Updated layout is not compiled: " + string.Join(",", program.EntryPoints.GetExportedSymbols()));
        }
        EditorSceneManager.SaveScene(scene, Output + "TEST.before-marker-layout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
        const string folder = "Assets/_Shinjuku/UI/SpeakerMap/";
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "PerformerNameOutline.mat");
        Undo.RecordObject(material, "Thicken map name outline");
        material.SetFloat("_OutlineWidth", .28f);
        material.SetFloat("_FaceDilate", .3f);
        material.SetColor("_FaceColor",((Color)new Color32(75,105,84,255)).linear);
        material.SetColor("_OutlineColor",((Color)new Color32(238,231,213,255)).linear);
        material.EnableKeyword("OUTLINE_ON");
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var prefab = PrefabUtility.LoadPrefabContents(folder + "SpeakerMap.prefab");
        try { Style(prefab, material); PrefabUtility.SaveAsPrefabAsset(prefab, folder + "SpeakerMap.prefab"); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var report = new StringBuilder();
        foreach (var map in maps)
        {
            Style(map.gameObject, material);
            Verify(map, report);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.AppendLine("PASS: updated Udon program, shared prefab and both live map templates. Original TEST saved. Preview clones removed. VRChat client not exercised.");
        File.WriteAllText(Output + "marker-edge-style.done", report.ToString());
    }
    static void Verify(SpeakerMap map, StringBuilder report)
    {
        var so = new SerializedObject(map);
        var overlay = (RectTransform)so.FindProperty("mapRect").objectReferenceValue;
        var template = (RectTransform)so.FindProperty("markerTemplate").objectReferenceValue;
        var samples = new System.Collections.Generic.List<GameObject>();
        var anchors = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,.5f), new Vector2(.5f,0), new Vector2(.5f,1), new Vector2(0,.5f), new Vector2(1,.5f) };
        var names = new[] { "Cuding", "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWW", "長い名前の表示確認用スピーカー利用者", "<size=200>Literal</size>", "한국어이름", "新宿音楽", "Cuding 한글 漢字" };
        try
        {
            foreach (var anchor in anchors)
            foreach (var name in names)
            {
                var sample = UnityEngine.Object.Instantiate(template.gameObject, overlay);
                sample.hideFlags = HideFlags.DontSave; samples.Add(sample);
                var marker = (RectTransform)sample.transform;
                marker.anchorMin = marker.anchorMax = anchor; marker.anchoredPosition3D = Vector3.zero;
                var label = sample.GetComponentInChildren<TextMeshProUGUI>(true);
                label.text = name; sample.SetActive(true);
                map.LayoutPerformerName(marker, label, anchor);
                label=sample.GetComponentsInChildren<TextMeshProUGUI>(false).Single();
                label.ForceMeshUpdate();
                if (!label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(c => c.isVisible)) throw new Exception("Name generated no visible glyphs: " + name);
                var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector3 p = overlay.InverseTransformPoint(corner);
                    if(p.x < overlay.rect.xMin + 7.9f || p.x > overlay.rect.xMax - 7.9f || p.y < overlay.rect.yMin + 7.9f || p.y > overlay.rect.yMax - 7.9f) throw new Exception("Name rectangle outside map " + anchor + " " + name);
                }
                if(label.enableAutoSizing || label.richText || label.enableWordWrapping) throw new Exception("Unsafe name presentation");
                if(label.fontStyle!=FontStyles.Bold)throw new Exception("Name is not upright bold");
                foreach(var c in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c=>c.isVisible))
                {
                    if(c.textElement==null || c.textElement.unicode==0x25A1)throw new Exception("Missing name glyph");
                    Material face=label.textInfo.meshInfo[c.materialReferenceIndex].material;
                    if(Vector4.Distance(face.GetColor("_FaceColor"),((Color)new Color32(75,105,84,255)).linear)>.01f || Mathf.Abs(face.GetFloat("_FaceDilate")-.3f)>.01f)throw new Exception("Fallback did not inherit name style: "+c.character);
                }
                if(name=="Cuding" && (anchor.x==0 || anchor.x==1))
                {
                    var visible=label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c=>c.isVisible).ToArray();
                    float edge=anchor.x==0?visible.Min(c=>c.bottomLeft.x):visible.Max(c=>c.topRight.x);
                    var p=marker.InverseTransformPoint(label.transform.TransformPoint(new Vector3(edge,0,0)));
                    if(Mathf.Abs(p.x)>25f)throw new Exception("Short name still pushed from icon: "+p.x);
                }
                sample.SetActive(false);
            }
            // Three readable examples; the test above covers all four corners and four edges.
            for (int i = 0; i < 6; i++)
            {
                var sample = samples[i]; sample.SetActive(true);
                var marker = (RectTransform)sample.transform;
                Vector2 anchor = new[]{new Vector2(.015f,.83f),new Vector2(.99f,.02f),new Vector2(.01f,.97f),new Vector2(.985f,.83f),new Vector2(.40f,.38f),new Vector2(.66f,.38f)}[i];
                marker.anchorMin = marker.anchorMax = anchor; marker.anchoredPosition3D = Vector3.zero;
                var label = sample.transform.Find("PerformerName").GetComponent<TextMeshProUGUI>();
                label.text=new[]{"Cuding",names[1],names[2],"Cuding",names[4],names[5]}[i];
                map.LayoutPerformerName(marker, label, anchor); sample.GetComponentsInChildren<TextMeshProUGUI>(false).Single().ForceMeshUpdate();
            }
            SpeakerMapSetup.CapturePanel((RectTransform)map.transform.Find("MapCanvas"), Output + map.name.Replace(" ", "-") + "-name-edge-style.png");
            report.AppendLine(map.name + ": PASS 63 cases (9 positions x 7 name types); edge-aligned short names within 25px of icon; upright Bold, green face and warm outline inherited by CJK fallback materials; boundary containment preserved.");
        }
        finally { foreach(var sample in samples) if(sample) UnityEngine.Object.DestroyImmediate(sample); }
    }
}
