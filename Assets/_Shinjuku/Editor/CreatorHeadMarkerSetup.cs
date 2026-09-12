using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UdonSharp;
using UdonSharpEditor;

/// <summary>
/// TEST 씬 전용 제작자 안내 생성 및 프리팹 저장
/// </summary>
[InitializeOnLoad]
public static class CreatorHeadMarkerSetup
{
    private const string Output = "output/creator-head-marker";
    private const string PrefabPath = "Assets/_Shinjuku/UI/CreatorMarker/CreatorHeadMarker.prefab";
    private const string TargetScene = "Assets/_Shinjuku/Scenes/TEST_PC.unity";

    static CreatorHeadMarkerSetup() { EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string repairRequest = Output + "/repair.request";
        if (File.Exists(repairRequest))
        {
            File.Delete(repairRequest);
            try { RepairExisting(); }
            catch (Exception exception)
            {
                File.WriteAllText(Output + "/repair.failed", exception.ToString());
                Debug.LogException(exception);
            }
            return;
        }
        string request = Output + "/build.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Create(); }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/build.failed", exception.ToString());
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Shinjuku/Add creator head marker to TEST")]
    public static void Create()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != TargetScene || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open TEST in edit mode first.");
        if (UnityEngine.Object.FindObjectsOfType<CreatorHeadMarker>(true).Any(m => m.gameObject.scene == scene))
            throw new InvalidOperationException("Creator marker already exists. Edit the existing marker instead.");
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_ShinjukuExhibition/Fonts/ShinjukuVisitorUI.otf");
        if (!font) throw new InvalidOperationException("Missing ShinjukuVisitorUI font.");

        Directory.CreateDirectory(Output);
        Directory.CreateDirectory("Assets/_Shinjuku/UI/CreatorMarker");
        var root = new GameObject("Creator Head Marker - Cuding");
        Undo.RegisterCreatedObjectUndo(root, "Add creator head marker");
        var panel = new GameObject("Marker Visual", typeof(RectTransform), typeof(Canvas));
        panel.transform.SetParent(root.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.sizeDelta = new Vector2(1000, 220);
        rect.localScale = Vector3.one * 0.0018f;
        panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Label(rect, "Title", "作者はここです！", new Vector2(0, 37), new Vector2(980, 130), 104,
            font, new Color(1f, 0.84f, 0.36f), 3f);
        Label(rect, "Feedback", "ご意見や不具合、お聞かせください！", new Vector2(0, -66), new Vector2(980, 65), 44,
            font, Color.white, 2f);
        foreach (var label in panel.GetComponentsInChildren<Text>()) Validate(label);

        var marker = root.AddUdonSharpComponent<CreatorHeadMarker>();
        marker.markerRoot = rect;
        marker.targetDisplayName = "Cuding";
        marker.hideForTarget = true;
        UdonSharpEditorUtility.CopyProxyToUdon(marker);
        UdonSharpProgramAsset.CompileAllCsPrograms();
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(marker);
        if (!backing || !backing.programSource || !backing.programSource.SerializedProgramAsset ||
            backing.programSource.SerializedProgramAsset.RetrieveProgram() == null)
            throw new InvalidOperationException("Creator marker Udon program not compiled.");

        Capture(rect, Output + "/preview.png");
        panel.SetActive(false);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText(Output + "/build.done",
            "Created one CreatorHeadMarker in TEST. Target=Cuding (exact display name).\n" +
            "Udon program compiled. Japanese glyphs and text bounds checked. Visual hidden until matching remote player is present.\n" +
            "Prefab saved: " + PrefabPath + "\nScene remains unsaved to preserve other pending edits.\n" +
            "Live VRChat tracking, join/leave and VR readability require in-client validation.\n");
    }

    [MenuItem("Tools/Shinjuku/Repair creator head marker in TEST")]
    public static void RepairExisting()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != TargetScene || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open TEST in edit mode first.");
        var markers = UnityEngine.Object.FindObjectsOfType<CreatorHeadMarker>(true)
            .Where(m => m.gameObject.scene == scene).ToArray();
        if (markers.Length != 1) throw new InvalidOperationException("Expected exactly one existing creator marker.");
        var marker = markers[0];
        var visual = marker.transform.Find("Marker Visual") as RectTransform;
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(marker);
        if (!visual || !visual.GetComponent<Canvas>() || !backing)
            throw new InvalidOperationException("Existing marker visual or backing behaviour missing.");
        foreach (var label in visual.GetComponentsInChildren<Text>(true)) Validate(label);

