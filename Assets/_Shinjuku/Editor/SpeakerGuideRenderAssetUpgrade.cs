using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpeakerGuideRenderAssetUpgrade
{
    const string Request = "output/speaker-guide-render-20260914/apply-v1.request";
    const string PreviewRequest = "output/speaker-guide-render-20260914/preview-v1.request";
    const string CleanupRequest = "output/speaker-guide-render-20260914/cleanup-v1.request";
    const string Output = "output/speaker-guide-render-20260914/";
    const string Folder = "Assets/_Shinjuku/Art/Materials/Speaker/";
    const string TexturePath = Folder + "GuideSpeakerBadgeAlpha.png";
    const string BadgeMeshPath = Folder + "GuideSpeakerBadgeQuad.asset";
    const string RingMeshPath = Folder + "GuideGroundRingContinuous.asset";
    const string BadgeMaterialPath = Folder + "GuideSpeakerBadgeTexture.mat";
    static readonly string[] Scenes =
    {
        "Assets/_Shinjuku/Scenes/TEST_PC.unity",
        "Assets/_Shinjuku/Scenes/TEST_Quest.unity",
        "Assets/_ShinjukuExhibition/Scenes/TechnicalExhibition.unity"
    };

    static SpeakerGuideRenderAssetUpgrade() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(CleanupRequest))
        {
            File.Delete(CleanupRequest);
            Directory.CreateDirectory(Output);
            try { UpgradeChevronAndCleanup(); }
            catch (Exception e) { File.WriteAllText(Output + "cleanup-v1.failed", e.ToString()); Debug.LogException(e); }
            return;
        }
        if (File.Exists(PreviewRequest))
        {
            File.Delete(PreviewRequest);
            Directory.CreateDirectory(Output);
            try { CapturePreview(); }
            catch (Exception e) { File.WriteAllText(Output + "preview-v1.failed", e.ToString()); Debug.LogException(e); }
            return;
        }
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Directory.CreateDirectory(Output);
        try { Apply(); }
        catch (Exception e)
        {
            File.WriteAllText(Output + "apply-v1.failed", e.ToString());
            Debug.LogException(e);
        }
    }

    static void UpgradeChevronAndCleanup()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string arrowPath = Folder + "GuideShortArrow.asset";
        BackupAsset(arrowPath, stamp);
        MethodInfo builder = typeof(SpeakerQuietGuideSetup).GetMethod("BuildChevron", BindingFlags.Static | BindingFlags.NonPublic);
        if (builder == null) throw new Exception("Connected chevron builder unavailable");
        Mesh arrow = (Mesh)builder.Invoke(null, null);
        if (!arrow || arrow.vertexCount != 6 || arrow.triangles.Length != 12) throw new Exception("Connected chevron topology mismatch");
        int arrowReferences = CountTargetSceneReferences(arrowPath);
        if (arrowReferences != 2) throw new Exception("Expected one placement arrow in PC and Quest scenes, found " + arrowReferences);

        string[] obsolete =
        {
            Folder + "GuideSpeakerIcon.asset",
            Folder + "GuideGroundIcon.asset",
            Folder + "GuideDashedBoundary.asset"
        };
        var report = new System.Text.StringBuilder();
        report.AppendLine("PASS GuideShortArrow: 2 independent quads -> 1 connected strip; 6 vertices / 4 triangles; 2 scene references retained");
        foreach (string path in obsolete)
        {
            if (!AssetDatabase.LoadMainAssetAtPath(path)) { report.AppendLine("SKIP already absent " + path); continue; }
            string[] users = FindSceneAndPrefabUsers(path);
            if (users.Length != 0) throw new Exception("Refusing to delete referenced asset " + path + "\n" + string.Join("\n", users));
            BackupAsset(path, stamp);
            if (!AssetDatabase.DeleteAsset(path)) throw new Exception("Could not delete " + path);
            report.AppendLine("REMOVED unreferenced " + path);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (obsolete.Any(path => AssetDatabase.LoadMainAssetAtPath(path))) throw new Exception("Obsolete asset cleanup incomplete");
        report.AppendLine("Backups: " + Output + "cleanup-backup-" + stamp + "/");
        File.WriteAllText(Output + "cleanup-v1.done", report.ToString());
        Debug.Log(report.ToString());
    }

    static void BackupAsset(string path, string stamp)
    {
        string folder = Output + "cleanup-backup-" + stamp + "/";
        Directory.CreateDirectory(folder);
        File.Copy(path, folder + Path.GetFileName(path), false);
        if (File.Exists(path + ".meta")) File.Copy(path + ".meta", folder + Path.GetFileName(path) + ".meta", false);
    }

    static int CountTargetSceneReferences(string dependency)
    {
        return Scenes.Count(path => AssetDatabase.GetDependencies(path, true).Contains(dependency));
    }

    static string[] FindSceneAndPrefabUsers(string dependency)
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene").Concat(AssetDatabase.FindAssets("t:Prefab")).Distinct().ToArray();
        return guids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path) && AssetDatabase.GetDependencies(path, true).Contains(dependency))
            .ToArray();
    }

    static void CapturePreview()
    {
        const string path = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
        Scene original = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            SpeakerManager manager = UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m => m.gameObject.scene == scene);
            MethodInfo capture = typeof(SpeakerQuietGuideSetup).GetMethod("CaptureAndTest", BindingFlags.Static | BindingFlags.NonPublic);
            if (capture == null) throw new Exception("Existing safe preview renderer unavailable");
            // The multiple-speaker branch is visual-only and does not depend on the
            // legacy warning/arrow assertions removed from the current design.
            capture.Invoke(null, new object[] { manager, true });
            string source = "output/speaker-placement-20260912/quiet-guide-multiple.png";
            if (!File.Exists(source)) throw new Exception("Preview image was not created");
            File.Copy(source, Output + "recommended-guide-preview.png", true);
            File.WriteAllText(Output + "preview-v1.done", "PASS editor camera render; temporary state restored; scene not modified");
        }
        finally
        {
            if (openedHere && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
    }

    static void Apply()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        CreateBadgeTexture();
        Mesh badgeMesh = CreateBadgeQuad();
        Mesh ringMesh = CreateContinuousRing();
        Material badgeMaterial = CreateBadgeMaterial();
        VerifyAssets(badgeMesh, ringMesh, badgeMaterial);

        Scene originalActive = SceneManager.GetActiveScene();
        var report = new System.Text.StringBuilder();
        report.AppendLine("Speaker guide render asset upgrade " + stamp);
        try
        {
            foreach (string path in Scenes)
            {
                string backup = Output + Path.GetFileNameWithoutExtension(path) + ".before-" + stamp + ".unity";
                File.Copy(path, backup, false);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool openedHere = !scene.isLoaded;
                if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    SpeakerManager manager = UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true).Single(m => m.gameObject.scene == scene);
                    var badges = Field<MeshRenderer[]>(manager, "placedSpeakerBadges");
                    var marks = Field<MeshRenderer[]>(manager, "placedSpeakerGroundMarks");
                    int speakerCount = Field<SpeakerController[]>(manager, "speakerControllers").Length;
                    if ((badges == null || badges.Length == 0) && (marks == null || marks.Length == 0))
                    {
                        report.AppendLine("PASS " + path + " | no location-guide instances in this exhibition scene; shared render assets available without adding a new system");
                        continue;
                    }
                    if (badges == null || marks == null || badges.Length != speakerCount || marks.Length != speakerCount) throw new Exception(path + ": guide renderer count does not match speakers");
                    for (int i = 0; i < speakerCount; i++)
                    {
                        if (!badges[i] || !marks[i]) throw new Exception(path + ": missing guide renderer " + i);
                        badges[i].GetComponent<MeshFilter>().sharedMesh = badgeMesh;
                        badges[i].sharedMaterial = badgeMaterial;
                        marks[i].GetComponent<MeshFilter>().sharedMesh = ringMesh;
                        Dirty(badges[i]); Dirty(badges[i].GetComponent<MeshFilter>());
                        Dirty(marks[i]); Dirty(marks[i].GetComponent<MeshFilter>());
                    }
                    ValidateLifecycle(manager, badges, marks);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + path);
                    report.AppendLine("PASS " + path + " | " + speakerCount + " badges + " + speakerCount + " continuous rings rebound; lifecycle restored");
                }
                finally
                {
                    if (openedHere && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
        finally
        {
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
        }
        AssetDatabase.SaveAssets();
        report.AppendLine("PASS badge=4 vertices/2 triangles/1 alpha texture; ring=128 vertices/128 connected triangles");
        report.AppendLine("Existing visibility, positioning, ripples, placement controls and speaker state logic unchanged.");
        File.WriteAllText(Output + "apply-v1.done", report.ToString());
        Debug.Log(report.ToString());
    }

    static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    static void Call(object target, string name)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    static void Dirty(UnityEngine.Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static void CreateBadgeTexture()
    {
        const int size = 256;
        const int samples = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "GuideSpeakerBadgeAlpha" };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                Vector2 p = new Vector2(x + (sx + .5f) / samples, y + (sy + .5f) / samples);
                if (BadgeShape(p)) covered++;
            }
            byte alpha = (byte)Mathf.RoundToInt(255f * covered / (samples * samples));
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels); texture.Apply(false, false);
        File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    static bool BadgeShape(Vector2 p)
    {
        Vector2 c = new Vector2(128, 128);
        float circle = Mathf.Abs(Vector2.Distance(p, c) - 105f);
        if (circle <= 4f) return true;
        if (p.x >= 53 && p.x <= 89 && p.y >= 109 && p.y <= 147) return true;
        if (InsideTriangle(p, new Vector2(89, 109), new Vector2(139, 74), new Vector2(139, 182)) ||
            InsideTriangle(p, new Vector2(89, 109), new Vector2(139, 182), new Vector2(89, 147))) return true;
        Vector2 waveCenter = new Vector2(126, 128);
        Vector2 d = p - waveCenter;
        float angle = Mathf.Atan2(d.y, d.x);
        if (Mathf.Abs(angle) <= .84f && (Mathf.Abs(d.magnitude - 47f) <= 4.5f || Mathf.Abs(d.magnitude - 72f) <= 4.5f)) return true;
        return false;
    }

    static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0;
        bool positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    }

    static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    static Mesh CreateBadgeQuad()
    {
        var mesh = LoadOrCreateMesh(BadgeMeshPath, "GuideSpeakerBadgeQuad");
        mesh.Clear();
        mesh.vertices = new[] { new Vector3(-.4f, -.4f, 0), new Vector3(.4f, -.4f, 0), new Vector3(.4f, .4f, 0), new Vector3(-.4f, .4f, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); Dirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    static Mesh CreateContinuousRing()
    {
        const int segments = 64;
        const float radius = .65f;
        const float halfWidth = .011f;
        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            vertices[i * 2] = direction * (radius - halfWidth);
            vertices[i * 2 + 1] = direction * (radius + halfWidth);
            int next = (i + 1) % segments;
            int t = i * 6, inner = i * 2, outer = inner + 1, nextInner = next * 2, nextOuter = nextInner + 1;
            triangles[t] = inner; triangles[t + 1] = nextInner; triangles[t + 2] = outer;
            triangles[t + 3] = outer; triangles[t + 4] = nextInner; triangles[t + 5] = nextOuter;
        }
        var mesh = LoadOrCreateMesh(RingMeshPath, "GuideGroundRingContinuous");
        mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles;
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); Dirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    static Mesh LoadOrCreateMesh(string path, string name)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh) Undo.RecordObject(mesh, "Update speaker guide render asset");
        else { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
        return mesh;
    }

    static Material CreateBadgeMaterial()
    {
        Shader shader = Shader.Find("Shinjuku/Quiet Placement Guide");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Quiet placement guide shader unavailable");
        var material = AssetDatabase.LoadAssetAtPath<Material>(BadgeMaterialPath);
        if (!material) { material = new Material(shader) { name = "GuideSpeakerBadgeTexture" }; AssetDatabase.CreateAsset(material, BadgeMaterialPath); }
        else { Undo.RecordObject(material, "Update textured speaker badge"); material.shader = shader; }
        material.SetColor("_Color", new Color(.94f, .91f, .80f, .80f));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        material.SetFloat("_UseTexture", 1f);
        material.SetFloat("_Billboard", 1f);
        Dirty(material); AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    static void VerifyAssets(Mesh badge, Mesh ring, Material material)
    {
        if (badge.vertexCount != 4 || badge.triangles.Length != 6 || badge.uv.Length != 4) throw new Exception("Badge is not one textured quad");
        if (ring.vertexCount != 128 || ring.triangles.Length != 384) throw new Exception("Continuous ring topology mismatch");
        if (!material.mainTexture || material.GetFloat("_UseTexture") < .5f || material.GetFloat("_Billboard") < .5f) throw new Exception("Badge texture material incomplete");
        float min = ring.vertices.Min(v => new Vector2(v.x, v.z).magnitude);
        float max = ring.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
        if (Mathf.Abs(min - .639f) > .0005f || Mathf.Abs(max - .661f) > .0005f) throw new Exception("Ring dimensions changed");
    }

    static void ValidateLifecycle(SpeakerManager manager, MeshRenderer[] badges, MeshRenderer[] marks)
    {
        var speakers = Field<SpeakerController[]>(manager, "speakerControllers");
        bool[] states = speakers.Select(s => s.isSpeakerTaken).ToArray();
        bool[] badgeEnabled = badges.Select(r => r.enabled).ToArray();
        bool[] markEnabled = marks.Select(r => r.enabled).ToArray();
        try
        {
            // Editor-loaded Udon proxies can retain partially initialized transient
            // ripple arrays. Rebuild them before exercising the normal update path.
            Call(manager, "ConfigureAudibleRangeRenderer");
            for (int i = 0; i < speakers.Length; i++) speakers[i].isSpeakerTaken = true;
            Call(manager, "UpdatePlacedSpeakerIndicators");
            if (badges.Any(r => !r.enabled) || marks.Any(r => !r.enabled)) throw new Exception("Occupied guide visibility failed");
            for (int i = 0; i < speakers.Length; i++) speakers[i].isSpeakerTaken = false;
            Call(manager, "UpdatePlacedSpeakerIndicators");
            if (badges.Any(r => r.enabled) || marks.Any(r => r.enabled)) throw new Exception("Returned guide visibility failed");
        }
        finally
        {
            for (int i = 0; i < speakers.Length; i++) speakers[i].isSpeakerTaken = states[i];
            Call(manager, "UpdatePlacedSpeakerIndicators");
            for (int i = 0; i < badges.Length; i++) { badges[i].enabled = badgeEnabled[i]; marks[i].enabled = markEnabled[i]; }
        }
    }
}
