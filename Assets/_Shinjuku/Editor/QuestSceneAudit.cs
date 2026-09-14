using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestSceneAudit
{
    const string ScenePath = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    const string RequestPath = "output/quest-optimization-20260913/audit.request";
    const string ReportPath = "output/quest-optimization-20260913/audit.txt";

    static QuestSceneAudit()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    static void RunIfRequested()
    {
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));

        Scene opened = default;
        try
        {
            opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var roots = opened.GetRootGameObjects();
            var components = roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
            var renderers = components.OfType<Renderer>().ToArray();
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            var meshes = new HashSet<Mesh>();
            foreach (var mf in components.OfType<MeshFilter>()) if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
            foreach (var smr in components.OfType<SkinnedMeshRenderer>()) if (smr.sharedMesh != null) meshes.Add(smr.sharedMesh);

            long vertices = meshes.Sum(m => (long)m.vertexCount);
            long triangles = 0;
            foreach (var mesh in meshes)
                for (int s = 0; s < mesh.subMeshCount; s++)
                    triangles += (long)mesh.GetIndexCount(s) / 3L;

            var shaderCounts = materials.GroupBy(m => m.shader != null ? m.shader.name : "<missing>")
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key);
            var componentCounts = components.Where(c => c != null).GroupBy(c => c.GetType().FullName)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key);
            var lights = components.OfType<Light>().ToArray();
            var realtimeLights = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime);
            var mixedLights = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);
            var bakedLights = lights.Count(l => l.lightmapBakeType == LightmapBakeType.Baked);

            var dependencies = AssetDatabase.GetDependencies(ScenePath, true).Distinct().ToArray();
            var textureRows = new List<string>();
            long dependencyBytes = 0;
            int textureCount = 0;
            int oversizedTextures = 0;
            foreach (var dep in dependencies)
            {
                var absolute = Path.GetFullPath(dep);
                if (File.Exists(absolute)) dependencyBytes += new FileInfo(absolute).Length;
                var importer = AssetImporter.GetAtPath(dep) as TextureImporter;
                if (importer == null) continue;
                textureCount++;
                int width = 0, height = 0;
                try { importer.GetSourceTextureWidthAndHeight(out width, out height); } catch { }
                if (width > 1024 || height > 1024)
                {
                    oversizedTextures++;
                    textureRows.Add($"{width}x{height} max={importer.maxTextureSize} {dep}");
                }
            }

            var audioRows = new List<string>();
            int audioCount = 0;
            foreach (var dep in dependencies)
            {
                var importer = AssetImporter.GetAtPath(dep) as AudioImporter;
                if (importer == null) continue;
                audioCount++;
                var settings = importer.defaultSampleSettings;
                audioRows.Add($"{settings.loadType} {settings.compressionFormat} q={settings.quality:0.00} {dep}");
            }

            var missingScripts = 0;
            foreach (var root in roots)
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject);

            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " QUEST SCENE AUDIT");
            sb.AppendLine("Scene=" + ScenePath);
            sb.AppendLine($"Roots={roots.Length} Components={components.Length} MissingScripts={missingScripts}");
            sb.AppendLine($"Renderers={renderers.Length} UniqueMaterials={materials.Length} UniqueMeshes={meshes.Count}");
            sb.AppendLine($"Vertices={vertices} Triangles={triangles}");
            sb.AppendLine($"Lights={lights.Length} Realtime={realtimeLights} Mixed={mixedLights} Baked={bakedLights}");
            sb.AppendLine($"Cameras={components.OfType<Camera>().Count()} ReflectionProbes={components.OfType<ReflectionProbe>().Count()} ParticleSystems={components.OfType<ParticleSystem>().Count()}");
            sb.AppendLine($"Colliders={components.OfType<Collider>().Count()} Rigidbodies={components.OfType<Rigidbody>().Count()} Cloth={components.OfType<Cloth>().Count()}");
            sb.AppendLine($"Dependencies={dependencies.Length} SourceBytes={dependencyBytes} Textures={textureCount} OversizedTextures={oversizedTextures} AudioClips={audioCount}");
            sb.AppendLine();
            sb.AppendLine("SHADERS");
            foreach (var g in shaderCounts) sb.AppendLine($"{g.Count(),5}  {g.Key}");
            sb.AppendLine();
            sb.AppendLine("MATERIALS_BY_SHADER");
            foreach (var material in materials.OrderBy(m => m.shader != null ? m.shader.name : "").ThenBy(m => m.name))
                sb.AppendLine($"{(material.shader != null ? material.shader.name : "<missing>")} | {AssetDatabase.GetAssetPath(material)}");
            sb.AppendLine();
            sb.AppendLine("TOP_RENDERERS_BY_TRIANGLES");
            foreach (var row in renderers.Select(r => new { Renderer = r, Mesh = RendererMesh(r) })
                         .Where(x => x.Mesh != null)
                         .Select(x => new { x.Renderer, x.Mesh, Triangles = MeshTriangles(x.Mesh) })
                         .OrderByDescending(x => x.Triangles).Take(80))
                sb.AppendLine($"{row.Triangles,9}  {HierarchyPath(row.Renderer.transform)} | {AssetDatabase.GetAssetPath(row.Mesh)}");
            sb.AppendLine();
            sb.AppendLine("ROOT_SUMMARY");
            foreach (var root in roots.OrderBy(r => r.name))
            {
                var rootRenderers = root.GetComponentsInChildren<Renderer>(true);
                long rootTriangles = rootRenderers.Select(RendererMesh).Where(m => m != null).Sum(MeshTriangles);
                sb.AppendLine($"{rootTriangles,10} tris {rootRenderers.Length,5} renderers active={root.activeSelf} | {root.name}");
            }
            sb.AppendLine();
            sb.AppendLine("SPECIAL_COMPONENT_PATHS");
            foreach (var c in components.Where(c => c is Camera || c is ReflectionProbe || c is ParticleSystem || c is Light ||
                                                  c.GetType().FullName.IndexOf("PostProcess", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  c.GetType().FullName.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0)
                                        .OrderBy(c => c.GetType().FullName).ThenBy(c => HierarchyPath(c.transform)))
            {
                var enabled = c is Behaviour b ? b.enabled.ToString() : "n/a";
                sb.AppendLine($"{c.GetType().FullName} enabled={enabled} active={c.gameObject.activeInHierarchy} | {HierarchyPath(c.transform)}");
            }
            sb.AppendLine();
            sb.AppendLine("ROOT_CHILD_SUMMARY");
            foreach (var root in roots.OrderBy(r => r.name))
            {
                foreach (Transform child in root.transform)
                {
                    var childRenderers = child.GetComponentsInChildren<Renderer>(true);
                    long childTriangles = childRenderers.Select(RendererMesh).Where(m => m != null).Sum(MeshTriangles);
                    if (childRenderers.Length > 0 || childTriangles > 0)
                        sb.AppendLine($"{childTriangles,10} tris {childRenderers.Length,5} renderers active={child.gameObject.activeSelf} | {root.name}/{child.name}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("OVERSIZED_TEXTURES");
            foreach (var row in textureRows.OrderBy(x => x)) sb.AppendLine(row);
            sb.AppendLine();
            sb.AppendLine("AUDIO_IMPORTERS");
            foreach (var row in audioRows.OrderBy(x => x)) sb.AppendLine(row);
            sb.AppendLine();
            sb.AppendLine("COMPONENTS");
            foreach (var g in componentCounts) sb.AppendLine($"{g.Count(),5}  {g.Key}");
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[QuestSceneAudit] Wrote " + ReportPath);
        }
        catch (Exception ex)
        {
            File.WriteAllText(ReportPath + ".failed", ex.ToString());
            Debug.LogException(ex);
        }
        finally
        {
            if (opened.IsValid() && opened.isLoaded) EditorSceneManager.CloseScene(opened, true);
        }
    }

    static Mesh RendererMesh(Renderer renderer)
    {
        var skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null) return skinned.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    static long MeshTriangles(Mesh mesh)
    {
        long triangles = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) triangles += (long)mesh.GetIndexCount(s) / 3L;
        return triangles;
    }

    static string HierarchyPath(Transform transform)
    {
        var names = new List<string>();
        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }
}