        Undo.RegisterFullObjectHierarchyUndo(marker.gameObject, "Repair creator marker connections");
        var script = MonoScript.FromMonoBehaviour(marker);
        var program = AssetDatabase.FindAssets("t:UdonSharpProgramAsset")
            .Select(guid => AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(guid)))
            .FirstOrDefault(asset => asset && asset.sourceCsScript == script);
        if (!program)
        {
            const string programPath = "Assets/_Shinjuku/Scripts/UI/CreatorHeadMarker.asset";
            if (File.Exists(programPath)) throw new InvalidOperationException("Program path is occupied by an unrelated asset.");
            program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = script;
            AssetDatabase.CreateAsset(program, programPath);
        }
        // 기존 안내 대상과 표시 설정 유지, 누락된 실행 프로그램 및 표시 영역 연결
        backing.programSource = program;
        marker.markerRoot = visual;
        EditorUtility.SetDirty(program);
        AssetDatabase.SaveAssetIfDirty(program);
        UdonSharpProgramAsset.CompileAllCsPrograms();
        program.UpdateProgram();
        if (!program.SerializedProgramAsset || program.SerializedProgramAsset.RetrieveProgram() == null)
            throw new InvalidOperationException("Creator marker Udon compilation failed.");
        var serializedBacking = new SerializedObject(backing);
        serializedBacking.FindProperty("serializedProgramAsset").objectReferenceValue = program.SerializedProgramAsset;
        serializedBacking.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(marker);
        Transform connectedVisual;
        if (!backing.publicVariables.TryGetVariableValue<Transform>("markerRoot", out connectedVisual) || connectedVisual != visual)
            throw new InvalidOperationException("Udon markerRoot connection verification failed.");
        bool wasActive = visual.gameObject.activeSelf;
        try
        {
            visual.gameObject.SetActive(true);
            Capture(visual, Output + "/preview.png");
        }
        finally { visual.gameObject.SetActive(wasActive); }
        visual.gameObject.SetActive(false);
        EditorUtility.SetDirty(marker);
        EditorUtility.SetDirty(backing);
        EditorSceneManager.MarkSceneDirty(scene);
        Directory.CreateDirectory("Assets/_Shinjuku/UI/CreatorMarker");
        PrefabUtility.SaveAsPrefabAssetAndConnect(marker.gameObject, PrefabPath, InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssetIfDirty(program);
        File.WriteAllText(Output + "/repair.done",
            DateTime.Now.ToString("s") + "\nExisting marker repaired, no duplicate created.\n" +
            "Target=" + marker.targetDisplayName + "; hideForTarget=" + marker.hideForTarget + "\n" +
            "markerRoot=" + visual.name + "; compiled Udon program=" + AssetDatabase.GetAssetPath(program) + "\n" +
            "PASS Udon public markerRoot reference, compiled program, text bounds and glyphs.\n" +
            "Prefab saved. TEST scene remains unsaved. Live VRChat tracking not tested.\n");
    }

    private static void Label(Transform parent, string name, string content, Vector2 position,
        Vector2 size, int fontSize, Font font, Color color, float outlineWidth)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.025f, 0.03f, 1f);
        outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
    }

    private static void Validate(Text label)
    {
        Canvas.ForceUpdateCanvases();
        foreach (char c in label.text)
            if (!char.IsWhiteSpace(c) && !label.font.HasCharacter(c)) throw new Exception("Missing glyph: " + c);
        if (label.preferredWidth > label.rectTransform.rect.width || label.preferredHeight > label.rectTransform.rect.height)
            throw new Exception("Creator marker text overflow: " + label.name);
    }

    private static void Capture(RectTransform marker, string path)
    {
        // 안내 전용 레이어로 임시 격리 후 기존 레이어 복원
        var children = marker.GetComponentsInChildren<Transform>(true);
        var layers = children.Select(t => t.gameObject.layer).ToArray();
        foreach (var child in children) child.gameObject.layer = 31;
        var cameraObject = new GameObject("Creator marker preview camera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(marker.position - Vector3.forward * 3f, Quaternion.identity);
        camera.orthographic = true;
        camera.orthographicSize = 0.36f;
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.065f, 0.075f, 0.09f);
        var target = new RenderTexture(1600, 460, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1600, 460, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 460), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            for (int i = 0; i < children.Length; i++) children[i].gameObject.layer = layers[i];
        }
    }
}
