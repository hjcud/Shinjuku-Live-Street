#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TEST 씬의 스피커 설치 범위 표시와 다국어 문자 안내를 구성하고 검증</summary>
[InitializeOnLoad]
public static class SpeakerPlacementSetup
{
    private const string Output = "output/speaker-placement-20260912";
    private const string ApplyRequest = Output + "/apply.request";
    private const string ResultPath = Output + "/apply.txt";
    private const string FontPath = "Assets/_Shinjuku/UI/SpeakerMap/CJKNames.asset";
    private const string WarningClipPath = "Assets/_Shinjuku/Art/Animations/Speaker/Animation/SpeakerHoloWarning.anim";

    static SpeakerPlacementSetup()
    {
        EditorApplication.update += RunIfRequested;
    }

    private static void RunIfRequested()
    {
        string inspect = Output + "/inspect-review.request";
        if (File.Exists(inspect) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(inspect);
            try { InspectReview(); } catch(Exception e) { File.WriteAllText(Output+"/inspect-review.failed",e.ToString()); }
            return;
        }
        if (!File.Exists(ApplyRequest) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(ApplyRequest);

        try
        {
            Apply();
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(ResultPath, exception.ToString());
            Debug.LogException(exception);
        }
    }

    private static void InspectReview()
    {
        var report = new StringBuilder();
        foreach(var manager in UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true))
        {
            var so=new SerializedObject(manager);
            report.AppendLine("MANAGER "+manager.name+" active="+manager.gameObject.activeInHierarchy+" range="+so.FindProperty("audibleRange").floatValue+" mask="+so.FindProperty("rayLayerMask").intValue);
            foreach(var line in ((GameObject)so.FindProperty("speakerPlacements").objectReferenceValue).GetComponentsInChildren<LineRenderer>(true))
                report.AppendLine("LINE "+line.name+" parent="+line.transform.parent.name+" scale="+line.transform.lossyScale+" shader="+line.sharedMaterial.shader.name+" mat="+AssetDatabase.GetAssetPath(line.sharedMaterial)+" alignment="+line.alignment);
        }
        foreach(var speaker in UnityEngine.Object.FindObjectsOfType<SpeakerController>(true))
        {
            report.AppendLine("SPEAKER "+speaker.name+" active="+speaker.gameObject.activeInHierarchy+" local="+speaker.gameObject.activeSelf+" pos="+speaker.transform.position);
            var so=new SerializedObject(speaker);var owners=so.FindProperty("ownerObjects");
            for(int i=0;i<owners.arraySize;i++){var o=owners.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;report.AppendLine(" OWNER_UI "+(o?o.name+" active="+o.activeSelf+" containsController="+speaker.transform.IsChildOf(o.transform):"NULL"));}
            foreach(var audio in speaker.GetComponentsInChildren<AudioSource>(true))
            {
                report.AppendLine(" AUDIO "+audio.name+" min="+audio.minDistance+" max="+audio.maxDistance+" volume="+audio.volume+" rolloff="+audio.rolloffMode+" blend="+audio.spatialBlend);
                foreach(var component in audio.GetComponents<Component>())if(component.GetType().Name.Contains("Spatial"))report.AppendLine(" SPATIAL "+EditorJsonUtility.ToJson(component));
                var curve=audio.GetCustomCurve(AudioSourceCurveType.CustomRolloff);if(curve!=null)report.AppendLine(" CURVE "+string.Join(";",curve.keys.Select(k=>k.time+":"+k.value)));
            }
            foreach(var canvas in speaker.GetComponentsInChildren<Canvas>(true))report.AppendLine(" CANVAS "+canvas.name+" active="+canvas.gameObject.activeSelf+" layer="+canvas.gameObject.layer+" raycaster="+(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()!=null));
            if(speaker.name=="Speaker (0)")
            {
                foreach(var canvas in speaker.GetComponentsInChildren<Canvas>(true))
                {
                    report.AppendLine(" UI_DETAIL "+canvas.name+" position="+canvas.transform.position+" scale="+canvas.transform.lossyScale+" components="+string.Join(",",canvas.GetComponents<Component>().Select(c=>c.GetType().Name)));
                    var ray=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();if(ray)report.AppendLine(" RAY "+EditorJsonUtility.ToJson(ray));
                    foreach(var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        if(g.raycastTarget)report.AppendLine(" HITGRAPHIC "+g.name+" active="+g.gameObject.activeSelf+" type="+g.GetType().Name+" size="+g.rectTransform.rect.size+" alpha="+g.color.a);
                }
                foreach(var c in speaker.GetComponentsInChildren<Collider>(true))report.AppendLine(" COLLIDER "+c.name+" enabled="+c.enabled+" trigger="+c.isTrigger+" layer="+c.gameObject.layer+" type="+c.GetType().Name+" local="+c.transform.localPosition+" size="+(c is BoxCollider?((BoxCollider)c).size.ToString():""));
            }
        }
        File.WriteAllText(Output+"/inspect-review.txt",report.ToString());
    }

    [MenuItem("Tools/Shinjuku/Apply speaker placement range and localized guide")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "TEST")
            throw new InvalidOperationException("Open the TEST scene before applying speaker placement UI.");

        SpeakerManager manager = UnityEngine.Object.FindObjectsOfType<SpeakerManager>(true)
            .Single(value => value.gameObject.scene == scene);
        SerializedObject serialized = new SerializedObject(manager);
        GameObject placements = RequireGameObject(serialized, "speakerPlacements");
        GameObject messageUi = RequireGameObject(serialized, "messageUI");
        Animator holoAnimator = serialized.FindProperty("holoAnimator").objectReferenceValue as Animator;
        if (holoAnimator == null) throw new InvalidOperationException("Speaker hologram Animator was not found.");
        Transform content = messageUi.transform.Find("ImageScaleParent");
        if (content == null) throw new InvalidOperationException("SystemMessageUI/ImageScaleParent was not found.");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new InvalidOperationException("CJK TMP font was not found: " + FontPath);
        if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            throw new InvalidOperationException("CJKNames must remain Dynamic so all four languages can render.");

        WorldLanguage worldLanguage = UnityEngine.Object.FindObjectsOfType<WorldLanguage>(true)
            .Single(value => value.gameObject.scene == scene);
        AnimatorController holoController = EnsureWarningHologram(holoAnimator);

        Undo.RegisterFullObjectHierarchyUndo(placements, "Add speaker audible range and localized guide");
        DisableLegacyImages(content);

        TextMeshProUGUI status = EnsureLabel(content, "PlacementStatusText", new Vector2(0f, -340f),
            new Vector2(1500f, 150f), 42f, font, FontStyles.Bold);
        TextMeshProUGUI guide = EnsureLabel(content, "PlacementGuideText", new Vector2(0f, -475f),
            new Vector2(1500f, 80f), 32f, font, FontStyles.Normal);
        guide.color = new Color(0.82f, 0.9f, 1f, 1f);

        LineRenderer range = EnsureRangeRenderer(placements.transform, serialized);
        Assign(serialized, "worldLanguage", worldLanguage);
        Assign(serialized, "placementGuideText", guide);
        Assign(serialized, "placementStatusText", status);
        Assign(serialized, "audibleRangeRenderer", range);
        serialized.FindProperty("audibleRange").floatValue = 30f;
        serialized.FindProperty("minimumSpeakerDistance").floatValue = 8f;
        serialized.FindProperty("audibleRangeSegments").intValue = 72;
        serialized.ApplyModifiedProperties();
        worldLanguage.Register(manager);
        EditorUtility.SetDirty(worldLanguage);
        UdonSharpEditorUtility.CopyProxyToUdon(worldLanguage);
        UdonSharpEditorUtility.CopyProxyToUdon(manager);

        Verify(manager, worldLanguage, placements, content, range, status, guide, font, holoController);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);

