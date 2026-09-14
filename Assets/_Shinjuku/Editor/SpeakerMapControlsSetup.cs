using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UdonSharpEditor;
using VRC.Udon;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UdonSharp;
using VRC.SDK3.Components;
using UnityEngine.UI;

/// <summary>지도 병기와 범례형 아이콘 설정의 연결 및 편집 도구</summary>
[InitializeOnLoad]
public static class SpeakerMapControlsSetup
{
    private const string Output = "output/speaker-map-ui-20260912";
    static SpeakerMapControlsSetup() { EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string refresh = Output + "/controls-refresh.request";
        if (File.Exists(refresh)) { File.Delete(refresh); AssetDatabase.Refresh(); return; }
        string integrated = Output + "/integrated-settings.request";
        if (File.Exists(integrated))
        {
            File.Delete(integrated);
            try { ApplyIntegratedSettings(); }
            catch (Exception error) { File.WriteAllText(Output + "/integrated-settings.failed", error.ToString()); Debug.LogException(error); }
            return;
        }
        string bilingual = Output + "/bilingual-map.request";
        if (File.Exists(bilingual))
        {
            File.Delete(bilingual);
            try { ApplyBilingualMaps(); }
            catch (Exception error) { File.WriteAllText(Output + "/bilingual-map.failed", error.ToString()); Debug.LogException(error); }
            return;
        }
        string header = Output + "/remove-map-header.request";
        if (File.Exists(header))
        {
            File.Delete(header);
            try { RemoveMapHeader(); }
            catch (Exception error) { File.WriteAllText(Output + "/remove-map-header.failed", error.ToString()); }
            return;
        }
        string build = Output + "/controls-build.request";
        if (File.Exists(build))
        {
            File.Delete(build);
            try { Build(); }
            catch (Exception error) { File.WriteAllText(Output + "/controls-build.failed", error.ToString()); Debug.LogException(error); }
            return;
        }
        string restore = Output + "/controls-restore.request";
        if (File.Exists(restore))
        {
            File.Delete(restore);
            try { RestoreToggleReferences(); Inspect(); }
            catch (Exception error) { File.WriteAllText(Output + "/controls-restore.failed", error.ToString()); }
            return;
        }
        string request = Output + "/controls-inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Inspect(); }
        catch (Exception error) { File.WriteAllText(Output + "/controls-inspect.failed", error.ToString()); }
    }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static void Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        var report = new StringBuilder(scene.path + " dirty=" + scene.isDirty + "\n");
        foreach (var toggle in UnityEngine.Object.FindObjectsOfType<ObjectLocalToggle>(true).Where(t => t.gameObject.scene == scene))
        {
            report.AppendLine(PathOf(toggle.transform) + " active=" + toggle.gameObject.activeInHierarchy + " " + EditorJsonUtility.ToJson(toggle));
            var serialized = new SerializedObject(toggle);
            var targets = serialized.FindProperty("targetObjects");
            for (int i = 0; i < targets.arraySize; i++)
            {
                var target = targets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                report.AppendLine("TARGET " + (target ? PathOf(target.transform) + " active=" + target.activeSelf : "null"));
            }
        }
        foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).Where(t => t.gameObject.scene == scene && (PathOf(t.transform).ToLowerInvariant().Contains("switch") || PathOf(t.transform).ToLowerInvariant().Contains("button"))))
        {
            report.AppendLine("UDON " + PathOf(behaviour.transform) + " source=" + AssetDatabase.GetAssetPath(behaviour.programSource));
            foreach (string key in behaviour.publicVariables.VariableSymbols)
            {
                behaviour.publicVariables.TryGetVariableValue(key, out object value);
                report.AppendLine("  " + key + "=" + (value is GameObject go ? PathOf(go.transform) : value is GameObject[] objects ? string.Join(",", objects.Select(o => o ? PathOf(o.transform) : "null")) : value));
            }
        }
        foreach (var manager in UnityEngine.Object.FindObjectsOfType<TrafficSimulationManager>(true).Where(t => t.gameObject.scene == scene))
        {
            report.AppendLine("TRAFFIC " + PathOf(manager.transform) + " active=" + manager.gameObject.activeInHierarchy);
            foreach (var root in manager.vehicleRoots)
                report.AppendLine("VEHICLE " + (root ? PathOf(root) + " components=" + string.Join(",", root.GetComponentsInChildren<MonoBehaviour>(true).Select(c => c ? c.GetType().Name : "missing")) : "null"));
        }
        foreach (var map in UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(t => t.gameObject.scene == scene))
        {
            report.AppendLine("MAP " + PathOf(map.transform));
            foreach (var panel in map.GetComponents<SpeakerMapPanel>())
            {
                report.AppendLine("PANELREF " + EditorJsonUtility.ToJson(panel));
                var footer=panel.transform.Find("MapCanvas/LocalSettings");
                foreach (var item in footer.GetComponentsInChildren<RectTransform>(true))
                {
                    var graphic=item.GetComponent<UnityEngine.UI.Image>();
                    report.AppendLine("CONTROL " + PathOf(item) + " id="+item.GetInstanceID()+" active="+item.gameObject.activeSelf+" pos="+item.anchoredPosition+" size="+item.sizeDelta+" sprite="+(graphic?AssetDatabase.GetAssetPath(graphic.sprite):"")+" color="+(graphic?graphic.color.ToString():""));
                }
            }
            foreach (var text in map.GetComponentsInChildren<TextMeshProUGUI>(true))
                report.AppendLine("TEXT " + PathOf(text.transform) + " active=" + text.gameObject.activeInHierarchy + " text=" + text.text + " font=" + AssetDatabase.GetAssetPath(text.font));
            foreach (var canvas in map.GetComponentsInChildren<Canvas>(true))
                report.AppendLine("CANVAS " + PathOf(canvas.transform) + " " + string.Join(",", canvas.GetComponents<Component>().Select(c => c.GetType().FullName)));
        }
        File.WriteAllText(Output + "/controls-inspect.txt", report.ToString());
    }

    // 읽기 점검 중 빈 Udon backing 값을 가져온 세 프록시만 직전 복구본에서 복원
    private static void RestoreToggleReferences()
    {
        var targetScene = SceneManager.GetActiveScene();
        var live = UnityEngine.Object.FindObjectsOfType<ObjectLocalToggle>(true).Where(t => t.gameObject.scene == targetScene).ToArray();
        const string snapshotPath = "Assets/_Shinjuku/Editor/_SpeakerMapToggleReferenceSnapshot.unity";
        if (!File.Exists(snapshotPath)) File.Copy(Output + "/TEST.with-speaker-map.unity", snapshotPath);
        AssetDatabase.ImportAsset(snapshotPath, ImportAssetOptions.ForceSynchronousImport);
        var snapshot = EditorSceneManager.OpenScene(snapshotPath, OpenSceneMode.Additive);
        try
        {
            var originals = UnityEngine.Object.FindObjectsOfType<ObjectLocalToggle>(true).Where(t => t.gameObject.scene == snapshot).ToArray();
            foreach (var target in live)
            {
                var source = originals.Single(t => Route(t.transform) == Route(target.transform));
                var from = new SerializedObject(source); var to = new SerializedObject(target);
                to.FindProperty("activeDefault").boolValue = from.FindProperty("activeDefault").boolValue;
                var sourceTargets = from.FindProperty("targetObjects");
                var targetTargets = to.FindProperty("targetObjects");
                targetTargets.arraySize = sourceTargets.arraySize;
                for (int i = 0; i < sourceTargets.arraySize; i++)
                    targetTargets.GetArrayElementAtIndex(i).objectReferenceValue = Remap(sourceTargets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject, targetScene);
                foreach (string field in new[] { "SwitchOn", "SwitchOff" })
                    to.FindProperty(field).objectReferenceValue = Remap(from.FindProperty(field).objectReferenceValue as GameObject, targetScene);
                to.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(snapshot, true);
            SceneManager.SetActiveScene(targetScene);
            // 복제 씬의 Bakery 해제가 원본의 공유 라이트맵 슬롯까지 비울 수 있으므로 다시 연결.
            ftLightmaps.RefreshScene(targetScene);
            AssetDatabase.DeleteAsset(snapshotPath);
        }
        File.WriteAllText(Output + "/controls-restore.done", "Restored three toggle proxies from pre-existing recovery copy. No scene saved.");
    }
    private static string Route(Transform t) { return t.parent == null ? t.name : Route(t.parent) + "/" + t.GetSiblingIndex(); }
    private static GameObject Remap(GameObject source, Scene scene)
    {
        if (!source) return null;
        string route = Route(source.transform);
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => Route(t) == route).gameObject;
    }

    [Serializable] private class Translations { public string[] paths, english, japanese, korean; }
    private const string AssetFolder = "Assets/_Shinjuku/UI/SpeakerMap";
    private static void RemoveMapHeader()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var maps = UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m => m.gameObject.scene == scene).ToArray();
        if (maps.Length != 2) throw new InvalidOperationException("Expected two wall maps.");
        var lightmaps = LightmapSettings.lightmaps.Select(m => m.lightmapColor).ToArray();
        foreach (var map in maps)
        {
            var canvas = (RectTransform)map.transform.Find("MapCanvas");
            string calibration = EditorJsonUtility.ToJson(map);
            Vector3 floorPosition = canvas.Find("FloorPlan").position;
            Vector3 buttonsPosition = canvas.Find("LocalSettings").position;
            Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Remove map header");
            CompactHeader(canvas);
            foreach (var item in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(item)) continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
            }
            var collider = canvas.GetComponent<BoxCollider>();
            if (PrefabUtility.IsPartOfPrefabInstance(collider)) PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            if (calibration != EditorJsonUtility.ToJson(map) || Vector3.Distance(floorPosition, canvas.Find("FloorPlan").position) > .0001f || Vector3.Distance(buttonsPosition, canvas.Find("LocalSettings").position) > .0001f)
                throw new InvalidOperationException("Map calibration or button wall position changed.");
        }
        string path = AssetFolder + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { CompactHeader((RectTransform)prefab.transform.Find("MapCanvas")); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var map in maps)
        {
            var canvas = (RectTransform)map.transform.Find("MapCanvas");
            SpeakerMapSetup.VerifyPresentation(canvas, Output + "/" + map.name + "-headerless-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas, Output + "/" + map.name + "-headerless.png");
        }
        if (!LightmapSettings.lightmaps.Select(m => m.lightmapColor).SequenceEqual(lightmaps))
            throw new InvalidOperationException("Lightmap connections changed.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/remove-map-header.done", "PASS: both maps and prefab have no visible header; panel height 1400; floor/button world positions, map calibration and lightmaps preserved. Original scene unsaved.");
    }

    private static void CompactHeader(RectTransform canvas)
    {
        // 번역 참조는 보존하고 제목/설명/브랜드의 표시만 제거.
        foreach (string name in new[] { "Title", "Subtitle", "Brand", "TopAccent" })
        {
            var item = canvas.Find(name);
            if (!item) throw new InvalidOperationException("Missing map header: " + name);
            item.gameObject.SetActive(false);
        }
        if (Mathf.Abs(canvas.sizeDelta.y - 1280) < .01f || Mathf.Abs(canvas.sizeDelta.y - 1240) < .01f) return;
        float removedHeight = canvas.sizeDelta.y - 1400;
        if (Mathf.Abs(removedHeight) > .01f && Mathf.Abs(removedHeight - 180) > .01f)
            throw new InvalidOperationException("Unexpected map canvas size.");
        // 아래쪽과 지도/버튼의 실제 위치를 고정한 채 상단 180px만 줄임.
        canvas.position -= canvas.TransformVector(new Vector3(0, removedHeight * .5f, 0));
        foreach (RectTransform child in canvas)
            if (child.name != "Board" && child.name != "MetalFrame")
                child.anchoredPosition += new Vector2(0, removedHeight * .5f);
        canvas.sizeDelta = new Vector2(1960, 1400);
        var board = (RectTransform)canvas.Find("Board");
        var frame = (RectTransform)canvas.Find("MetalFrame");
        board.anchoredPosition = frame.anchoredPosition = Vector2.zero;
        board.sizeDelta = new Vector2(1960, 1400);
        frame.sizeDelta = new Vector2(2000, 1440);
        var collider = canvas.GetComponent<BoxCollider>();
        if (collider) collider.size = new Vector3(1960, 1400, 2);
    }
    private static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST in edit mode.");
        var maps = UnityEngine.Object.FindObjectsOfType<SpeakerMap>(true).Where(m => m.gameObject.scene == scene).OrderBy(m => m.name).ToArray();
        if (maps.Length != 2) throw new InvalidOperationException("Expected two maps.");
        var traffic = UnityEngine.Object.FindObjectsOfType<TrafficSimulationManager>(true).Single(t => t.gameObject.scene == scene);
        var pool = traffic.transform.parent.parent.Find("VehicleVisualPool");
        if (!pool || traffic.transform.IsChildOf(pool) || traffic.vehicleRoots.Any(t => !t || !t.IsChildOf(pool)))
            throw new InvalidOperationException("Vehicle pool must contain all vehicles and exclude simulation manager.");
        var ambientToggle = UnityEngine.Object.FindObjectsOfType<ObjectLocalToggle>(true).Single(t => t.gameObject.scene == scene && PathOf(t.transform) == "50_Audio/ambient sound button/Switch_MicTest");
        var targets = new SerializedObject(ambientToggle).FindProperty("targetObjects");
        var sources = Enumerable.Range(0, targets.arraySize)
            .Select(i => targets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject)
            .Where(t => t).SelectMany(t => t.GetComponentsInChildren<AudioSource>(true)).Distinct().ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("Ambient toggle has no connected audio sources.");
        var table = JsonUtility.FromJson<Translations>(File.ReadAllText(AssetFolder + "/MapLanguages.json"));
        foreach (var values in new[] { table.english, table.japanese, table.korean })
            if (values.Length != table.paths.Length || values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Incomplete translations.");
        EditorSceneManager.SaveScene(scene, Output + "/TEST.before-map-controls.unity", true);
        EnsurePrograms();
        var font = PrepareFont(table);
        string prefabPath = AssetFolder + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try { ConfigurePanel(prefab, table, font); PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var settings = UnityEngine.Object.FindObjectsOfType<WorldLocalSettings>(true).SingleOrDefault(t => t.gameObject.scene == scene);
        if (!settings)
        {
            var go = new GameObject("Map Local Settings");
            Undo.RegisterCreatedObjectUndo(go, "Create map local settings");
            settings = go.AddUdonSharpComponent<WorldLocalSettings>();
        }
        Undo.RecordObject(settings, "Connect map settings");
        settings.traffic = traffic; settings.ambientSources = sources;
        settings.vehiclesEnabled = true; settings.ambientEnabled = true;
        settings.panels = new SpeakerMapPanel[maps.Length];
        Undo.RecordObject(traffic, "Separate local traffic visuals");
        traffic.localVehicleVisualRoot = pool.gameObject;
        for (int i = 0; i < maps.Length; i++)
        {
            var map = maps[i];
            string before = EditorJsonUtility.ToJson(map);
            var canvas = map.transform.Find("MapCanvas");
            Vector3 position = canvas.position, scale = canvas.localScale;
            Quaternion rotation = canvas.rotation;
            Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Localize map and add local settings");
            var panel = ConfigurePanel(map.gameObject, table, font);
            panel.settings = settings; settings.panels[i] = panel;
            if (before != EditorJsonUtility.ToJson(map) || position != canvas.position || scale != canvas.localScale || rotation != canvas.rotation)
                throw new InvalidOperationException("Map calibration or wall placement changed.");
            Record(panel);
        }
        // 같은 부모 아래의 Ambient sound 음원은 유지하고 스위치/표시 메시만 숨김
        foreach (var oldControl in new[] { ambientToggle.transform,
            ambientToggle.transform.parent.Find("MicTestSwitchObject/Switch_MicTest_On"),
            ambientToggle.transform.parent.Find("MicTestSwitchObject/Switch_MicTest_Off") })
        {
            if (!oldControl) continue;
            Undo.RecordObject(oldControl.gameObject, "Move ambient control to maps");
            oldControl.gameObject.SetActive(false);
            if (PrefabUtility.IsPartOfPrefabInstance(oldControl.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(oldControl.gameObject);
        }
        Record(settings); Record(traffic);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms();
        foreach (var panel in settings.panels) UdonSharpEditorUtility.CopyProxyToUdon(panel);
        UdonSharpEditorUtility.CopyProxyToUdon(settings);
        UdonSharpEditorUtility.CopyProxyToUdon(traffic);
        Verify(settings, table);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/controls-build.done", "Two maps connected. 3 languages / " + table.paths.Length + " labels. Ambient sources=" + sources.Length + ". Manager remains active. Original scene unsaved.\n");
    }
    private static void ApplyBilingualMaps()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var panels = UnityEngine.Object.FindObjectsOfType<SpeakerMapPanel>(true).Where(p => p.gameObject.scene == scene).ToArray();
        if (panels.Length != 2 || panels.Any(p => !p.settings)) throw new InvalidOperationException("Expected two connected map panels.");
        var table = JsonUtility.FromJson<Translations>(File.ReadAllText(AssetFolder + "/MapLanguages.json"));
        EnsurePrograms();
        var font = PrepareFont(table);
        var shared = UnityEngine.Object.FindObjectsOfType<WorldLanguage>(true).SingleOrDefault(p => p.gameObject.scene == scene);
        if (!shared)
        {
            var go = new GameObject("World Language");
            Undo.RegisterCreatedObjectUndo(go, "Create shared language service");
            shared = go.AddUdonSharpComponent<WorldLanguage>();
        }
        var exhibitions = UnityEngine.Object.FindObjectsOfType<ExhibitionLanguage>(true).Where(p => p.gameObject.scene == scene).ToArray();
        Undo.RecordObject(shared, "Connect language consumers");
        foreach (var exhibition in exhibitions)
        {
            Undo.RecordObject(exhibition, "Use shared world language");
            exhibition.worldLanguage = shared;
            shared.Register(exhibition);
            Record(exhibition);
        }
        Record(shared);
        string prefabPath = AssetFolder + "/SpeakerMap.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            StyleBilingualPanel(prefab.GetComponent<SpeakerMapPanel>(), table, font);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var report = new StringBuilder();
        foreach (var panel in panels)
        {
            var map = panel.GetComponent<SpeakerMap>();
            string calibration = EditorJsonUtility.ToJson(map);
            var canvas = (RectTransform)panel.transform.Find("MapCanvas");
            Vector3 position = canvas.position;
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Japanese English map captions");
            StyleBilingualPanel(panel, table, font);
            if (calibration != EditorJsonUtility.ToJson(map) || position != canvas.position) throw new InvalidOperationException("Map calibration changed.");
            foreach (var label in panel.labels.Concat(new[] { panel.vehicleLabel, panel.ambientLabel }))
            {
                label.ForceMeshUpdate(true);
                string plain = label.GetParsedText();
                if (label.isTextOverflowing || !label.font.HasCharacters(plain)) throw new InvalidOperationException("Bilingual overflow or missing glyphs: " + label.name);
                report.AppendLine(panel.name + "/" + label.name + ": " + plain.Replace("\n", " / "));
            }
            Record(panel);
            SpeakerMapSetup.VerifyPresentation(canvas, Output + "/" + panel.name + "-bilingual-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas, Output + "/" + panel.name + "-bilingual.png");
        }
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms();
        report.AppendLine("Shared language services=1; exhibition consumers=" + exhibitions.Length + "; map locale listeners=0.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-map.unity", true);
        File.WriteAllText(Output + "/bilingual-map.done", report.ToString());
    }

    private static void StyleBilingualPanel(SpeakerMapPanel panel, Translations table, TMP_FontAsset font)
    {
        if (!panel) throw new InvalidOperationException("Missing map panel.");
        var canvas = panel.transform.Find("MapCanvas");
        panel.labels = table.paths.Select(path => canvas.Find(path).GetComponent<TextMeshProUGUI>()).ToArray();
        panel.japanese = table.japanese; panel.english = table.english;
        foreach (var label in panel.labels.Concat(new[] { panel.vehicleLabel, panel.ambientLabel }))
        {
            label.font = font; label.fontSharedMaterial = font.material;
            label.richText = true; label.enableAutoSizing = false; label.enableWordWrapping = false;
            label.lineSpacing = 0; label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, Mathf.Max(label.rectTransform.sizeDelta.y, label.fontSize * 2.7f));
            EditorUtility.SetDirty(label);
        }
        panel._RefreshLabels();
        // 두 줄 문구가 지도의 실제 기둥 표시를 가리지 않도록 빈 공간에 배치.
        ((RectTransform)canvas.Find("FloorPlan/OppositeWalkway")).anchoredPosition = new Vector2(0, 282);
        ((RectTransform)canvas.Find("FloorPlan/IndoorPassage")).anchoredPosition = new Vector2(185, -257);
        StyleIntegratedSettings(panel);
        Record(panel);
    }

    private static Sprite ControlIcon(string name)
    {
        string path = AssetFolder + "/" + name + "Control.png";
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void IntegratedButton(SpeakerMapPanel panel, string name, float x, float width, Sprite icon, out GameObject offMark)
    {
        var footer = panel.transform.Find("MapCanvas/LocalSettings");
        var buttonRect = Rect(footer, name, new Vector2(x, -20), new Vector2(width, 128));
        var button = buttonRect.GetComponent<UnityEngine.UI.Button>();
        var background = buttonRect.GetComponent<UnityEngine.UI.Image>();
        background.color = Color.clear; background.raycastTarget = true;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(.91f,.96f,.93f,1);
        colors.pressedColor = new Color(.76f,.86f,.80f,1);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        var stateRect = (RectTransform)(name == "Vehicles" ? panel.vehicleState : panel.ambientState).transform;
        var state = stateRect.GetComponent<UnityEngine.UI.Image>();
        state.sprite = icon; state.preserveAspect = true; state.raycastTarget = false;
        var slash = Rect(stateRect, "OffMark", Vector2.zero, new Vector2(48,3));
        slash.localRotation = Quaternion.Euler(0,0,45);
        var slashImage = slash.GetComponent<UnityEngine.UI.Image>();
        if (!slashImage) slashImage = slash.gameObject.AddComponent<UnityEngine.UI.Image>();
        slashImage.color = new Color(.55f,.59f,.62f); slashImage.raycastTarget = false;
        offMark = slash.gameObject;
        var caption = buttonRect.Find("Caption").GetComponent<TextMeshProUGUI>();
        caption.rectTransform.anchoredPosition = new Vector2(0,28);
        caption.rectTransform.sizeDelta = new Vector2(width-28,72);
        caption.fontSize = 26; caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.raycastTarget = false;
        var trackRect = Rect(buttonRect,"Switch",new Vector2(-width*.5f+76,-30),new Vector2(124,52));
        // Earlier scene-added controls and prefab controls can coexist. Keep the wired
        // switch, hide only redundant visual copies (recoverable, no reference deletion).
        foreach (Transform child in buttonRect)
            if (child.name == "Switch")
            {
                child.gameObject.SetActive(child == trackRect);
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }
        var track = trackRect.GetComponent<UnityEngine.UI.Image>();
        if (!track) track = trackRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        track.sprite = FlatSwitchSprite(false); track.type = UnityEngine.UI.Image.Type.Simple; track.raycastTarget = false;
        var thumbRect = Rect(trackRect,"Thumb",Vector2.zero,new Vector2(62,52));
        var thumb = thumbRect.GetComponent<UnityEngine.UI.Image>();
        if (!thumb) thumb = thumbRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        thumb.sprite = FlatSwitchSprite(true); thumb.type = UnityEngine.UI.Image.Type.Simple;
        thumb.color = Color.white; thumb.raycastTarget = false;
        button.targetGraphic = thumb;
        stateRect.SetParent(trackRect,false);
        stateRect.anchorMin = stateRect.anchorMax = stateRect.pivot = new Vector2(.5f,.5f);
        stateRect.sizeDelta = new Vector2(36,36);
        stateRect.SetAsLastSibling();
        var switchLabel = Label(trackRect,"Status",Vector2.zero,new Vector2(56,36),19,caption.font);
        switchLabel.alignment = TextAlignmentOptions.Center; switchLabel.raycastTarget = false;
        switchLabel.gameObject.SetActive(false);
        if (name == "Vehicles")
        {
            panel.vehicleSwitchTrack = track; panel.vehicleSwitchThumb = thumbRect; panel.vehicleSwitchLabel = switchLabel;
        }
        else
        {
            panel.ambientSwitchTrack = track; panel.ambientSwitchThumb = thumbRect; panel.ambientSwitchLabel = switchLabel;
        }
    }

    // Solid, antialiased UI geometry: no built-in bevel, inset border, or shadow.
    private static Sprite FlatSwitchSprite(bool half)
    {
        string path = AssetFolder + (half ? "/SwitchThumbFlat.png" : "/SwitchTrackFlat.png");
        if (!File.Exists(path))
        {
            int width = half ? 248 : 496, height = 208;
            var texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
            var pixels = new Color32[width*height];
            const float radius = 48;
            for (int y=0;y<height;y++) for (int x=0;x<width;x++)
            {
                float px=x+.5f, py=y+.5f;
                float cx=half ? Mathf.Max(px,radius) : Mathf.Clamp(px,radius,width-radius);
                float cy=Mathf.Clamp(py,radius,height-radius);
                float distance=Mathf.Sqrt((px-cx)*(px-cx)+(py-cy)*(py-cy));
                pixels[y*width+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(255*Mathf.Clamp01(radius-distance+.5f)));
            }
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.mipmapEnabled=false; importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void StyleIntegratedSettings(SpeakerMapPanel panel)
    {
        if(panel.settingsTitle){SpeakerMapCompactSettings.RefreshLayout(panel);return;}
        var canvas = (RectTransform)panel.transform.Find("MapCanvas");
        var floor = (RectTransform)canvas.Find("FloorPlan");
        // 지도 크기와 모든 내용의 월드 위치를 보존하고 흰 여백만 사방 20px로 축소.
        if ((!Mathf.Approximately(floor.sizeDelta.x,1800) && !Mathf.Approximately(floor.sizeDelta.x,1610)) || (!Mathf.Approximately(floor.sizeDelta.y,1200) && !Mathf.Approximately(floor.sizeDelta.y,1110)) ||
            (!Mathf.Approximately(canvas.sizeDelta.y,1400) &&
             !Mathf.Approximately(canvas.sizeDelta.y,1280) &&
             !Mathf.Approximately(canvas.sizeDelta.y,1240) &&
             !Mathf.Approximately(canvas.sizeDelta.y,1150)))
            throw new InvalidOperationException("Unexpected map size for narrow border.");
        Vector2 center = floor.anchoredPosition;
        canvas.position += canvas.TransformVector(new Vector3(center.x,center.y,0));
        foreach (RectTransform child in canvas)
            if (child.name != "Board" && child.name != "MetalFrame") child.anchoredPosition -= center;
        float boardWidth = floor.sizeDelta.x + 40;
        float boardHeight = floor.sizeDelta.y + 40;
        canvas.sizeDelta = new Vector2(boardWidth,boardHeight);
        var board = (RectTransform)canvas.Find("Board");
        var frame = (RectTransform)canvas.Find("MetalFrame");
        board.anchoredPosition = frame.anchoredPosition = Vector2.zero;
        board.sizeDelta = new Vector2(boardWidth,boardHeight);
        frame.sizeDelta = new Vector2(boardWidth+40,boardHeight+40);
        canvas.GetComponent<BoxCollider>().size = new Vector3(boardWidth,boardHeight,2);
        var footer = Rect(canvas,"LocalSettings",new Vector2((1800-floor.sizeDelta.x)*.5f,floor.anchoredPosition.y-floor.sizeDelta.y*.5f+120),new Vector2(1800,200));
        var backdrop = footer.Find("MapMargin");
        if (backdrop) backdrop.gameObject.SetActive(false);
        // 내용 폭에 맞춘 클릭 영역 사이 12px 간격. 제목은 아이콘 행 바로 위에 배치.
        IntegratedButton(panel,"Vehicles",-794,144,ControlIcon("Vehicle"),out GameObject vehicleOff);
        IntegratedButton(panel,"Ambient",-630,160,ControlIcon("Ambient"),out GameObject ambientOff);
        panel.vehicleOffMark = vehicleOff; panel.ambientOffMark = ambientOff;
        var notice = footer.Find("LocalOnly").GetComponent<TextMeshProUGUI>();
        notice.rectTransform.anchoredPosition = new Vector2(-752,84);
        notice.rectTransform.sizeDelta = new Vector2(200,40);
        notice.fontSize = 23; notice.alignment = TextAlignmentOptions.MidlineLeft;
        notice.color = new Color(.46f,.51f,.54f);
        // 사용자가 지정한 영문 제목은 자동 병기 대상에서 제외.
        int[] keep = Enumerable.Range(0,panel.labels.Length).Where(i=>panel.labels[i]!=notice).ToArray();
        panel.english = keep.Select(i=>panel.english[i]).ToArray();
        panel.japanese = keep.Select(i=>panel.japanese[i]).ToArray();
        panel.labels = keep.Select(i=>panel.labels[i]).ToArray();
        notice.text = "Local Settings";
        var outlineRect = Rect(footer,"SettingsOutline",new Vector2(-708,-6),new Vector2(336,180));
        var outline = outlineRect.GetComponent<UnityEngine.UI.Image>();
        if (!outline) outline = outlineRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        outline.sprite = SettingsOutlineSprite(24+notice.GetPreferredValues().x+10);
        outline.type = UnityEngine.UI.Image.Type.Simple;
        outline.color = new Color(.62f,.67f,.70f,1); outline.raycastTarget = false;
        outlineRect.SetAsFirstSibling();
        HideDuplicateSettingsOutlines(footer);
        panel._RefreshLabels();
        Record(panel);
    }

    private static void HideDuplicateSettingsOutlines(Transform footer)
    {
        var keep = footer.Find("SettingsOutline");
        foreach (Transform child in footer)
            if (child.name == "SettingsOutline")
            {
                child.gameObject.SetActive(child == keep);
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }
    }

    private static Sprite SettingsOutlineSprite(float gapRight)
    {
        const string path = AssetFolder + "/SettingsOutline.png";
        if (!File.Exists(path))
        {
            const int width=1344,height=720;
            const float radius=56,stroke=6;
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var pixels=new Color32[width*height];
            for (int y=0;y<height;y++) for (int x=0;x<width;x++)
            {
                float px=x+.5f,py=y+.5f;
                float dx=px-Mathf.Clamp(px,radius,width-radius),dy=py-Mathf.Clamp(py,radius,height-radius);
                float distance=Mathf.Sqrt(dx*dx+dy*dy);
                float alpha=Mathf.Clamp01(radius-distance+.5f)*Mathf.Clamp01(distance-(radius-stroke)+.5f);
                // The title opening is genuinely transparent, not covered by a background patch.
                if (py>height-stroke-1 && px>=14*4 && px<=gapRight*4) alpha=0;
                pixels[y*width+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(alpha*255));
            }
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType!=TextureImporterType.Sprite)
        {
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.mipmapEnabled=false; importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=2048;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ApplyIntegratedSettings()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Shinjuku/Scenes/TEST_PC.unity") throw new InvalidOperationException("Open TEST.");
        var panels = UnityEngine.Object.FindObjectsOfType<SpeakerMapPanel>(true).Where(p=>p.gameObject.scene==scene).ToArray();
        if (panels.Length != 2) throw new InvalidOperationException("Expected two map panels.");
        string path = AssetFolder + "/SpeakerMap.prefab";
        foreach (var panel in panels)
        {
            string mapBefore = EditorJsonUtility.ToJson(panel.GetComponent<SpeakerMap>());
            Vector3 floorBefore = panel.transform.Find("MapCanvas/FloorPlan").position;
            var settings = panel.settings;
            bool vehicles = settings.vehiclesEnabled, ambient = settings.ambientEnabled;
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject,"Integrate settings into map margin");
            StyleIntegratedSettings(panel);
            if (mapBefore != EditorJsonUtility.ToJson(panel.GetComponent<SpeakerMap>()) || Vector3.Distance(floorBefore,panel.transform.Find("MapCanvas/FloorPlan").position) > .0001f || vehicles != settings.vehiclesEnabled || ambient != settings.ambientEnabled)
                throw new InvalidOperationException("Map calibration or user settings changed.");
            var canvas = (RectTransform)panel.transform.Find("MapCanvas");
            SpeakerMapSetup.VerifyPresentation(canvas,Output+"/"+panel.name+"-integrated-layout.txt");
            SpeakerMapSetup.CapturePanel(canvas,Output+"/"+panel.name+"-integrated.png");
            // 표시만 임시 OFF로 검토. 실제 차량·음원과 사용자 설정은 건드리지 않음.
            var previewHost = new GameObject("Settings appearance preview") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var preview = previewHost.AddComponent<WorldLocalSettings>();
                preview.vehiclesEnabled = false; preview.ambientEnabled = false;
                panel.settings = preview; panel._RefreshSettings();
                SpeakerMapSetup.CapturePanel(canvas,Output+"/"+panel.name+"-integrated-off.png");
            }
            finally { panel.settings = settings; panel._RefreshSettings(); UnityEngine.Object.DestroyImmediate(previewHost); }
        }
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try { StyleIntegratedSettings(prefab.GetComponent<SpeakerMapPanel>()); PrefabUtility.SaveAsPrefabAsset(prefab,path); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var panel in panels) HideDuplicateSettingsOutlines(panel.transform.Find("MapCanvas/LocalSettings"));
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene,Output+"/TEST.with-speaker-map.unity",true);
        File.WriteAllText(Output+"/integrated-settings.done","Both map footers integrated. Original map calibration and toggle states preserved; original TEST scene unsaved.");
    }

    private static void Record(UdonSharpBehaviour behaviour)
    {
        EditorUtility.SetDirty(behaviour);
        if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
    }
    private static void EnsurePrograms()
    {
        string[] names = { "SpeakerMapPanel", "WorldLocalSettings", "WorldLanguage" };
        var programs = new UdonSharpProgramAsset[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string path = "Assets/_Shinjuku/Scripts/UI/" + names[i] + ".asset";
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            if (!program)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Shinjuku/Scripts/UI/" + names[i] + ".cs");
                AssetDatabase.CreateAsset(program, path);
            }
            programs[i] = program;
        }
        UdonSharpProgramAsset.CompileAllCsPrograms();
        foreach (var program in programs) program.UpdateProgram();
    }
    private static TMP_FontAsset PrepareFont(Translations table)
    {
        const string path = AssetFolder + "/MapInterfaceLabels.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(AssetFolder + "/NotoSansCJKjp-Regular.otf");
            font = TMP_FontAsset.CreateFontAsset(source, 50, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            font.name = "MapInterfaceLabels"; font.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(font, path);
        }
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        var fontData = new SerializedObject(font);
        fontData.FindProperty("m_SourceFontFile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(AssetFolder + "/NotoSansCJKjp-Regular.otf");
        fontData.ApplyModifiedPropertiesWithoutUndo();
        string text = string.Join("", table.english.Concat(table.japanese).Concat(table.korean)) + "차량환경음車両環境音VehiclesAmbient sound ON OFF_…";
        string pending = new string(text.Where(c => c != '\n' && c != '\r' && !font.HasCharacter(c)).Distinct().ToArray());
        if (pending.Length > 0 && !font.TryAddCharacters(pending, out string missing)) throw new InvalidOperationException("Missing localized glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var atlas in font.atlasTextures) if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssetIfDirty(font);
        if (!AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Any()) throw new InvalidOperationException("Font material was not saved.");
        return font;
    }
    private static SpeakerMapPanel ConfigurePanel(GameObject root, Translations table, TMP_FontAsset font)
    {
        var canvas = (RectTransform)root.transform.Find("MapCanvas");
        var panel = root.GetComponent<SpeakerMapPanel>();
        if (!panel) panel = root.AddUdonSharpComponent<SpeakerMapPanel>();
        var footer = Rect(canvas, "LocalSettings", new Vector2(0, -canvas.sizeDelta.y * .5f + 82), new Vector2(1800, 112));
        Label(footer, "LocalOnly", new Vector2(-675, 0), new Vector2(450, 75), 29, font);
        panel.vehicleLabel = Button(footer, "Vehicles", new Vector2(-190, 0), panel, "_ToggleVehicles", font, out Image vehicleState);
        panel.ambientLabel = Button(footer, "Ambient", new Vector2(540, 0), panel, "_ToggleAmbient", font, out Image ambientState);
        panel.vehicleState = vehicleState; panel.ambientState = ambientState;
        if (!canvas.GetComponent<GraphicRaycaster>()) canvas.gameObject.AddComponent<GraphicRaycaster>();
        if (!canvas.GetComponent<VRCUiShape>()) canvas.gameObject.AddComponent<VRCUiShape>();
        var box = canvas.GetComponent<BoxCollider>();
        if (!box) box = canvas.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(canvas.sizeDelta.x, canvas.sizeDelta.y, 2); box.isTrigger = true;
        // VRChat excludes the UI layer while its menu is closed, even when GraphicRaycaster succeeds.
        canvas.gameObject.layer = LayerMask.NameToLayer("Default");
        foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
            if (child.gameObject.layer == LayerMask.NameToLayer("UI")) child.gameObject.layer = LayerMask.NameToLayer("Default");
        panel.labels = table.paths.Select(path => canvas.Find(path).GetComponent<TextMeshProUGUI>()).ToArray();
        panel.english = table.english; panel.japanese = table.japanese;
        foreach (var label in panel.labels)
        {
            label.font = font; label.fontSharedMaterial = font.material; label.raycastTarget = false;
            label.enableAutoSizing = false; label.enableWordWrapping = false;
            label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, Mathf.Max(label.rectTransform.sizeDelta.y, label.fontSize * 1.7f));
        }
        // 영문 방향명이 길어져도 인접한 지도 표식과 겹치지 않도록 두 줄 사용
        foreach (string name in new[] { "SpawnGate", "OppositeGate" })
        {
            var label = canvas.Find("FloorPlan/" + name).GetComponent<TextMeshProUGUI>();
            label.rectTransform.sizeDelta = new Vector2(name == "SpawnGate" ? 340 : 420, 100);
            label.fontSize = 28; label.enableWordWrapping = true;
        }
        var bridge = canvas.Find("FloorPlan/BridgeLabel").GetComponent<TextMeshProUGUI>();
        bridge.rectTransform.sizeDelta = new Vector2(400, 50); bridge.fontSize = 26;
        StyleBilingualPanel(panel, table, font);
        CompactHeader(canvas);
        Record(panel);
        return panel;
    }
    private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect) { rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); }
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static TextMeshProUGUI Label(Transform parent, string name, Vector2 position, Vector2 size, float pointSize, TMP_FontAsset font)
    {
        var rect = Rect(parent, name, position, size);
        var label = rect.GetComponent<TextMeshProUGUI>();
        if (!label) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSharedMaterial = font.material; label.fontSize = pointSize;
        label.color = new Color(.24f, .30f, .34f); label.alignment = TextAlignmentOptions.Midline;
        label.raycastTarget = false; label.enableAutoSizing = false; label.enableWordWrapping = false;
        return label;
    }
    private static TextMeshProUGUI Button(Transform parent, string name, Vector2 position, SpeakerMapPanel panel, string method, TMP_FontAsset font, out Image state)
    {
        var rect = Rect(parent, name, position, new Vector2(680, 100));
        var image = rect.GetComponent<Image>(); if (!image) image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.94f, .96f, .95f); image.raycastTarget = true;
        var button = rect.GetComponent<Button>(); if (!button) button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(.84f, .93f, .88f); colors.pressedColor = new Color(.71f, .87f, .77f); button.colors = colors;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        while (button.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(button.onClick, 0);
        UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(panel).SendCustomEvent, method);
        var bar = Rect(rect, "State", new Vector2(-312, 0), new Vector2(8, 54));
        state = bar.GetComponent<Image>(); if (!state) state = bar.gameObject.AddComponent<Image>(); state.raycastTarget = false;
        return Label(rect, "Caption", Vector2.zero, new Vector2(600, 84), 34, font);
    }
    private static void Verify(WorldLocalSettings settings, Translations table)
    {
        var report = new StringBuilder();
        foreach (var panel in settings.panels)
        {
            var canvas = (RectTransform)panel.transform.Find("MapCanvas");
            foreach (string language in new[] { "ko", "ja", "en" })
            {
                panel.OnLanguageChanged(language);
                foreach (var label in panel.labels.Concat(new[] { panel.vehicleLabel, panel.ambientLabel }))
                {
                    label.ForceMeshUpdate();
                    if (label.isTextOverflowing) throw new InvalidOperationException(language + " overflow: " + label.name);
                    if (!label.font.HasCharacters(label.GetParsedText())) throw new InvalidOperationException(language + " missing glyphs: " + label.name);
                }
                SpeakerMapSetup.VerifyPresentation(canvas, Output + "/" + panel.name + "-" + language + "-layout.txt");
                SpeakerMapSetup.CapturePanel(canvas, Output + "/" + panel.name + "-" + language + "-controls.png");
                report.AppendLine(panel.name + " / " + language + " passed glyph and layout checks.");
            }
            panel.OnLanguageChanged("fr");
            if (panel.labels[0].text != panel.Bilingual(table.japanese[0], table.english[0])) throw new InvalidOperationException("Fixed bilingual text changed with locale.");
            panel.OnLanguageChanged("ko"); Record(panel);
        }
        if (!settings.traffic.gameObject.activeInHierarchy) throw new InvalidOperationException("Traffic manager disabled.");
        if (settings.panels.Any(p => p.settings != settings)) throw new InvalidOperationException("Settings are not shared.");
        File.WriteAllText(Output + "/controls-validation.txt", report.ToString());
    }
}
