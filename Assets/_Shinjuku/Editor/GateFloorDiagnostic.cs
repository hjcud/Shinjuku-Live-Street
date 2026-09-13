using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GateFloorDiagnostic
{
    const string Output="output/gate-floor-diagnostic/";
    static GateFloorDiagnostic(){EditorApplication.update+=Poll;}
    static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string request=Output+"inspect-v3.request";if(!File.Exists(request))return;File.Delete(request);
        try{Run();}catch(Exception e){File.WriteAllText(Output+"inspect.failed",e.ToString());}
    }
    static void Run()
    {
        var scene=SceneManager.GetActiveScene();var report=new StringBuilder();
        report.AppendLine("scene="+scene.path+" dirty="+scene.isDirty);
        report.AppendLine("selection="+(Selection.activeTransform?Path(Selection.activeTransform):"none"));
        var view=SceneView.lastActiveSceneView;
        if(view)report.AppendLine("view camera="+view.camera.transform.position+" rotation="+view.camera.transform.eulerAngles+" pivot="+view.pivot);
        var seen=new System.Collections.Generic.HashSet<Renderer>();
        if(Selection.activeTransform)foreach(var r in Selection.activeTransform.GetComponentsInChildren<Renderer>(true))if(seen.Add(r))Describe(r,report);
        foreach(var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m=>m.gameObject.scene==scene))
        {
            Vector3 center=map.transform.Find("MapCanvas").position;report.AppendLine("MAP "+map.name+" pos="+center);
            for(int x=-10;x<=10;x+=2)for(int z=-10;z<=10;z+=2)
            {
                var origin=new Vector3(center.x+x,center.y+.3f,center.z+z);
                var hits=Physics.RaycastAll(origin,Vector3.down,5,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.8f).OrderBy(h=>h.distance).ToArray();
                if(hits.Length==0)continue;
                var hit=hits[0];var renderer=hit.collider.GetComponent<Renderer>();
                if(renderer&&seen.Add(renderer))Describe(renderer,report);
                report.AppendLine("HIT "+hit.point.ToString("F3")+" "+Path(hit.collider.transform)+" tri="+hit.triangleIndex+" uv="+hit.textureCoord+" uv2="+hit.lightmapCoord);
                if(hits.Length>1 && hits[1].distance-hit.distance<.05f)report.AppendLine("OVERLAP "+Path(hits[1].collider.transform)+" delta="+(hits[1].distance-hit.distance));
            }
        }
        File.WriteAllText(Output+"inspect.txt",report.ToString());
        InspectTriangles(scene);
    }
    static void InspectTriangles(Scene scene)
    {
        var report=new StringBuilder();
        var renderers=UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true).Where(r=>r.gameObject.scene==scene&&r.enabled&&r.gameObject.activeInHierarchy&&r.bounds.min.y<7.1f&&r.bounds.max.y>6.5f&&r.bounds.min.x<26&&r.bounds.max.x>4&&r.bounds.min.z<47&&r.bounds.max.z>26).ToArray();
        foreach(var r in renderers)report.AppendLine("CANDIDATE "+Path(r.transform)+" bounds="+r.bounds);
        var ground=renderers.Single(r=>r.name=="땅");var mesh=ground.GetComponent<MeshFilter>().sharedMesh;
        var v=mesh.vertices.Select(p=>ground.transform.TransformPoint(p)).ToArray();var uv=mesh.uv;
        for(float x=5;x<=25;x+=1)for(float z=27;z<=46;z+=1)
        {
            int count=0;
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                var tr=mesh.GetTriangles(s);
                for(int i=0;i<tr.Length;i+=3)
                {
                    int ia=tr[i],ib=tr[i+1],ic=tr[i+2];var a=v[ia];var b=v[ib];var c=v[ic];
                    var ab=b-a;var ac=c-a;float det=ab.x*ac.z-ab.z*ac.x;if(Mathf.Abs(det)<.0001f)continue;
                    float u=((x-a.x)*ac.z-(z-a.z)*ac.x)/det,w=(ab.x*(z-a.z)-ab.z*(x-a.x))/det;
                    if(u<.0001f||w<.0001f||u+w>.9999f)continue;
                    float y=a.y+u*ab.y+w*ac.y;if(y<6.5f||y>7.1f)continue;
                    var mat=ground.sharedMaterials[s];var du=uv[ib]-uv[ia];var dv=uv[ic]-uv[ia];
                    var dx=(du*ac.z-dv*ab.z)/det;var dz=(dv*ab.x-du*ac.x)/det;
                    var tile=mat.mainTextureScale;dx=Vector2.Scale(dx,tile);dz=Vector2.Scale(dz,tile);
                    report.AppendLine("SURFACE x="+x+" z="+z+" y="+y.ToString("F5")+" sub="+s+" mat="+mat.name+" UV/m="+dx.magnitude.ToString("F3")+","+dz.magnitude.ToString("F3"));count++;
                }
            }
            if(count>1)report.AppendLine("GROUND_OVERLAP x="+x+" z="+z+" count="+count);
        }
        File.WriteAllText(Output+"triangles.txt",report.ToString());
    }
    static void Describe(Renderer r,StringBuilder b)
    {
        b.AppendLine("RENDERER "+Path(r.transform)+" bounds="+r.bounds+" scale="+r.transform.lossyScale+" lightmap="+r.lightmapIndex+" ST="+r.lightmapScaleOffset+" shadows="+r.shadowCastingMode+" receive="+r.receiveShadows);
        var filter=r.GetComponent<MeshFilter>();if(filter&&filter.sharedMesh)b.AppendLine("MESH "+AssetDatabase.GetAssetPath(filter.sharedMesh)+" name="+filter.sharedMesh.name+" submeshes="+filter.sharedMesh.subMeshCount+" verts="+filter.sharedMesh.vertexCount);
        foreach(var m in r.sharedMaterials)
        {
            if(!m)continue;
            b.AppendLine("MAT "+AssetDatabase.GetAssetPath(m)+" shader="+m.shader.name+" queue="+m.renderQueue);
            foreach(string p in new[]{"_Color","_BaseColor","_EmissionColor"})if(m.HasProperty(p))b.AppendLine(p+"="+m.GetColor(p));
            foreach(string p in new[]{"_MainTex","_BaseMap","_BumpMap","_OcclusionMap","_EmissionMap"})if(m.HasProperty(p))b.AppendLine(p+"="+AssetDatabase.GetAssetPath(m.GetTexture(p))+" scale="+m.GetTextureScale(p)+" offset="+m.GetTextureOffset(p));
        }
    }
}