        Directory.CreateDirectory(Output);
        EditorSceneManager.SaveScene(scene, Output + "/TEST.with-speaker-placement-range.unity", true);
        File.WriteAllText(ResultPath,
            "PASS\n" +
            "Scene=" + scene.path + " dirty=" + scene.isDirty + " (active scene intentionally not saved)\n" +
            "AudibleRange=30m MinimumDistance=8m OverlapWarningBelow=60m Segments=72\n" +
            "Labels=English/Japanese/Korean/Chinese via WorldLanguage\n" +
            "Hologram=blue ready, amber overlap warning, red blocked\n" +
            "Legacy guide/status images disabled; existing animator and hologram retained\n" +
            "Scene copy=" + Output + "/TEST.with-speaker-placement-range.unity\n");
    }

    private static GameObject RequireGameObject(SerializedObject serialized, string propertyName)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        GameObject value = property == null ? null : property.objectReferenceValue as GameObject;
        if (value == null) throw new InvalidOperationException("Missing SpeakerManager reference: " + propertyName);
        return value;
    }

    private static void DisableLegacyImages(Transform content)
    {
        foreach (string childName in new[] { "Place_Cancel", "Image_Msg1", "Image_Msg2", "Image_Msg3" })
        {
            Transform child = content.Find(childName);
            if (child == null) throw new InvalidOperationException("Legacy guide object was not found: " + childName);
            UnityEngine.UI.Image image = child.GetComponent<UnityEngine.UI.Image>();
            if (image == null) throw new InvalidOperationException(childName + " has no Image component.");
            image.enabled = false;
            image.raycastTarget = false;
            EditorUtility.SetDirty(image);
        }
    }

    private static TextMeshProUGUI EnsureLabel(Transform parent, string name, Vector2 position,
        Vector2 size, float fontSize, TMP_FontAsset font, FontStyles style)
    {
        Transform existing = parent.Find(name);
        GameObject labelObject;
        if (existing == null)
        {
            labelObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(labelObject, "Create " + name);
            labelObject.transform.SetParent(parent, false);
        }
        else labelObject = existing.gameObject;

        labelObject.layer = parent.gameObject.layer;
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        if (label == null) label = Undo.AddComponent<TextMeshProUGUI>(labelObject);
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = string.Empty;
        label.color = Color.white;
        EditorUtility.SetDirty(label);
        return label;
    }

    private static LineRenderer EnsureRangeRenderer(Transform parent, SerializedObject manager)
    {
        Transform existing = parent.Find("AudibleRangeIndicator");
        GameObject rangeObject;
        if (existing == null)
        {
            rangeObject = new GameObject("AudibleRangeIndicator");
            Undo.RegisterCreatedObjectUndo(rangeObject, "Create audible range indicator");
            rangeObject.transform.SetParent(parent, false);
        }
        else rangeObject = existing.gameObject;
        rangeObject.layer = parent.gameObject.layer;

        LineRenderer range = rangeObject.GetComponent<LineRenderer>();
        if (range == null) range = Undo.AddComponent<LineRenderer>(rangeObject);
        LineRenderer ray = manager.FindProperty("lineRenderer").objectReferenceValue as LineRenderer;
        if (ray == null || ray.sharedMaterial == null)
            throw new InvalidOperationException("Placement ray material was not found.");

        range.sharedMaterial = ray.sharedMaterial;
        range.useWorldSpace = false;
        range.loop = true;
        range.positionCount = 72;
        range.widthMultiplier = 0.11f;
        range.numCornerVertices = 2;
        range.numCapVertices = 0;
        range.alignment = LineAlignment.View;
        range.textureMode = LineTextureMode.Stretch;
        range.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        range.receiveShadows = false;
        range.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        range.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        range.startColor = range.endColor = new Color(0.18f, 0.9f, 0.56f, 0.9f);
        for (int i = 0; i < 72; i++)
        {
            float angle = i * Mathf.PI * 2f / 72f;
            range.SetPosition(i, new Vector3(Mathf.Cos(angle) * 30f, 0f, Mathf.Sin(angle) * 30f));
        }
        EditorUtility.SetDirty(range);
        return range;
    }

    private static AnimatorController EnsureWarningHologram(Animator animator)
    {
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) throw new InvalidOperationException("Speaker hologram does not use an AnimatorController.");

        if (!controller.parameters.Any(parameter => parameter.name == "HoloWarning"))
            controller.AddParameter("HoloWarning", AnimatorControllerParameterType.Bool);

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WarningClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "SpeakerHoloWarning", frameRate = 60f, wrapMode = WrapMode.Loop };
            AssetDatabase.CreateAsset(clip, WarningClipPath);
        }
        SetConstantColorCurve(clip, "material._EmissionColor", new Color(1f, 1f, 0.05f, 1f));
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState enable = FindState(machine, "SpeakerHoloEnable");
        AnimatorState disable = FindState(machine, "SpeakerHoloDisable");
        if (enable == null || disable == null)
            throw new InvalidOperationException("Existing speaker hologram states were not found.");

        AnimatorState warning = FindState(machine, "SpeakerHoloWarning");
        if (warning == null) warning = machine.AddState("SpeakerHoloWarning", new Vector3(540f, 200f));
        warning.motion = clip;

        if (!HasTransition(machine.anyStateTransitions, warning, "HoloWarning", AnimatorConditionMode.If))
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(warning);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, "HoloWarning");
        }
        if (!HasTransition(warning.transitions, enable, "HoloWarning", AnimatorConditionMode.IfNot))
        {
            AnimatorStateTransition transition = warning.AddTransition(enable);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "HoloWarning");
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "HoloDisabled");
        }
        if (!HasTransition(warning.transitions, disable, "HoloWarning", AnimatorConditionMode.IfNot))
        {
            AnimatorStateTransition transition = warning.AddTransition(disable);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "HoloWarning");
            transition.AddCondition(AnimatorConditionMode.If, 0f, "HoloDisabled");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    private static void SetConstantColorCurve(AnimationClip clip, string property, Color color)
    {
        float[] values = { color.r, color.g, color.b, color.a };
        string[] channels = { ".r", ".g", ".b", ".a" };
        for (int i = 0; i < channels.Length; i++)
        {
            AnimationCurve curve = AnimationCurve.Constant(0f, 1f, values[i]);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("SpeakerHologram/SpeakerObject", typeof(MeshRenderer), property + channels[i]), curve);
        }
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state != null && child.state.name == name) return child.state;
        return null;
    }

    private static bool HasTransition(AnimatorStateTransition[] transitions, AnimatorState destination,
        string parameter, AnimatorConditionMode mode)
    {
        foreach (AnimatorStateTransition transition in transitions)
            if (transition.destinationState == destination && transition.conditions.Any(condition =>
                condition.parameter == parameter && condition.mode == mode)) return true;
        return false;
    }

    private static void Assign(SerializedObject serialized, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null) throw new InvalidOperationException("SpeakerManager field was not found: " + propertyName);
        property.objectReferenceValue = value;
    }

    private static void Verify(SpeakerManager manager, WorldLanguage worldLanguage, GameObject placements, Transform content,
        LineRenderer range, TextMeshProUGUI status, TextMeshProUGUI guide, TMP_FontAsset font,
        AnimatorController holoController)
    {
        if (range.transform.parent != placements.transform || range.useWorldSpace || !range.loop || range.positionCount != 72)
            throw new InvalidOperationException("Audible range renderer configuration failed.");
        if (worldLanguage.listeners == null || !worldLanguage.listeners.Contains(manager))
            throw new InvalidOperationException("SpeakerManager is not registered with WorldLanguage.");
        if (!holoController.parameters.Any(parameter => parameter.name == "HoloWarning") ||
            FindState(holoController.layers[0].stateMachine, "SpeakerHoloWarning") == null)
            throw new InvalidOperationException("Speaker hologram warning state configuration failed.");
        if (status.rectTransform.sizeDelta.x <= 0f || status.rectTransform.sizeDelta.y <= 0f ||
            guide.rectTransform.sizeDelta.x <= 0f || guide.rectTransform.sizeDelta.y <= 0f)
            throw new InvalidOperationException("Localized placement labels have zero size.");
        if (status.font != font || guide.font != font)
            throw new InvalidOperationException("Localized placement labels are not using the CJK font.");
        foreach (string childName in new[] { "Place_Cancel", "Image_Msg1", "Image_Msg2", "Image_Msg3" })
            if (content.Find(childName).GetComponent<UnityEngine.UI.Image>().enabled)
                throw new InvalidOperationException("Legacy image is still enabled: " + childName);

        MethodInfo localizedStatus = typeof(SpeakerManager).GetMethod("LocalizedStatus", BindingFlags.Instance | BindingFlags.NonPublic);
        if (localizedStatus == null) throw new InvalidOperationException("LocalizedStatus method was not found.");
        var checks = new[]
        {
            new[] { "en", "Too close to another speaker. Move it farther away." },
            new[] { "ja-JP", "他のスピーカーに近すぎます。もう少し離してください。" },
            new[] { "ko-KR", "다른 스피커와 너무 가깝습니다. 조금 더 떨어뜨려 주세요." },
            new[] { "zh-CN", "离其他音箱太近，请再移远一些。" }
        };
        string originalLanguageCode = worldLanguage.languageCode;
        int originalLanguage = worldLanguage.currentLanguage;
        foreach (string[] check in checks)
        {
            worldLanguage.languageCode = check[0];
            manager._OnWorldLanguageChanged();
            string actual = (string)localizedStatus.Invoke(manager, new object[] { 4 });
            if (actual != check[1]) throw new InvalidOperationException("Language check failed: " + check[0]);
        }
        worldLanguage.languageCode = originalLanguageCode;
        worldLanguage.currentLanguage = originalLanguage;
        manager._OnWorldLanguageChanged();

        UdonSharpEditorUtility.CopyProxyToUdon(manager);
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
        if (backing == null || backing.programSource == null)
            throw new InvalidOperationException("SpeakerManager Udon backing program is missing.");
    }
}
#endif
