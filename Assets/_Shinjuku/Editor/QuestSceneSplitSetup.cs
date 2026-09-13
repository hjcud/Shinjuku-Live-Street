using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QuestSceneSplitSetup
{
    const string Folder = "Assets/_Shinjuku/Scenes/";
    // Split to keep the legacy path literal out of the editor-helper path migration.
    static readonly string OldScene = Folder + "TEST" + ".unity";
    const string PcScene = Folder + "TEST_PC.unity";
    const string QuestScene = Folder + "TEST_Quest.unity";
    const string Output = "output/quest-optimization-20260913/";

    static QuestSceneSplitSetup() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string request = Output + "split-scenes.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Split(); }
        catch (Exception e)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "split-scenes.failed", e.ToString());
            Debug.LogException(e);
        }
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    static void Split()
    {
        Directory.CreateDirectory(Output);
        var scene = SceneManager.GetActiveScene();
        if (scene.path != OldScene || SceneManager.sceneCount != 1) throw new Exception("Open only the original TEST scene in edit mode.");
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(OldScene)) throw new Exception("Original TEST scene asset is missing.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PcScene) || AssetDatabase.LoadAssetAtPath<SceneAsset>(QuestScene))
            throw new Exception("TEST_PC or TEST_Quest already exists; nothing was overwritten.");

        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save the current TEST scene.");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string recovery = Output + "TEST.before-scene-split-" + stamp + ".unity";
        if (!EditorSceneManager.SaveScene(scene, recovery, true)) throw new Exception("Could not create recovery scene.");

        string originalGuid = AssetDatabase.AssetPathToGUID(OldScene);
        string originalHash = Hash(OldScene);
        int rootCount = scene.rootCount;
        int componentCount = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Component>(true).Length);
        string[] dependencies = AssetDatabase.GetDependencies(OldScene, true).Where(p => p != OldScene).OrderBy(p => p).ToArray();

        if (!AssetDatabase.CopyAsset(OldScene, QuestScene)) throw new Exception("Could not create TEST_Quest.");
        string moveError = AssetDatabase.MoveAsset(OldScene, PcScene);
        if (!string.IsNullOrEmpty(moveError))
        {
            AssetDatabase.DeleteAsset(QuestScene);
            throw new Exception("Could not rename TEST to TEST_PC: " + moveError);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        if (scene.path != PcScene) throw new Exception("The open scene did not follow the asset rename: " + scene.path);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save renamed PC scene.");
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(PcScene) || !AssetDatabase.LoadAssetAtPath<SceneAsset>(QuestScene))
            throw new Exception("One of the split scene assets is missing.");
        if (AssetDatabase.AssetPathToGUID(PcScene) != originalGuid) throw new Exception("PC scene did not preserve the original GUID.");
        if (AssetDatabase.AssetPathToGUID(QuestScene) == originalGuid) throw new Exception("Quest scene did not receive an independent GUID.");
        if (Hash(PcScene) != originalHash || Hash(QuestScene) != originalHash) throw new Exception("Scene contents changed during split.");

        string[] pcDependencies = AssetDatabase.GetDependencies(PcScene, true).Where(p => p != PcScene).OrderBy(p => p).ToArray();
        string[] questDependencies = AssetDatabase.GetDependencies(QuestScene, true).Where(p => p != QuestScene).OrderBy(p => p).ToArray();
        if (!pcDependencies.SequenceEqual(dependencies) || !questDependencies.SequenceEqual(dependencies))
            throw new Exception("Initial PC/Quest dependency sets differ from TEST.");

        File.WriteAllText(Output + "split-scenes.done",
            DateTime.Now + " PASS\n" +
            "Active=" + scene.path + "\n" +
            "PC=" + PcScene + " GUID=" + AssetDatabase.AssetPathToGUID(PcScene) + "\n" +
            "Quest=" + QuestScene + " GUID=" + AssetDatabase.AssetPathToGUID(QuestScene) + "\n" +
            "Roots=" + rootCount + " Components=" + componentCount + " Dependencies=" + dependencies.Length + "\n" +
            "Identical baseline scene contents and dependency sets verified. Original PC GUID preserved; Quest GUID is independent.\n" +
            "Recovery=" + recovery + "\n" +
            "Quest optimization and separate lighting/occlusion bake have not been applied yet.");
    }
}
