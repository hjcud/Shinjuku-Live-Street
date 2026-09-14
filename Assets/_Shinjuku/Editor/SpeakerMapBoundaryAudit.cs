using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only floor boundary captures. Does not open scenes or change world assets.
[InitializeOnLoad]
public static class SpeakerMapBoundaryAudit
{
    private const string Output = "output/speaker-map-ui-20260912";
    static SpeakerMapBoundaryAudit() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        string request = Output + "/boundary-audit.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        File.Delete(request);
        try
        {
            if (SceneManager.GetActiveScene().path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Expected active TEST scene.");
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(SpeakerMapSetup).GetMethod("InspectCollisions", flags).Invoke(null, null);
            var capture = typeof(SpeakerMapSetup).GetMethod("Capture", flags);
            capture.Invoke(null, new object[] { new Vector3(-25, 8.3f, 29), 6f, 2f, Output + "/boundary-west-door.png" });
            capture.Invoke(null, new object[] { new Vector3(-25, 8.3f, 29), 25f, 1.8f, Output + "/boundary-northwest.png" });
            capture.Invoke(null, new object[] { new Vector3(65, 8.3f, 29), 25f, 1.8f, Output + "/boundary-northeast.png" });
            capture.Invoke(null, new object[] { new Vector3(-25, 8.3f, -42), 25f, 1.8f, Output + "/boundary-southwest.png" });
            capture.Invoke(null, new object[] { new Vector3(65, 8.3f, -42), 25f, 1.8f, Output + "/boundary-southeast.png" });
            File.WriteAllText(Output + "/boundary-audit.done", DateTime.Now.ToString("s") + " PASS: current player-collision slice and all four ground-floor quadrants captured; no scene writes.");
        }
        catch (Exception error) { File.WriteAllText(Output + "/boundary-audit.failed", error.ToString()); Debug.LogException(error); }
    }
}
