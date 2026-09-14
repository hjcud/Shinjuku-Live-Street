using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fits the non-rendering settings container without moving or resizing its content.
[InitializeOnLoad]
public static class SpeakerMapSettingsBounds
{
    private const string Output = "output/speaker-map-ui-20260912";
    static SpeakerMapSettingsBounds() { EditorApplication.update += Poll; }
    private static void Record(RectTransform rect)
    {
        EditorUtility.SetDirty(rect);
        if (PrefabUtility.IsPartOfPrefabInstance(rect)) PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }
    private static Vector3[] Corners(RectTransform rect) { var result = new Vector3[4]; rect.GetWorldCorners(result); return result; }
    public static void Fit(RectTransform footer)
    {
        var outline = footer ? footer.Find("SettingsOutline") as RectTransform : null;
        if (!footer || !outline) throw new Exception("Missing existing settings container or outline");
        if (footer.GetComponent<UnityEngine.UI.LayoutGroup>() || footer.GetComponent<UnityEngine.UI.ContentSizeFitter>()) throw new Exception("Unexpected automatic footer layout");
        var descendants = footer.GetComponentsInChildren<RectTransform>(true).Where(r => r != footer).ToArray();
        var before = descendants.Select(Corners).ToArray();
        var children = footer.Cast<Transform>().OfType<RectTransform>().ToArray();
        var positions = children.Select(c => c.position).ToArray();
        var sizes = children.Select(c => c.rect.size).ToArray();
        var centre = outline.TransformPoint(outline.rect.center);
        var size = outline.rect.size;
        footer.anchorMin = footer.anchorMax = footer.pivot = new Vector2(.5f,.5f);
        footer.sizeDelta = size;
        footer.position = centre;
        for (int i = 0; i < children.Length; i++)
        {
            children[i].sizeDelta = sizes[i] - Vector2.Scale(footer.rect.size, children[i].anchorMax - children[i].anchorMin);
            children[i].position = positions[i];
            Record(children[i]);
        }
        Record(footer);
        for (int i = 0; i < descendants.Length; i++)
        {
            var after = Corners(descendants[i]);
            for (int j = 0; j < 4; j++) if (Vector3.Distance(before[i][j],after[j]) > .0001f) throw new Exception("Content bounds changed: " + descendants[i].name);
        }
    }
    private static void Poll()
    {
        string verify=Output+"/settings-bounds-verify.request";
        if(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(verify))
        {
            File.Delete(verify);
            try { VerifyRepair(); } catch(Exception e) { File.WriteAllText(Output+"/settings-bounds-repair.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string repair=Output+"/settings-bounds-repair.request";
        if(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(repair))
        {
            File.Delete(repair);
            try { Repair(); } catch(Exception e) { File.WriteAllText(Output+"/settings-bounds-repair.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string inspect = Output + "/settings-bounds-inspect.request";
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(inspect))
        {
            File.Delete(inspect);
            var report = new System.Text.StringBuilder();
            foreach(var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true))
            {
                report.AppendLine(map.name+" scene="+map.gameObject.scene.path+" playing="+EditorApplication.isPlaying);
                var footer=map.transform.Find("MapCanvas/LocalSettings");
                foreach(var r in footer.GetComponentsInChildren<RectTransform>(true))
                    report.AppendLine(r.name+" parent="+r.parent.name+" pos="+r.anchoredPosition+" local="+r.localPosition+" size="+r.rect.size+" anchors="+r.anchorMin+"/"+r.anchorMax+" active="+r.gameObject.activeInHierarchy);
                SpeakerMapSetup.CapturePanel((RectTransform)map.transform.Find("MapCanvas"),Output+"/"+map.name+"-bounds-current.png");
            }
            File.WriteAllText(Output+"/settings-bounds-inspect.txt",report.ToString());
        }
        string request = Output + "/settings-bounds.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText(Output + "/settings-bounds.failed", e.ToString()); Debug.LogException(e); }
    }
    private static void Normalize(RectTransform footer)
    {
        if(!footer || footer.rect.size!=new Vector2(600,156)) throw new Exception("Unexpected settings container size");
        string[] names={"SettingsOutline","LocalOnly","Vehicles","Ambient","UsageDivider"};
        Vector2[] positions={Vector2.zero,new Vector2(-176,48),new Vector2(-194,-15),new Vector2(-22,-15),new Vector2(84,-11)};
        for(int i=0;i<names.Length;i++)
        {
            var rect=footer.Find(names[i]) as RectTransform;if(!rect)throw new Exception("Missing "+names[i]);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=positions[i];Record(rect);
        }
    }
    private static void CheckContent(SpeakerMap map)
    {
        var canvas=(RectTransform)map.transform.Find("MapCanvas");
        var footer=(RectTransform)canvas.Find("LocalSettings");
        foreach(var rect in footer.GetComponentsInChildren<RectTransform>(false))
            foreach(var corner in Corners(rect))
            {
                Vector2 point=canvas.InverseTransformPoint(corner);
                if(!canvas.rect.Contains(point))throw new Exception("Active UI outside board: "+rect.name+" "+point);
            }
        var panel=map.GetComponent<SpeakerMapPanel>();
        var rects=footer.GetComponentsInChildren<RectTransform>(false);var before=rects.Select(Corners).ToArray();
        panel._RefreshSettings();panel._OnWorldLanguageChanged();
        for(int i=0;i<rects.Length;i++)for(int j=0;j<4;j++)if(Vector3.Distance(before[i][j],Corners(rects[i])[j])>.0001f)throw new Exception("Refresh moved UI: "+rects[i].name);
        SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+map.name+"-bounds-repaired-layout.txt");
        SpeakerMapSetup.CapturePanel(canvas,Output+"/"+map.name+"-bounds-repaired.png");
    }
    private static void Repair()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected TEST");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();if(maps.Length!=2)throw new Exception("Expected two maps");
        const string path="Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        // Save the prefab FIRST. Old per-instance overrides must be corrected afterwards.
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try{Normalize((RectTransform)prefab.transform.Find("MapCanvas/LocalSettings"));PrefabUtility.SaveAsPrefabAsset(prefab,path);}
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
        foreach(var map in maps)
        {
            var footer=(RectTransform)map.transform.Find("MapCanvas/LocalSettings");
            Undo.RegisterFullObjectHierarchyUndo(footer.gameObject,"Repair compact settings coordinate overrides");
            Normalize(footer);
        }
        Undo.FlushUndoRecordObjects();
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        File.WriteAllText(Output+"/settings-bounds-verify.request","Verify after prefab import, including across assembly reload.");
    }
    private static void VerifyRepair()
    {
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/_Shinjuku/Scenes/TEST_PC.unity")throw new Exception("Expected TEST");
        var maps=UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene).ToArray();if(maps.Length!=2)throw new Exception("Expected two maps");
        foreach(var map in maps)CheckContent(map);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/settings-bounds-repair.done",DateTime.Now+" PASS: prefab saved/reimported before final live verification; both maps' active settings inside board, refresh/language callbacks retain layout; recovery saved, original TEST unsaved.");
    }
    private static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new Exception("Expected active TEST scene");
        var maps = UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m => m.gameObject.scene == scene).ToArray();
        if (maps.Length != 2) throw new Exception("Expected two maps");
        var report = new System.Text.StringBuilder();
        foreach (var map in maps)
        {
            var canvas = (RectTransform)map.transform.Find("MapCanvas");
            var footer = (RectTransform)canvas.Find("LocalSettings");
            var outside = canvas.GetComponentsInChildren<RectTransform>(true).Where(r => r != footer && !r.IsChildOf(footer)).ToArray();
            var outsideCorners = outside.Select(Corners).ToArray();
            string bindings = EditorJsonUtility.ToJson(map);
            string panel = EditorJsonUtility.ToJson(map.GetComponent<SpeakerMapPanel>());
            SpeakerMapSetup.CapturePanel(canvas, Output + "/" + map.name + "-bounds-before.png");
            Undo.RegisterFullObjectHierarchyUndo(footer.gameObject,"Fit settings container to content");
            Fit(footer);
            for (int i = 0; i < outside.Length; i++)
                for (int j = 0; j < 4; j++) if (Vector3.Distance(outsideCorners[i][j],Corners(outside[i])[j]) > .0001f) throw new Exception("Unrelated layout changed");
            foreach (var corner in Corners(footer)) if (!canvas.rect.Contains((Vector2)canvas.InverseTransformPoint(corner))) throw new Exception("Settings container remains outside board");
            if (bindings != EditorJsonUtility.ToJson(map) || panel != EditorJsonUtility.ToJson(map.GetComponent<SpeakerMapPanel>())) throw new Exception("Runtime bindings changed");
            SpeakerMapSetup.CapturePanel(canvas, Output + "/" + map.name + "-bounds-after.png");
            SpeakerMapSetup.VerifyPresentation(canvas, Output + "/" + map.name + "-bounds-layout.txt");
            report.AppendLine(map.name + " PASS: container=" + footer.rect.size + "; position=" + footer.anchoredPosition + "; content/click rectangles, other UI and runtime bindings unchanged; inside board.");
        }
        const string path = "Assets/_Shinjuku/UI/SpeakerMap/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { Fit((RectTransform)prefab.transform.Find("MapCanvas/LocalSettings")); PrefabUtility.SaveAsPrefabAsset(prefab,path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene,Output + "/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output + "/settings-bounds.done",DateTime.Now + "\n" + report + "Prefab and recovery saved; original TEST unsaved.");
    }
}
