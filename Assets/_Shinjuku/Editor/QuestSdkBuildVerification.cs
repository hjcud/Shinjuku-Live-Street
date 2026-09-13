using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VRC.SDK3.Editor;

[InitializeOnLoad]
public static class QuestSdkBuildVerification
{
    const string Output = "output/quest-optimization-20260913/";
    const string Request = Output + "sdk-build-check.request";
    const string ClosePcRequest = Output + "close-pc-scene.request";
    const string PcScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";
    static bool running;

    static QuestSdkBuildVerification()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!running && !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(ClosePcRequest))
        {
            File.Delete(ClosePcRequest);
            var lines = new StringBuilder();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var loaded = SceneManager.GetSceneAt(i);
                lines.AppendLine($"Loaded={loaded.path} Active={loaded == SceneManager.GetActiveScene()} Dirty={loaded.isDirty}");
                if (loaded.path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity")
                {
                    EditorSceneManager.CloseScene(loaded, true);
                    lines.AppendLine("ClosedNonQuestWithoutSaving=True");
                }
            }
            lines.AppendLine("RemainingSceneCount=" + SceneManager.sceneCount);
            File.WriteAllText(Output + "close-pc-scene.done", lines.ToString(), new UTF8Encoding(false));
            return;
        }
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        if (File.Exists(Output + "sdk-build-check.done")) File.Delete(Output + "sdk-build-check.done");
        if (File.Exists(Output + "sdk-build-check.failed")) File.Delete(Output + "sdk-build-check.failed");
        if (File.Exists(Output + "sdk-build-check.progress")) File.Delete(Output + "sdk-build-check.progress");
        running = true;
        _ = Run();
    }

    static async Task Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("SDK build check requires Android as the active target.");
            if (SceneManager.GetActiveScene().path != "Assets/_Shinjuku/Scenes/TEST_Quest.unity")
                throw new InvalidOperationException("SDK build check requires TEST_Quest as the active scene.");
            if (SceneManager.sceneCount != 1)
                throw new InvalidOperationException("SDK build check requires TEST_Quest to be the only loaded scene. Loaded scene count: " + SceneManager.sceneCount);
            string pcHashBefore = Hash(PcScene);

            EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
            IVRCSdkWorldBuilderApi builder = null;
            for (int i = 0; i < 120 && builder == null; i++)
            {
                await Task.Delay(1000);
                VRCSdkControlPanel.TryGetBuilder(out builder);
            }
            if (builder == null) throw new InvalidOperationException("VRChat SDK World Builder did not initialize.");

            File.WriteAllText(Output + "sdk-build-check.progress", "Building Android world bundle...", new UTF8Encoding(false));
            string bundlePath = await builder.Build();
            long size = File.Exists(bundlePath) ? new FileInfo(bundlePath).Length : -1;
            string pcHashAfter = Hash(PcScene);
            if (pcHashAfter != pcHashBefore)
                throw new InvalidOperationException("PC scene changed during Quest build verification: " + pcHashAfter);
            string report = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " PASS\n" +
                            "Target=Android\nScene=" + SceneManager.GetActiveScene().path + "\n" +
                            "Bundle=" + bundlePath + "\nCompressedBundleBytes=" + size + "\n" +
                            "PCSceneHashBefore=" + pcHashBefore + "\nPCSceneHashAfter=" + pcHashAfter + "\n";
            File.WriteAllText(Output + "sdk-build-check.done", report, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Output + "sdk-build-check.failed", ex.ToString(), new UTF8Encoding(false));
        }
        finally
        {
            running = false;
            if (File.Exists(Output + "sdk-build-check.progress")) File.Delete(Output + "sdk-build-check.progress");
        }
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
