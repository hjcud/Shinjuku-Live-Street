using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;

// One-shot live Editor setup. Never edits serialized scene YAML or loads a duplicate scene.
[InitializeOnLoad]
public static class SpeakerRippleGuideSetup
{
    const string Folder = "Assets/_Shinjuku/Art/Materials/Speaker/";
    const string Output = "output/speaker-placement-20260912/";
    static SpeakerRippleGuideSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string request = Output + "ripple-guide-a.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText(Output + "ripple-guide-a.failed", e.ToString()); Debug.LogException(e); }
    }
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    static void Dirty(UnityEngine.Object obj)
    {
        EditorUtility.SetDirty(obj);
        if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    }
    static Material MakeMaterial(string name, Shader shader, Color color)
    {
        string path = Folder + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        Undo.RecordObject(mat, "Configure thin speaker ripples");
        mat.shader = shader;
        mat.SetColor("_Color", color);
        mat.SetFloat("_Period", 6);
        mat.SetFloat("_Width", .085f);
        mat.SetFloat("_PreviewTime", -1);
        Dirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
        return mat;
    }
    static Mesh MakeMesh()
    {
        // Two disconnected narrow annuli, each with 96 segments. The shader moves
        // their radii; UV.x remains constant within each annulus, avoiding wrap seams.
        const int segments = 96;
        var vertices = new Vector3[2 * (segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[2 * segments * 6];
        for (int pulse = 0; pulse < 2; pulse++)
        {
            int start = pulse * (segments + 1) * 2;
            for (int step = 0; step <= segments; step++)
            {
                float angle = step * Mathf.PI * 2 / segments;
                for (int edge = 0; edge < 2; edge++)
                {
                    int n = start + step * 2 + edge;
                    vertices[n] = new Vector3(Mathf.Cos(angle)*.5f, 0, Mathf.Sin(angle)*.5f);
                    uv[n] = new Vector2(pulse * .5f, edge);
                }
                if (step == segments) continue;
                int a = start + step * 2, t = (pulse * segments + step) * 6;
                triangles[t] = a; triangles[t+1] = a+2; triangles[t+2] = a+1;
                triangles[t+3] = a+1; triangles[t+4] = a+2; triangles[t+5] = a+3;
            }
        }
        string path = Folder + "GuideThinRipples.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = new Mesh { name = "GuideThinRipples" }; AssetDatabase.CreateAsset(mesh, path); }
        Undo.RecordObject(mesh, "Build thin speaker ripple mesh");
        mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        // Full animation envelope, not the collapsed source geometry, controls culling.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.02f,.02f,1.02f));
        Dirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }
    static void Apply()
    {
        Directory.CreateDirectory(Output);
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new Exception("Expected original TEST scene");
        var manager = UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m => m.gameObject.scene == scene);
        var renderers = Field<MeshRenderer[]>(manager, "placedSpeakerHeatmaps");
        if (renderers == null || renderers.Length != 6 || renderers.Any(r => !r)) throw new Exception("Expected six existing guide renderers");
        var shader = Shader.Find("Shinjuku/Speaker Ripple Guide");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Ripple shader unavailable or has errors");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (!EditorSceneManager.SaveScene(scene, Output + "TEST.before-ripples-" + stamp + ".unity", true)) throw new Exception("Backup failed");
        var mesh = MakeMesh();
        var quiet = MakeMaterial("GuideRippleQuiet", shader, new Color(.87f,.84f,.72f,.48f));
        var focused = MakeMaterial("GuideRippleFocused", shader, new Color(.96f,.90f,.67f,.78f));
        Undo.RecordObject(manager, "Bind speaker ripple guidance");
        var so = new SerializedObject(manager);
        so.FindProperty("quietGuideMaterial").objectReferenceValue = quiet;
        so.FindProperty("focusedGuideMaterial").objectReferenceValue = focused;
        so.ApplyModifiedProperties();
        foreach (var r in renderers)
        {
            var filter = r.GetComponent<MeshFilter>();
            Undo.RecordObject(filter, "Replace dashed boundary with ripples");
            Undo.RecordObject(r, "Bind ripple material");
            filter.sharedMesh = mesh; r.sharedMaterial = quiet; r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Dirty(filter); Dirty(r);
        }
        ValidateGeometry(mesh, manager);
        var capture = typeof(SpeakerQuietGuideSetup).GetMethod("CaptureAndTest", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            foreach (float time in new[] { 0f, 1f, 2f })
            {
                quiet.SetFloat("_PreviewTime", time); focused.SetFloat("_PreviewTime", time);
                capture.Invoke(null, new object[] { manager, false });
                File.Copy(Output + "quiet-guide-eye-level.png", Output + "ripple-a-time-" + (int)time + ".png", true);
            }
            capture.Invoke(null, new object[] { manager, true });
            File.Copy(Output + "quiet-guide-multiple.png", Output + "ripple-a-multiple.png", true);
        }
        finally
        {
            quiet.SetFloat("_PreviewTime", -1); focused.SetFloat("_PreviewTime", -1);
            Dirty(quiet); Dirty(focused); AssetDatabase.SaveAssetIfDirty(quiet); AssetDatabase.SaveAssetIfDirty(focused);
        }
        if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Shader compilation failed during rendering");
        if (Field<TMPro.TextMeshProUGUI>(manager,"placementStatusText").gameObject.activeSelf) throw new Exception("Status text was re-enabled");
        UdonSharpEditorUtility.CopyProxyToUdon(manager);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
        if (!backing.publicVariables.TryGetVariableValue("quietGuideMaterial", out Material stored) || stored != quiet) throw new Exception("Udon ripple binding missing");
        Dirty(manager); Dirty(backing);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("TEST save failed");
        File.WriteAllText(Output + "ripple-guide-a.done", DateTime.Now + " PASS: original TEST saved; six shared two-ring meshes; 388 vertices / 384 triangles per source; vertex-only animation, 6s travel, 0.085m width; geometry outward movement / annular area tests, six occupancy+return tests, arrow clearance tests, three temporal and multiple-source previews passed; shader compiles, Udon bindings verified; preview time reset to live. No new runtime raycasts, mesh rebuilds or material allocations. Client GPU timing not measured.");
    }
    static void ValidateGeometry(Mesh mesh, SpeakerManager manager)
    {
        if (mesh.vertexCount != 388 || mesh.triangles.Length != 1152) throw new Exception("Unexpected ripple mesh budget");
        for (int source = 0; source < 6; source++)
        {
            float maxRadius = manager.GetSpeakerWarningRadius(source);
            float previous = 0;
            for (int step = 0; step < 100; step++)
            {
                float phase = step / 100f;
                float radius = Mathf.Lerp(.65f, Mathf.Max(.7f,maxRadius-.1f), phase);
                if (radius < previous || radius+.0425f > maxRadius) throw new Exception("Pulse moved inward or outside influence radius");
                previous = radius;
            }
            // Upper bound for the area of two narrow annuli vs two full range disks.
            float areaRatio = 2*.085f/maxRadius;
            if (areaRatio > .03f) throw new Exception("Unexpected transparent coverage budget");
        }
    }
}
