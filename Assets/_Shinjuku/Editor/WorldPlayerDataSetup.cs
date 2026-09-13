using System;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

/// <summary>기존 TEST 월드의 로컬 설정 및 스피커 이벤트에 PlayerData 저장 연결.</summary>
[InitializeOnLoad]
public static class WorldPlayerDataSetup
{
    private const string Output = "output/player-persistence";
    private const string ScenePath = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    private const string ProgramPath = "Assets/_Shinjuku/Scripts/UI/WorldPlayerData.asset";

    static WorldPlayerDataSetup() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string request = Output + "/setup.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        if (File.Exists(Output + "/setup.failed")) File.Delete(Output + "/setup.failed");
        try { Setup(); }
        catch (Exception error)
        {
            File.WriteAllText(Output + "/setup.failed", error.ToString());
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Shinjuku/Connect player persistence")]
    public static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open TEST in edit mode.");
        Directory.CreateDirectory(Output);
        var settings = SceneComponents<WorldLocalSettings>(scene).Single();
        var speakers = SceneComponents<SpeakerController>(scene);
        if (!settings.gameObject.activeInHierarchy || !settings.traffic || speakers.Length == 0)
            throw new InvalidOperationException("Expected active world settings and speakers.");
        var existing = SceneComponents<WorldPlayerData>(scene);
        if (existing.Length > 1) throw new InvalidOperationException("Multiple player persistence trackers in the scene.");
        bool wasDirty = scene.isDirty;
        string backup = Output + "/TEST.before-player-data-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
        if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up the active scene.");

        var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
        if (!program)
        {
            program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Shinjuku/Scripts/UI/WorldPlayerData.cs");
            if (!program.sourceCsScript) throw new InvalidOperationException("WorldPlayerData source not imported.");
            AssetDatabase.CreateAsset(program, ProgramPath);
        }
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("UdonSharp compilation failed; see Console.");
        program.UpdateProgram();

        var tracker = existing.SingleOrDefault();
        if (!tracker)
        {
            var root = new GameObject("World Player Data");
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Add player persistence");
            tracker = root.AddUdonSharpComponent<WorldPlayerData>();
        }
        Undo.RecordObjects(new UnityEngine.Object[] { settings, tracker }, "Connect player persistence");
        tracker.settings = settings;
        settings.playerData = tracker;
        Sync(tracker);
        Sync(settings);
        var trackerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(tracker);
        foreach (string retired in new[] { "totalStaySeconds", "totalSpeakerSeconds", "cleanSpeakerReturns", "hasPlacedSpeaker", "hasPostedImage" })
            trackerBacking.publicVariables.RemoveVariable(retired);
        // 이전 연결 도구가 추가했던 이미지 저장 참조만 제거. 이미지 기능 자체는 유지.
        foreach (var image in SceneComponents<ImageLoader>(scene))
        {
            var imageBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(image);
            if (imageBacking.publicVariables.TryGetVariableValue("playerData", out UdonBehaviour oldTracker) && oldTracker == trackerBacking)
            {
                Undo.RecordObject(imageBacking, "Remove retired image persistence reference");
                imageBacking.publicVariables.RemoveVariable("playerData");
                EditorUtility.SetDirty(imageBacking);
                if (PrefabUtility.IsPartOfPrefabInstance(imageBacking)) PrefabUtility.RecordPrefabInstancePropertyModifications(imageBacking);
            }
        }
        foreach (var speaker in speakers)
        {
            Undo.RecordObject(speaker, "Connect speaker persistence");
            speaker.playerData = tracker;
            Sync(speaker);
        }

        var report = new StringBuilder();
        report.AppendLine("Scene: " + scene.path + "; initially dirty: " + wasDirty);
        report.AppendLine("Backup: " + backup);
        report.AppendLine("PlayerData tracker: " + tracker.name + "; speakers: " + speakers.Length + "; schema: 2; integer minutes; six requested values including recordStartDay.");
        CheckReference(settings, "playerData", tracker);
        CheckReference(tracker, "settings", settings);
        foreach (var speaker in speakers) CheckReference(speaker, "playerData", tracker);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(tracker);
        if (backing.programSource != program || !program.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_onPlayerRestored"))
            throw new InvalidOperationException("Missing compiled PlayerData restore entry point.");
        report.AppendLine("PASS: UdonSharp compilation and all serialized backing references / restore event.");
        EditorSceneManager.MarkSceneDirty(scene);
        // 기존에 미저장 작업이 있으면 원본 디스크를 덮어쓰지 않고 완성된 씬 사본 저장.
        string result = wasDirty ? Output + "/TEST.with-player-data.unity" : ScenePath;
        if (!EditorSceneManager.SaveScene(scene, result, wasDirty)) throw new IOException("Could not save connected scene.");
        report.AppendLine("Saved: " + result);
        report.AppendLine(wasDirty ? "Original active scene remains dirty to preserve other pending edits." : "Original TEST saved.");
        report.AppendLine("Live VRChat cloud persistence requires an uploaded-world rejoin test.");
        File.WriteAllText(Output + "/setup.done", report.ToString());
    }

    private static T[] SceneComponents<T>(Scene scene) where T : Component
    {
        return UnityEngine.Object.FindObjectsOfType<T>(true).Where(c => c.gameObject.scene == scene).ToArray();
    }

    private static void Sync(UdonSharpBehaviour proxy)
    {
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
        EditorUtility.SetDirty(proxy);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        EditorUtility.SetDirty(backing);
        if (PrefabUtility.IsPartOfPrefabInstance(proxy)) PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
        if (PrefabUtility.IsPartOfPrefabInstance(backing)) PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
    }

    private static void CheckReference(UdonSharpBehaviour source, string key, UdonSharpBehaviour target)
    {
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(source);
        if (!backing.publicVariables.TryGetVariableValue(key, out UdonBehaviour value) || value != UdonSharpEditorUtility.GetBackingUdonBehaviour(target))
            throw new InvalidOperationException(source.name + ": missing serialized " + key);
    }
}
