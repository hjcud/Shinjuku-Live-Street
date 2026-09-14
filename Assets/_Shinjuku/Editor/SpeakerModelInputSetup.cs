using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpeakerModelInputSetup
{
    const string Output = "output/speaker-model-input-20260913/";
    static readonly string[] ScenePaths = {
        "Assets/_Shinjuku/Scenes/TEST_PC.unity",
        "Assets/_Shinjuku/Scenes/TEST_Quest.unity",
        "Assets/_ShinjukuExhibition/Scenes/TechnicalExhibition.unity"
    };
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static SpeakerModelInputSetup() { EditorApplication.update += Poll; }
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Flags).Invoke(target, values);
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string apply = Output + "apply-v2.request";
        if (File.Exists(apply))
        {
            File.Delete(apply);
            try { Apply(); }
            catch (Exception e) { File.WriteAllText(Output + "apply.failed", e.ToString()); Debug.LogException(e); }
            return;
        }
        string request = Output + "inspect.request";
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { Inspect(); }
        catch (Exception e) { File.WriteAllText(Output + "inspect.failed", e.ToString()); Debug.LogException(e); }
    }
    static void Inspect()
    {
        var report = new StringBuilder();
        report.AppendLine("Active: " + SceneManager.GetActiveScene().path);
        foreach (string path in ScenePaths)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var managers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpeakerManager>(true)).ToArray();
                report.AppendLine(path + " managers=" + managers.Length);
                foreach (var manager in managers)
                {
                    var holo = Get<GameObject>(manager, "holoSpeaker");
                    var animator = Get<Animator>(manager, "holoAnimator");
                    report.AppendLine("manager=" + manager.name + " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(manager));
                    foreach (var t in holo.GetComponentsInChildren<Transform>(true))
                        report.AppendLine("  " + t.name + " scale=" + t.localScale + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "missing")));
                    foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                        report.AppendLine("clip=" + clip.name + " bindings=" + string.Join(";", AnimationUtility.GetCurveBindings(clip).Select(b => b.path + ":" + b.propertyName)));
                    var guide = Get<TextMeshProUGUI>(manager, "placementGuideText");
                    report.AppendLine("guide=" + (guide ? guide.text + " rect=" + guide.rectTransform.rect + " font=" + guide.fontSize : "MISSING"));
                }
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        File.WriteAllText(Output + "inspect.done", report.ToString());
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Apply()
    {
        Check(!UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), "Resolve Udon compile errors first");
        var original = SceneManager.GetActiveScene();
        var report = new StringBuilder();
        TextMeshProUGUI template = null;
        try
        {
            foreach (string path in ScenePaths)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                bool saved = false;
                try
                {
                    var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpeakerManager>(true)).Single();
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    Check(EditorSceneManager.SaveScene(scene, Output + scene.name + ".before-" + stamp + ".unity", true), "Scene backup failed");
                    var holo = Get<GameObject>(manager, "holoSpeaker");
                    var mesh = holo.GetComponentsInChildren<MeshRenderer>(true).Single();
                    Check(mesh.transform != holo.transform, "Model visual must be separate from placement root");
                    Set(manager, "modelPreviewVisual", mesh.transform);
                    var guide = Get<TextMeshProUGUI>(manager, "placementGuideText");
                    if (!guide)
                    {
                        Check(template, "Missing PC guide template");
                        var content = Get<GameObject>(manager, "messageUI").transform.Find("ImageScaleParent");
                        Check(content, "Missing existing guidance container");
                        var existing = content.Find("PlacementGuideText");
                        guide = existing ? existing.GetComponent<TextMeshProUGUI>() : UnityEngine.Object.Instantiate(template, content, false);
                        guide.name = "PlacementGuideText";
                        guide.gameObject.hideFlags = HideFlags.None;
                        Set(manager, "placementGuideText", guide);
                        report.AppendLine(scene.name + ": connected missing placement guide");
                    }
                    guide.rectTransform.sizeDelta = new Vector2(guide.rectTransform.sizeDelta.x, 110f);
                    guide.enableWordWrapping = false;
                    guide.enableAutoSizing = false;
                    guide.raycastTarget = false;
                    guide.gameObject.SetActive(true);
                    Verify(manager, report);
                    guide.text = (string)Call(manager, "LocalizedGuide");
                    if (!template)
                    {
                        template = UnityEngine.Object.Instantiate(guide);
                        template.gameObject.hideFlags = HideFlags.HideAndDontSave;
                        template.gameObject.SetActive(false);
                    }
                    EditorUtility.SetDirty(guide);
                    EditorUtility.SetDirty(manager);
                    UdonSharpEditorUtility.CopyProxyToUdon(manager);
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                    Check(backing.publicVariables.TryGetVariableValue("modelPreviewVisual", out Transform linked) && linked == mesh.transform, "Udon visual reference mismatch");
                    Check(backing.publicVariables.TryGetVariableValue("placementGuideText", out TextMeshProUGUI linkedGuide) && linkedGuide == guide, "Udon guide reference mismatch");
                    EditorUtility.SetDirty(backing);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene), "Saving scene failed");
                    saved = true;
                    report.AppendLine("SAVED " + path + "; visual=" + mesh.name + "; guide/Udon references verified");
                    File.WriteAllText(Output + "apply.progress", report.ToString());
                }
                finally { if (opened && saved) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally
        {
            if (template) UnityEngine.Object.DestroyImmediate(template.gameObject);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
        report.AppendLine("PASS all three requested scenes. Editor state tests and compiled Udon references verified; physical VRChat/Quest input not exercised.");
        File.WriteAllText(Output + "apply.done", report.ToString());
    }

    static void Verify(SpeakerManager live, StringBuilder report)
    {
        var host = new GameObject("Temporary model input tests") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        var test = host.AddComponent<SpeakerManager>();
        var placement = new GameObject("Placement test"); placement.transform.SetParent(host.transform, false);
        var holo = UnityEngine.Object.Instantiate(Get<GameObject>(live, "holoSpeaker"), placement.transform, false);
        var visual = holo.GetComponentsInChildren<MeshRenderer>(true).Single().transform;
        var ring = new GameObject("Ring test"); ring.transform.SetParent(host.transform, false);
        var ringAnimator = ring.AddComponent<Animator>();
        ringAnimator.runtimeAnimatorController = Get<Animator>(live, "ringAnimator").runtimeAnimatorController;
        Set(test, "holoSpeaker", holo); Set(test, "modelPreviewVisual", visual);
        Set(test, "speakerPlacements", placement); Set(test, "ringObject", ring); Set(test, "ringAnimator", ringAnimator);
        var baseScale = visual.localScale; var baseRotation = visual.localRotation;
        var rootPosition = holo.transform.position; var rootRotation = holo.transform.rotation;
        // Live guidance is below an inactive placement root; render a temporary active copy.
        var ui = new GameObject("Temporary guide layout test", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
        ui.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ui.transform.position = Vector3.one * 100000f;
        var guide = UnityEngine.Object.Instantiate(Get<TextMeshProUGUI>(live, "placementGuideText"), ui.transform, false);
        guide.gameObject.SetActive(true);
        try
        {
            Call(test, "HandleDesktopModelKeys", true, false);
            Check(!Get<bool>(test, "modelSwitchActive"), "Q triggered while not placing");
            Set(test, "isPlacingSpeaker", true);
            Call(test, "HandleDesktopModelKeys", true, true);
            Check(!Get<bool>(test, "modelSwitchActive"), "Simultaneous Q/E should be ignored");
            foreach (int direction in new[] { -1, 1 })
            {
                Call(test, "HandleDesktopModelKeys", direction < 0, direction > 0);
                Check(Get<bool>(test, "modelSwitchActive") && Get<int>(test, "modelSwitchDirection") == direction && Get<bool>(test, "isPlacingSpeaker"), "Q/E must switch, not cancel");
                Call(test, "AdvanceModelTransition", .08f);
                Check(visual.localScale.magnitude < baseScale.magnitude * .7f, "Transition did not shrink");
                float elapsed = Get<float>(test, "modelSwitchElapsed");
                Call(test, "HandleDesktopModelKeys", direction > 0, direction < 0);
                Check(Get<float>(test, "modelSwitchElapsed") == elapsed, "Repeated input restarted active transition");
                Call(test, "ConfirmPlacement");
                Check(Get<bool>(test, "isPlacingSpeaker"), "Placement allowed during model transition");
                Call(test, "AdvanceModelTransition", .08f);
                Check(Get<int>(test, "selectedPreviewModel") == 0 && visual.localScale.magnitude <= baseScale.magnitude * .061f, "Single model midpoint mismatch");
                Call(test, "AdvanceModelTransition", .2f);
                Check(!Get<bool>(test, "modelSwitchActive") && visual.localScale == baseScale && Quaternion.Angle(visual.localRotation, baseRotation) < .001f, "Final visual not restored");
                Check(holo.transform.position == rootPosition && Quaternion.Angle(holo.transform.rotation, rootRotation) < .001f, "Transition moved placement root");
            }
            report.AppendLine("PASS " + live.gameObject.scene.name + " Q/E directions, single-model wrap, animated shrink/restore, root unchanged, repeat lock, confirm lock");
            Call(test, "HandleDesktopModelKeys", false, true); Call(test, "AdvanceModelTransition", .1f);
            test.InputDrop(true, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(!Get<bool>(test, "isPlacingSpeaker") && !Get<bool>(test, "modelSwitchActive") && !placement.activeSelf && visual.localScale == baseScale && Get<bool>(test, "waitForPlacementRelease"), "Right click cancel/reset failed");
            Set(test, "currentHoldTime", .5f);
            Check((bool)Call(test, "TryCancelDesktop", true) && Get<float>(test, "currentHoldTime") == 0f, "Right click charging cancel failed");
            Set(test, "isVrUser", true); Set(test, "isPlacingSpeaker", true);
            test.InputDrop(true, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(Get<bool>(test, "isPlacingSpeaker"), "VR drop incorrectly cancelled");
            Call(test, "HandleDesktopModelKeys", true, false);
            Check(!Get<bool>(test, "modelSwitchActive"), "Desktop model keys affected VR");
            test.InputLookVertical(-1f, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(!Get<bool>(test, "modelSwitchActive"), "Opening stick triggered model change before neutral");
            test.InputLookVertical(0f, default(VRC.Udon.Common.UdonInputEventArgs));
            test.InputLookVertical(.5f, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(!Get<bool>(test, "modelSwitchActive"), "Small stick drift changed model");
            test.InputLookVertical(1f, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(Get<bool>(test, "modelSwitchActive") && Get<int>(test, "modelSwitchDirection") == -1, "Stick up previous failed");
            Call(test, "AdvanceModelTransition", 1f);
            test.InputLookVertical(.8f, default(VRC.Udon.Common.UdonInputEventArgs));
            test.InputLookVertical(-1f, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(!Get<bool>(test, "modelSwitchActive"), "Held stick repeated without neutral");
            test.InputLookVertical(0f, default(VRC.Udon.Common.UdonInputEventArgs));
            test.InputLookVertical(-1f, default(VRC.Udon.Common.UdonInputEventArgs));
            Check(Get<bool>(test, "modelSwitchActive") && Get<int>(test, "modelSwitchDirection") == 1, "Stick down next failed");
            Call(test, "CancelPlacement");
            Check(visual.localScale == baseScale && !Get<bool>(test, "modelSwitchActive"), "VR cancel restore failed");
            report.AppendLine("PASS right-click charging/transition cancel; VR input isolation, neutral gate, threshold, hold debounce, up/down, cancel restore");
            for (int language = 0; language < 4; language++)
            foreach (bool vr in new[] { false, true })
            {
                Set(test, "languageIndex", language); Set(test, "isVrUser", vr);
                guide.text = (string)Call(test, "LocalizedGuide"); guide.ForceMeshUpdate(true);
                Check(guide.textInfo.lineCount == 2 && !guide.isTextOverflowing, "Guide must fit in two short lines: " + language + "/" + vr);
                Check(!guide.text.Contains("Q: Cancel") && guide.text.Contains(" | "), "Stale cancel copy");
                foreach (char c in guide.text)
                    if (!char.IsWhiteSpace(c)) Check(guide.font.HasCharacter(c, true, true), "Missing guide glyph " + c);
                report.AppendLine("GUIDE " + language + "/" + vr + " " + guide.text.Replace('\n', '/'));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(host); }
    }
}
