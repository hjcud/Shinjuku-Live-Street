using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

/// <summary>
/// 전시 씬에 기존 저장 서비스와 환경음 스위치를 연결. 다른 열린 씬은 변경하지 않음
/// </summary>
[InitializeOnLoad]
public static class ExhibitionPlayerDataSetup
{
    private const string ScenePath = "Assets/_ShinjukuExhibition/Scenes/TechnicalExhibition.unity";
    private const string Output = "output/player-persistence";

    static ExhibitionPlayerDataSetup() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string apply = Output + "/exhibition-apply.request";
        bool applying = File.Exists(apply);
        string request = applying ? apply : Output + "/exhibition-inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        if (File.Exists(Output + "/exhibition.failed")) File.Delete(Output + "/exhibition.failed");
        try
        {
            if (applying) Apply();
            else Inspect();
        }
        catch (Exception error)
        {
            File.WriteAllText(Output + "/exhibition.failed", error.ToString());
            Debug.LogException(error);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }

    private static string PathOf(Transform t)
    {
        return t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    }

    [MenuItem("Tools/Shinjuku/Connect exhibition player persistence")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        Directory.CreateDirectory(Output);
        var original = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool completed = false;
        try
        {
            var speakers = Components<SpeakerController>(scene);
            var traffic = Components<TrafficSimulationManager>(scene).Single();
            var pool = traffic.transform.parent.parent.Find("VehicleVisualPool");
            if (!pool || traffic.transform.IsChildOf(pool) || traffic.vehicleRoots.Any(t => !t || !t.IsChildOf(pool)))
                throw new InvalidOperationException("Vehicle visuals must be separate from the traffic manager.");
            var ambient = Components<ObjectLocalToggle>(scene).Single(t => PathOf(t.transform) == "50_Audio/ambient sound button/Switch_MicTest");
            var targets = new SerializedObject(ambient).FindProperty("targetObjects");
            var audio = Enumerable.Range(0, targets.arraySize)
                .Select(i => targets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject)
                .Where(t => t).SelectMany(t => t.GetComponentsInChildren<AudioSource>(true)).Distinct().ToArray();
            if (speakers.Length != 5 || audio.Length == 0 || !ambient.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Expected five exhibition speakers and an active ambient switch.");
            var lockedAudio = Components<ExhibitionMediaLock>(scene).SelectMany(l => l.mediaAudio ?? new AudioSource[0]).ToArray();
            if (audio.Intersect(lockedAudio).Any())
                throw new InvalidOperationException("Ambient settings must not control exhibition-locked media.");
            bool wasDirty = scene.isDirty;
            string backup = Output + "/TechnicalExhibition.before-player-data-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            // 지정된 전시 씬은 적용 후 저장하되, 디스크 원본과 미저장 상태를 각각 보존한다.
            File.Copy(ScenePath, backup + ".disk.unity", false);
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Backup failed.");
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("UdonSharp compilation failed.");

            var tracker = Components<WorldPlayerData>(scene).SingleOrDefault();
            if (!tracker)
            {
                var root = new GameObject("World Player Data");
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Add exhibition persistence");
                tracker = root.AddUdonSharpComponent<WorldPlayerData>();
            }
            var settings = Components<WorldLocalSettings>(scene).SingleOrDefault();
            if (!settings) settings = tracker.gameObject.AddUdonSharpComponent<WorldLocalSettings>();
            if (!tracker.gameObject.activeInHierarchy || !settings.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Persistence and settings must remain active.");
            Undo.RecordObjects(new UnityEngine.Object[] { tracker, settings, traffic, ambient }, "Connect exhibition persistence");
            tracker.settings = settings;
            settings.playerData = tracker;
            settings.traffic = traffic;
            settings.ambientSources = audio;
            settings.panels = Components<SpeakerMapPanel>(scene);
            settings.ambientSwitch = ambient;
            ambient.worldSettings = settings;
            ambient.controlsVehicles = false;
            traffic.localVehicleVisualRoot = pool.gameObject;
            settings.vehicleSwitch = ConnectVehicleSwitch(ambient, settings);
            foreach (var item in new UdonSharpBehaviour[] { tracker, settings, traffic, ambient }) Sync(item);
            foreach (var speaker in speakers)
            {
                Undo.RecordObject(speaker, "Connect exhibition speaker persistence");
                speaker.playerData = tracker;
                Sync(speaker);
                CheckReference(speaker, "playerData", tracker);
            }
            CheckReference(tracker, "settings", settings);
            CheckReference(settings, "playerData", tracker);
            CheckReference(settings, "traffic", traffic);
            CheckReference(settings, "ambientSwitch", ambient);
            CheckReference(ambient, "worldSettings", settings);
            CheckReference(settings, "vehicleSwitch", settings.vehicleSwitch);
            CheckReference(settings.vehicleSwitch, "worldSettings", settings);
            var program = (UdonSharpProgramAsset)UdonSharpEditorUtility.GetBackingUdonBehaviour(tracker).programSource;
            if (!program.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_onPlayerRestored"))
                throw new InvalidOperationException("Missing restore entry point.");
            // 이번에 갱신한 공통 프로그램만 저장하며 다른 편집 중 에셋을 일괄 저장하지 않는다.
            foreach (var behaviour in new UdonSharpBehaviour[] { tracker, settings, ambient })
            {
                var asset = (UdonSharpProgramAsset)UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour).programSource;
                AssetDatabase.SaveAssetIfDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset.SerializedProgramAsset);
            }
            VerifyInteraction(scene, ambient);
            VerifyInteraction(scene, settings.vehicleSwitch);
            CaptureSwitches(scene, ambient.transform.parent, 1f, "exhibition-switches-front.png");
            try
            {
                ambient._ApplySettingState(false);
                settings.vehicleSwitch._ApplySettingState(false);
                CaptureSwitches(scene, ambient.transform.parent, 1f, "exhibition-switches-off.png");
            }
            finally
            {
                ambient._ApplySettingState(settings.ambientEnabled);
                settings.vehicleSwitch._ApplySettingState(settings.vehiclesEnabled);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            string savedPath = ScenePath;
            if (!EditorSceneManager.SaveScene(scene, savedPath)) throw new IOException("Could not save exhibition scene.");
            completed = true;
            File.WriteAllText(Output + "/exhibition-apply.done",
                "PASS: actual UdonSharp compile and backing references / restore entry.\n" +
                "Scene: " + ScenePath + "; initially dirty: " + wasDirty + "\n" +
                "Trackers: 1; settings: 1; speakers: " + speakers.Length + "; ambient sources: " + audio.Length + "\n" +
                "Ambient: existing switch connected; vehicle visuals: existing pool connected.\n" +
                "Vehicle switch added beside ambient; both linked to saved settings. Separate hitboxes and front interaction rays passed; ON/OFF previews captured.\n" +
                "Exhibition media lock preserved. Saved: " + savedPath + "\nBackup: " + backup + "\n" +
                "Other scenes left unchanged. Live VRChat rejoin test not performed.\n");
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            // 오류 시 열린 씬은 남겨 부분 적용 상태를 확인할 수 있게 한다.
            if (opened && completed)
            {
                // UdonSharp가 예약한 컴포넌트 초기화 뒤 닫아 지연 콜백의 파괴된 참조 접근 방지.
                EditorApplication.delayCall += () =>
                {
                    if (scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                };
            }
        }
    }

    private static ObjectLocalToggle ConnectVehicleSwitch(ObjectLocalToggle ambient, WorldLocalSettings settings)
    {
        var frame = ambient.transform.parent;
        var root = frame.parent.Find("Vehicle settings switch");
        if (!root)
        {
            var host = new GameObject("Vehicle settings switch");
            Undo.RegisterCreatedObjectUndo(host, "Add vehicle settings switch");
            root = host.transform;
            root.SetParent(frame.parent, false);
            root.SetPositionAndRotation(frame.position + frame.right * 0.65f, frame.rotation);
            root.localScale = frame.localScale;
            // 스위치와 ON/OFF 메시만 복제. 환경음 AudioSource 및 주변 장식은 복제하지 않음.
            var button = UnityEngine.Object.Instantiate(ambient.gameObject, root);
            button.name = "Switch_Vehicles";
            var control = button.GetComponent<ObjectLocalToggle>();
            var source = new SerializedObject(ambient);
            var destination = new SerializedObject(control);
            foreach (string key in new[] { "SwitchOn", "SwitchOff" })
            {
                var original = (GameObject)source.FindProperty(key).objectReferenceValue;
                var visual = UnityEngine.Object.Instantiate(original, root);
                visual.name = key;
                visual.transform.SetPositionAndRotation(root.TransformPoint(frame.InverseTransformPoint(original.transform.position)), original.transform.rotation);
                destination.FindProperty(key).objectReferenceValue = visual;
            }
            destination.FindProperty("targetObjects").arraySize = 0;
            destination.ApplyModifiedProperties();

        }
        var result = root.GetComponentInChildren<ObjectLocalToggle>(true);
        result.worldSettings = settings;
        result.controlsVehicles = true;
        result._ApplySettingState(settings.vehiclesEnabled);
        foreach (var control in new[] { ambient, result })
        {
            var serialized = new SerializedObject(control);
            var controlFrame = control.transform.parent;
            var geometry = SwitchBounds(control, serialized);
            float front = geometry.max.z + 0.012f;
            Label(controlFrame, control == ambient ? "Ambient setting label" : "Vehicle setting label",
                control == ambient ? "AMBIENT" : "VEHICLES",
                new Vector3(geometry.center.x, geometry.max.y + 0.07f, front), 0.32f, Color.white);
            foreach (string key in new[] { "SwitchOn", "SwitchOff" })
            {
                var visual = (GameObject)serialized.FindProperty(key).objectReferenceValue;
                bool on = key == "SwitchOn";
                bool wasActive = visual.activeSelf;
                try
                {
                    // TMP의 Renderer 초기화 후 글꼴을 지정하고 원래 ON/OFF 상태로 되돌린다.
                    visual.SetActive(true);
                    Vector3 statePosition = visual.transform.InverseTransformPoint(
                        controlFrame.TransformPoint(new Vector3(geometry.center.x, geometry.center.y, front)));
                    Label(visual.transform, "Setting state label", on ? "ON" : "OFF",
                        statePosition, 0.24f, on ? new Color(0.3f, 0.95f, 0.5f) : new Color(1f, 0.65f, 0.4f));
                }
                finally { visual.SetActive(wasActive); }
            }
        }
        Sync(result);
        var hitbox = result.GetComponent<BoxCollider>();
        var ambientHitbox = ambient.GetComponent<BoxCollider>();
        Physics.SyncTransforms();
        if (!hitbox || !hitbox.enabled || !result.gameObject.activeInHierarchy || hitbox.bounds.Intersects(ambientHitbox.bounds))
            throw new InvalidOperationException("Vehicle switch must be active with a separate interaction hitbox.");
        if (root.GetComponentsInChildren<AudioSource>(true).Length != 0)
            throw new InvalidOperationException("Vehicle switch must not duplicate audio sources.");
        return result;
    }

    private static Bounds SwitchBounds(ObjectLocalToggle control, SerializedObject serialized)
    {
        var meshes = control.GetComponentsInChildren<MeshFilter>(true).ToList();
        foreach (string key in new[] { "SwitchOn", "SwitchOff" })
            meshes.AddRange(((GameObject)serialized.FindProperty(key).objectReferenceValue).GetComponentsInChildren<MeshFilter>(true));
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (var mesh in meshes.Where(m => m.sharedMesh && !m.GetComponent<TMP_Text>()))
        {
            var local = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z);
                var point = control.transform.parent.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        if (first) throw new InvalidOperationException("Switch has no mesh bounds.");
        return bounds;
    }

    private static void Label(Transform parent, string name, string text, Vector3 position, float size, Color color)
    {
        var child = parent.Find(name);
        if (!child)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }
        child.localPosition = position;
        child.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var caption = child.GetComponent<TextMeshPro>();
        if (!caption) caption = child.gameObject.AddComponent<TextMeshPro>();
        caption.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        const string materialPath = "Assets/_ShinjukuExhibition/Materials/SettingsLabel.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(caption.font.material) { name = "SettingsLabel" };
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            material.EnableKeyword("OUTLINE_ON");
            AssetDatabase.CreateAsset(material, materialPath);
        }
        caption.fontSharedMaterial = material;
        caption.text = text;
        caption.fontSize = size;
        caption.alignment = TextAlignmentOptions.Center;
        caption.color = color;
        caption.rectTransform.sizeDelta = new Vector2(0.3f, 0.08f);
        caption.enableWordWrapping = false;
        caption.ForceMeshUpdate(true);
        if (caption.preferredWidth > 0.3f || caption.preferredHeight > 0.08f)
            throw new InvalidOperationException("Switch label overflow: " + name);
    }

    private static void VerifyInteraction(Scene scene, ObjectLocalToggle control)
    {
        var collider = control.GetComponent<BoxCollider>();
        Vector3 front = control.transform.forward;
        var hits = Physics.RaycastAll(collider.bounds.center + front * 1.2f, -front, 1.4f, ~(1 << 2), QueryTriggerInteraction.Collide)
            .Where(h => h.collider.gameObject.scene == scene && (h.collider == collider || !h.collider.isTrigger))
            .OrderBy(h => h.distance).ToArray();
        if (hits.Length == 0 || hits[0].collider != collider)
            throw new InvalidOperationException("Switch interaction blocked: " + PathOf(control.transform));
        var program = (UdonSharpProgramAsset)UdonSharpEditorUtility.GetBackingUdonBehaviour(control).programSource;
        if (!program.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_interact"))
            throw new InvalidOperationException("Switch is missing its Interact entry point.");
    }

    private static void CaptureSwitches(Scene scene, Transform frame, float direction, string filename)
    {
        var host = new GameObject("Temporary switch preview camera");
        host.hideFlags = HideFlags.HideAndDontSave;
        var camera = host.AddComponent<Camera>();
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        Vector3 center = frame.TransformPoint(new Vector3(0.325f, 1.05f, 0f));
        camera.transform.position = center + frame.forward * direction * 1.6f;
        camera.transform.LookAt(center, frame.up);
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 30f;
        var target = new RenderTexture(1200, 800, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(host);
            if (image) UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
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

    private static void Inspect()
    {
        var original = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var report = new StringBuilder();
            report.AppendLine("Original active: " + original.path + "; dirty: " + original.isDirty);
            report.AppendLine("Exhibition dirty: " + scene.isDirty);
            report.AppendLine("Trackers: " + Components<WorldPlayerData>(scene).Length + "; settings: " + Components<WorldLocalSettings>(scene).Length + "; panels: " + Components<SpeakerMapPanel>(scene).Length);
            foreach (var speaker in Components<SpeakerController>(scene))
                report.AppendLine("Speaker: " + PathOf(speaker.transform) + "; active: " + speaker.gameObject.activeInHierarchy);
            foreach (var traffic in Components<TrafficSimulationManager>(scene))
            {
                report.AppendLine("Traffic: " + PathOf(traffic.transform) + "; visuals: " + (traffic.localVehicleVisualRoot ? PathOf(traffic.localVehicleVisualRoot.transform) : "NONE"));
                foreach (var vehicle in traffic.vehicleRoots)
                    report.AppendLine("Vehicle: " + (vehicle ? PathOf(vehicle) : "NONE"));
            }
            foreach (var toggle in Components<ObjectLocalToggle>(scene))
            {
                report.AppendLine("Toggle: " + PathOf(toggle.transform) + "; active: " + toggle.gameObject.activeInHierarchy);
                if (PathOf(toggle.transform) == "50_Audio/ambient sound button/Switch_MicTest")
                {
                    report.AppendLine("Ambient switch frame: " + toggle.transform.parent.position.ToString("F3") + "; rotation: " + toggle.transform.parent.eulerAngles + "; scale: " + toggle.transform.parent.lossyScale);
                    foreach (var child in toggle.transform.parent.GetComponentsInChildren<Transform>(true))
                        report.AppendLine("  Control node: " + PathOf(child) + "; local: " + child.localPosition.ToString("F3") + "; scale: " + child.localScale.ToString("F3") +
                            "; components: " + string.Join(",", child.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)));
                }
                var targets = new SerializedObject(toggle).FindProperty("targetObjects");
                for (int i = 0; i < targets.arraySize; i++)
                {
                    var target = targets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                    report.AppendLine("  Target: " + (target ? PathOf(target.transform) : "NONE"));
                }
            }
            foreach (var source in Components<AudioSource>(scene))
                report.AppendLine("Audio: " + PathOf(source.transform) + "; enabled: " + source.enabled + "; active: " + source.gameObject.activeInHierarchy + "; clip: " + (source.clip ? source.clip.name : "NONE"));
            File.WriteAllText(Output + "/exhibition-inspect.done", report.ToString());
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
