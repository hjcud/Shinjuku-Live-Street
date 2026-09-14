using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestSignFaceFix
{
    const string Folder = "output/quest-optimization-20260913/sign-face";
    const string Quest = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string Pc = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string MatPath = "Assets/_Shinjuku/Quest/GeneratedMaterials/2-Shinjuku-1024_6b404c5f.mat";
    const string EmissionPath = "Assets/model/Texture_Main/2-Shinjuku-1024_Emission.png";
    static QuestSignFaceFix() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Folder + "/apply.request") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "/apply.request");
        try { Apply(); } catch (Exception e) { File.WriteAllText(Folder + "/apply.failed", e.ToString()); Debug.LogException(e); }
    }
    static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != Quest) throw new InvalidOperationException("Open TEST_Quest first.");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath);
        if (!mat || !emission || mat.shader.name != "VRChat/Mobile/Lightmapped") throw new InvalidOperationException("Unexpected sign assets.");
        var signs = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).Where(r=>r.sharedMaterials.Contains(mat)).ToArray();
        if (signs.Length != 1 || signs[0].lightmapIndex != -1) throw new InvalidOperationException("Sign must retain the verified baked-lightmap exclusion.");
        var paths = AssetDatabase.GetDependencies(Pc, true).Where(p=>p.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))
            .Concat(new[]{Pc,Quest,EmissionPath,EmissionPath+".meta"}).Distinct().ToDictionary(p=>p,Hash);
        if (paths.ContainsKey(MatPath)) throw new InvalidOperationException("Quest material is referenced by PC.");
        File.Copy(MatPath, Folder + "/material-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mat", false);
        Capture(signs[0].bounds, "before");
        // The original emission atlas encodes the intended luminous faces (white/green)
        // and the non-luminous dark extruded sides. Reuse it directly; no PBR or new shader.
        mat.SetTexture("_MainTex", emission);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        Capture(signs[0].bounds, "after");
        foreach (var p in paths) if (Hash(p.Key)!=p.Value) throw new InvalidOperationException("Protected file changed: " + p.Key);
        File.WriteAllText(Folder + "/apply.done", "ChangedMaterial="+MatPath+"\nMainTexture="+EmissionPath+"\nProtectedFilesUnchanged="+paths.Count+"\nSignLightmap="+signs[0].lightmapIndex);
        SceneView.RepaintAll();
    }
    static void Capture(Bounds bounds, string phase)
    {
        var go = new GameObject("Sign face verification camera") {hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var c=go.AddComponent<Camera>();c.enabled=false;c.fieldOfView=50;
            c.transform.position=bounds.center+new Vector3(-3,1.4f,-15);c.transform.LookAt(bounds.center);
            QuestBlackSurfaceAudit.Capture(c,Folder+"/"+phase+"-front.png");
            Vector3 target=bounds.center+new Vector3(-5,0,0);
            c.transform.position=target+new Vector3(-5,-1.2f,-6);c.transform.LookAt(target);
            QuestBlackSurfaceAudit.Capture(c,Folder+"/"+phase+"-angle.png");
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    static string Hash(string path)
    {
        using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
    }
}
