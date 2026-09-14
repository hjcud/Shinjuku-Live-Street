using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit request only. Never opens/replaces the user's active scene.
[InitializeOnLoad]
public static class QuestSafeShaderCleanup
{
    const string Folder = "output/quest-shader-safe-cleanup-20260914";
    const string Quest = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string Pc = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    static QuestSafeShaderCleanup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string apply=Folder+"/apply.request";
        if(File.Exists(apply))
        {
            File.Delete(apply);
            try { Apply(); } catch(Exception e) { File.WriteAllText(Folder+"/apply.failed",e.ToString());Debug.LogException(e); }
            return;
        }
        string request=Folder+"/audit.request";
        if(!File.Exists(request)) return;
        File.Delete(request);
        try { Audit(); } catch(Exception e) { File.WriteAllText(Folder+"/audit.failed",e.ToString()); }
    }
    static void Audit()
    {
        var lines=new List<string>{"ActiveScene="+SceneManager.GetActiveScene().path};
        for(int i=0;i<SceneManager.sceneCount;i++) { var s=SceneManager.GetSceneAt(i);lines.Add("Loaded="+s.path+" dirty="+s.isDirty); }
        var preview=SceneManager.GetSceneByPath(Quest);
        bool opened=!preview.IsValid()||!preview.isLoaded;
        if(opened)preview=EditorSceneManager.OpenScene(Quest,OpenSceneMode.Additive);
        try
        {
            var roots=preview.GetRootGameObjects();
            var renderers=roots.SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var materials=EditorUtility.CollectDependencies(roots).OfType<Material>().Where(m=>m&&m.shader).Distinct().ToArray();
            foreach(var group in materials.GroupBy(m=>m.shader.name).OrderBy(g=>g.Key))
            {
                lines.Add("SHADER "+group.Key+" materials="+group.Count());
                foreach(var m in group)
                {
                    var users=renderers.Where(r=>r.sharedMaterials.Contains(m)).ToArray();
                    if(!AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Shinjuku/Quest/"))continue;
                    lines.Add(" MAT "+AssetDatabase.GetAssetPath(m)+" users="+users.Length+" keywords="+string.Join(",",m.shaderKeywords));
                    foreach(var r in users.Take(2))lines.Add("  USER "+r.name+" active="+r.gameObject.activeInHierarchy+" lightmap="+r.lightmapIndex);
                    var so=new SerializedObject(m);var tex=so.FindProperty("m_SavedProperties.m_TexEnvs");
                    for(int j=0;j<tex.arraySize;j++)
                    {
                        var p=tex.GetArrayElementAtIndex(j);string n=p.FindPropertyRelative("first").stringValue;
                        var t=p.FindPropertyRelative("second.m_Texture").objectReferenceValue;
                        if(t&&!m.HasProperty(n))lines.Add("  STALE_TEXTURE "+n+" -> "+AssetDatabase.GetAssetPath(t));
                    }
                }
            }
            File.WriteAllLines(Folder+"/audit.txt",lines);
        }
        finally {if(opened)EditorSceneManager.CloseScene(preview,true);}
    }
    static string Hash(string path)
    {
        using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream));
    }
    static int ClearUnusedTextures(Material mat,List<string> changes)
    {
        var so=new SerializedObject(mat);var a=so.FindProperty("m_SavedProperties.m_TexEnvs");int count=0;
        for(int i=a.arraySize-1;i>=0;i--)
        {
            var e=a.GetArrayElementAtIndex(i);string key=e.FindPropertyRelative("first").stringValue;
            var value=e.FindPropertyRelative("second.m_Texture");
            if(mat.HasProperty(key)||!value.objectReferenceValue)continue;
            changes.Add(mat.name+" | "+key+" | "+AssetDatabase.GetAssetPath(value.objectReferenceValue));
            // Keep old scalar data, but disconnect textures the current shader cannot sample.
            value.objectReferenceValue=null;count++;
        }
        if(count>0)so.ApplyModifiedPropertiesWithoutUndo();
        return count;
    }
    static byte[] Capture(Scene scene,string path,bool sign)
    {
        var go=new GameObject("Quest safe cleanup verification") {hideFlags=HideFlags.HideAndDontSave};
        SceneManager.MoveGameObjectToScene(go,scene);
        var camera=go.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
        camera.fieldOfView=55;camera.nearClipPlane=.1f;camera.farClipPlane=1500;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.025f,.035f);
        if(sign){camera.transform.position=new Vector3(15,12,10);camera.transform.LookAt(new Vector3(23,12,24));}
        else {camera.transform.position=new Vector3(-5,19,-43);camera.transform.LookAt(new Vector3(18,10,22));}
        var rt=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var prior=RenderTexture.active;Texture2D tex=null;
        try
        {
            camera.targetTexture=rt;camera.aspect=1280f/720f;camera.Render();RenderTexture.active=rt;
            tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
            byte[] png=tex.EncodeToPNG();File.WriteAllBytes(path,png);return png;
        }
        finally {RenderTexture.active=prior;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(go);}
    }
    static bool ComparePixels(byte[] a,byte[] b,string label,List<string> report)
    {
        var x=new Texture2D(2,2);var y=new Texture2D(2,2);
        try
        {
            x.LoadImage(a);y.LoadImage(b);var p=x.GetPixels32();var q=y.GetPixels32();
            if(p.Length!=q.Length)return false;
            long sum=0;int changed=0,max=0;
            for(int i=0;i<p.Length;i++)
            {
                int r=Math.Abs(p[i].r-q[i].r),g=Math.Abs(p[i].g-q[i].g),z=Math.Abs(p[i].b-q[i].b);
                sum+=r+g+z;if(r+g+z>0)changed++;max=Math.Max(max,Math.Max(r,Math.Max(g,z)));
            }
            report.Add(label+" ChangedPixels="+changed+"/"+p.Length+" MaxChannelDelta="+max+" MeanChannelDelta="+((double)sum/(p.Length*3)));
            // Permit only sparse one-level 8-bit quantization differences, never a visible change.
            return max<=1&&changed<=p.Length/1000;
        }
        finally {UnityEngine.Object.DestroyImmediate(x);UnityEngine.Object.DestroyImmediate(y);}
    }
    static void Apply()
    {
        var pcDependencies=new HashSet<string>(AssetDatabase.GetDependencies(Pc,true));
        var protectedFiles=pcDependencies.Where(p=>p.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)).Concat(new[]{Pc,Quest}).Distinct().ToDictionary(p=>p,Hash);
        var active=SceneManager.GetActiveScene();bool dirty=active.isDirty;
        var scene=SceneManager.GetSceneByPath(Quest);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(Quest,OpenSceneMode.Additive);
        var originals=new Dictionary<Material,Material>();var changes=new List<string>();
        string backup=Folder+"/backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backup);
        bool saved=false;
        try
        {
            var world=scene.GetRootGameObjects().Single(r=>r.name=="00_World");
            var candidates=world.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                .Where(m=>m&&m.shader.name=="VRChat/Mobile/Lightmapped"&&AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Shinjuku/Quest/GeneratedMaterials/",StringComparison.Ordinal)).Distinct().ToArray();
            foreach(var mat in candidates)if(pcDependencies.Contains(AssetDatabase.GetAssetPath(mat)))throw new InvalidOperationException("Shared PC material: "+mat.name);
            var beforeDeps=new HashSet<string>(AssetDatabase.GetDependencies(candidates.Select(AssetDatabase.GetAssetPath).ToArray(),true));
            var before=Capture(scene,Folder+"/before-street.png",false);
            var beforeSign=Capture(scene,Folder+"/before-sign.png",true);
            foreach(var mat in candidates)
            {
                var original=new Material(mat){hideFlags=HideFlags.HideAndDontSave};
                string main=AssetDatabase.GetAssetPath(mat.mainTexture);var scale=mat.mainTextureScale;var offset=mat.mainTextureOffset;
                if(ClearUnusedTextures(mat,changes)==0){UnityEngine.Object.DestroyImmediate(original);continue;}
                originals.Add(mat,original);
                File.Copy(AssetDatabase.GetAssetPath(mat),backup+"/"+Path.GetFileName(AssetDatabase.GetAssetPath(mat)),false);
                if(AssetDatabase.GetAssetPath(mat.mainTexture)!=main||mat.mainTextureScale!=scale||mat.mainTextureOffset!=offset||mat.shader!=original.shader)
                    throw new InvalidOperationException("Active shader property changed: "+mat.name);
            }
            var after=Capture(scene,Folder+"/after-street.png",false);
            var afterSign=Capture(scene,Folder+"/after-sign.png",true);
            var comparisons=new List<string>();
            bool streetMatch=ComparePixels(before,after,"Street",comparisons);
            bool signMatch=ComparePixels(beforeSign,afterSign,"Sign",comparisons);
            File.WriteAllLines(Folder+"/pixel-comparison.txt",comparisons);
            if(!streetMatch||!signMatch)throw new InvalidOperationException("Rendered pixels changed beyond sparse 1/255 rounding tolerance; refusing to save.");
            foreach(var p in protectedFiles)if(Hash(p.Key)!=p.Value)throw new InvalidOperationException("Protected file changed: "+p.Key);
            foreach(var mat in originals.Keys)AssetDatabase.SaveAssetIfDirty(mat);
            saved=true;
            var afterDeps=new HashSet<string>(AssetDatabase.GetDependencies(candidates.Select(AssetDatabase.GetAssetPath).ToArray(),true));
            var removed=beforeDeps.Except(afterDeps).ToArray();
            var report=new List<string>{"ChangedMaterials="+originals.Count,"RemovedUnusedTextureReferences="+changes.Count,
                "StreetAndSignVisualComparison=Passed","ProtectedFilesUnchanged="+protectedFiles.Count,
                "DetachedMaterialDependencies="+removed.Length,"Backup="+backup,"ShaderChanges=0","SceneChanges=0"};
            report.AddRange(comparisons);report.AddRange(removed.Select(p=>"DETACHED "+p));report.AddRange(changes);
            File.WriteAllLines(Folder+"/apply.done",report);
        }
        finally
        {
            if(!saved)foreach(var p in originals){EditorUtility.CopySerialized(p.Value,p.Key);AssetDatabase.SaveAssetIfDirty(p.Key);}
            foreach(var m in originals.Values)UnityEngine.Object.DestroyImmediate(m);
            if(opened)EditorSceneManager.CloseScene(scene,true);
            if(SceneManager.GetActiveScene()!=active)SceneManager.SetActiveScene(active);
            if(active.isDirty!=dirty)Debug.LogWarning("Active scene dirty state changed during audit; no scene saved.");
        }
    }
}
