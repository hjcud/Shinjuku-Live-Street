using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestInteriorAudit
{
    const string Folder = "output/quest-optimization-20260913/interior";
    static QuestInteriorAudit() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Folder + "/audit.request") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "/audit.request");
        try { Run(); } catch (Exception e) { File.WriteAllText(Folder + "/audit.failed", e.ToString()); }
    }
    static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity") throw new InvalidOperationException("Quest scene required.");
        var view = SceneView.lastActiveSceneView;
        if (!view) throw new InvalidOperationException("Scene view required.");
        var rs = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
        var report = new List<string> { "Pivot=" + view.pivot + " Rotation=" + view.rotation.eulerAngles + " Size=" + view.size };
        foreach (var r in rs.Where(r => r.enabled && r.gameObject.activeInHierarchy))
            report.Add("Renderer=" + PathOf(r.transform) + " Bounds=" + r.bounds + " LM=" + r.lightmapIndex + " ST=" + r.lightmapScaleOffset
                + " Materials=" + string.Join(";", r.sharedMaterials.Where(m => m).Select(m => AssetDatabase.GetAssetPath(m) + " [" + m.shader.name + "]")));
        QuestBlackSurfaceAudit.Capture(view.camera, Folder + "/01-before.png");
        CaptureInterior("north-before", new Vector3(20, 8.5f, -27), new Vector3(12, 8.5f, -39), report);
        CaptureInterior("south-before", new Vector3(20, 8.5f, 26), new Vector3(12, 8.5f, 38), report);
        CaptureInterior("gate-before", new Vector3(27, 8.5f, 29), new Vector3(19, 8.5f, 39), report);
        var indices = rs.ToDictionary(r => r, r => r.lightmapIndex);
        try
        {
            foreach (var r in rs.Where(r => r.sharedMaterials.Any(m => m && m.shader.name == "VRChat/Mobile/Lightmapped"))) r.lightmapIndex = -1;
            QuestBlackSurfaceAudit.Capture(view.camera, Folder + "/02-no-lightmaps.png");
            CaptureInterior("north-no-lm", new Vector3(20, 8.5f, -27), new Vector3(12, 8.5f, -39), null);
            CaptureInterior("south-no-lm", new Vector3(20, 8.5f, 26), new Vector3(12, 8.5f, 38), null);
            CaptureInterior("gate-no-lm", new Vector3(27, 8.5f, 29), new Vector3(19, 8.5f, 39), null);
        }
        finally { foreach (var p in indices) p.Key.lightmapIndex = p.Value; }
        var savedMaterials = rs.ToDictionary(r => r, r => r.sharedMaterials);
        var replacements = new Dictionary<Material,Material>();
        try
        {
            foreach (string pair in new[] { "2-UV B-4096 2048|1e8ce972", "2-UV B-4096 2048.001|677b9b9a", "1-UV S-2048|41101075", "1-UV S-2048.001|c6f2f9cc", "1-UV W-4096|fa9cd06e" })
            {
                var parts=pair.Split('|');
                var quest=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Shinjuku/Quest/GeneratedMaterials/"+parts[0]+"_"+parts[1]+".mat");
                var pc=AssetDatabase.LoadAssetAtPath<Material>("Assets/model/Materials/"+parts[0]+".mat");
                if(quest && pc) replacements[quest]=pc;
            }
            CaptureInterior("pillars-before", new Vector3(-10,8.5f,20),new Vector3(-13,8.5f,34),null);
            CaptureInterior("north-gate-before", new Vector3(35,8.6f,-23),new Vector3(46,8.6f,-38),report);
            foreach(var p in savedMaterials)p.Key.sharedMaterials=p.Value.Select(m=>m && replacements.ContainsKey(m)?replacements[m]:m).ToArray();
            CaptureInterior("south-original-materials", new Vector3(20, 8.5f, 26), new Vector3(12, 8.5f, 38), null);
            CaptureInterior("pillars-original-materials", new Vector3(-10,8.5f,20),new Vector3(-13,8.5f,34),null);
            CaptureInterior("north-gate-original-materials", new Vector3(35,8.6f,-23),new Vector3(46,8.6f,-38),null);
        }
        finally {foreach(var p in savedMaterials)p.Key.sharedMaterials=p.Value;}
        File.WriteAllLines(Folder + "/audit.done", report);
        SceneView.RepaintAll();
    }
    static void CaptureInterior(string name, Vector3 position, Vector3 target, List<string> report)
    {
        var go = new GameObject("Interior audit camera") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var c = go.AddComponent<Camera>(); c.enabled = false; c.fieldOfView = 65f; c.aspect = 1280f/720;
            c.transform.position = position; c.transform.LookAt(target);
            QuestBlackSurfaceAudit.Capture(c, Folder + "/" + name + ".png");
            if (report != null)
            {
                var method = typeof(HandleUtility).GetMethod("IntersectRayMesh", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var renderers = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
                foreach (var sample in new[] { new Vector2(.75f,.50f), new Vector2(.9f,.50f), new Vector2(.5f,.5f), new Vector2(.7f,.3f) })
                {
                    var ray=c.ViewportPointToRay(sample); float nearest=40f; string description="No mesh hit";
                    foreach(var r in renderers)
                    {
                        float d; if(!r.bounds.IntersectRay(ray,out d)||d>nearest) continue;
                        var mf=r.GetComponent<MeshFilter>(); if(!mf||!mf.sharedMesh||method==null) continue;
                        object[] args={ray,mf.sharedMesh,r.localToWorldMatrix,new RaycastHit()};
                        if(!(bool)method.Invoke(null,args))continue; var h=(RaycastHit)args[3]; if(h.distance>=nearest)continue;
                        nearest=h.distance; int tri=h.triangleIndex*3,slot=0;
                        for(int j=0;j<mf.sharedMesh.subMeshCount;j++) {int count=(int)mf.sharedMesh.GetIndexCount(j);if(tri<count){slot=j;break;}tri-=count;}
                        var m=r.sharedMaterials[Mathf.Min(slot,r.sharedMaterials.Length-1)];
                        description=PathOf(r.transform)+" Slot="+slot+" Material="+AssetDatabase.GetAssetPath(m)+" UV="+h.textureCoord+" Point="+h.point;
                    }
                    report.Add(name+" MeshPick="+sample+" "+description);
                }
                for (int y = 1; y < 5; y++) for (int x = 1; x < 8; x++)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(c.ViewportPointToRay(new Vector3(x/8f,y/5f,0)), out hit, 40f))
                        report.Add(name + " Ray=" + x + "," + y + " Hit=" + PathOf(hit.transform) + " Point=" + hit.point);
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static string PathOf(Transform t) { return t.parent ? PathOf(t.parent) + "/" + t.name : t.name; }
}
