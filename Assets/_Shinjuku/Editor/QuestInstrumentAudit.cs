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
public static class QuestInstrumentAudit
{
    const string Request = "output/quest-optimization-20260913/instrument-audit-v1.request";
    const string Output = "output/quest-optimization-20260913/instrument-audit-v1.txt";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    const string QuestScene = "Assets/_Shinjuku/Scenes/TEST_Quest.unity";
    static readonly string[] Terms = { "instrument", "guitar", "bass", "drum", "piano", "keyboard", "violin", "cello", "sax", "trumpet", "music", "midori", "楽器", "みどり", "악기" };

    static QuestInstrumentAudit() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        try { Run(); }
        catch (Exception exception)
        {
            File.WriteAllText(Output + ".failed", exception.ToString(), new UTF8Encoding(false));
            Debug.LogException(exception);
        }
    }

    static void Run()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene changes; audit aborted.");

        var lines = new List<string>();
        AuditScene(PcScene, "PC", lines);
        AuditScene(QuestScene, "QUEST", lines);
        File.WriteAllText(Output, string.Join("\n", lines), new UTF8Encoding(false));
    }

    static void AuditScene(string scenePath, string label, List<string> lines)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        lines.Add("=== " + label + " ===");
        GameObject[] all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
        foreach (GameObject gameObject in all.Where(go => Terms.Any(term => Path(go.transform).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            string materials = renderer == null ? "<no renderer>" : string.Join("; ", renderer.sharedMaterials.Select(material => material ? material.name + " [" + material.shader.name + "]" : "<null>"));
            lines.Add($"{Path(gameObject.transform)} | self={gameObject.activeSelf} hierarchy={gameObject.activeInHierarchy} | renderer={(renderer ? renderer.GetType().Name : "none")} enabled={(renderer ? renderer.enabled.ToString() : "-")} | {materials}");
        }
        lines.Add(string.Empty);
    }

    static string Path(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }
}
