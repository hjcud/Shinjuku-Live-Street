using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using TMPro;
using UdonSharpEditor;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.Udon;

/// <summary>아이콘 설정을 포함한 런타임 코드를 독립 객체로 검증. 실제 씬 설정은 변경하지 않음</summary>
[InitializeOnLoad]
public static class SpeakerMapControlsChecks
{
    private const string Output = "output/speaker-map-ui-20260912";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static StringBuilder report;
    private static int count;
    static SpeakerMapControlsChecks() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(Output + "/controls-check.request")) return;
        File.Delete(Output + "/controls-check.request");
        try { Run(); }
        catch (Exception exception) { File.WriteAllText(Output + "/controls-check.failed", exception.ToString()); }
    }
    private static object Call(object target, string method, params object[] values) { return target.GetType().GetMethod(method, Flags).Invoke(target, values); }
    private static T Get<T>(object target, string field) { return (T)target.GetType().GetField(field, Flags).GetValue(target); }
    private static void Set(object target, string field, object value) { target.GetType().GetField(field, Flags).SetValue(target, value); }
    private static void Check(bool valid, string description)
    {
        if (!valid) throw new InvalidOperationException(description);
        count++; report.AppendLine("PASS " + description);
    }
    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        child.transform.SetParent(parent.transform, false); return child;
    }
    private static void Run()
    {
        report = new StringBuilder(); count = 0;
        var host = new GameObject("Map controls isolated checks") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var languageService = Child(host, "Shared language").AddComponent<WorldLanguage>();
            string[] codes = { "ko", "ko-KR", "KO", "ja", "ja-JP", "en", "fr", "", null };
            int[] resolved = { 2, 2, 2, 1, 1, 0, 0, 0, 0 };
            for (int i = 0; i < codes.Length; i++)
            {
                languageService.OnLanguageChanged(codes[i]);
                Check(languageService.GetLanguage() == resolved[i], "shared locale normalization " + codes[i]);
            }
            languageService.OnLanguageChanged("ja");
            Check(languageService.SelectText("Map", "地図", "지도") == "地図", "shared text selection");
            var consumer = Child(host, "Exhibition consumer").AddComponent<ExhibitionLanguage>();
            consumer.worldLanguage = languageService;
            consumer.allLabels = new UnityEngine.UI.Text[0]; consumer.staticLabels = new UnityEngine.UI.Text[0];
            consumer.modeButtons = new UnityEngine.UI.Image[0];
            languageService.Register(consumer); languageService.Register(consumer); languageService.Register(null);
            Check(languageService.listeners.Length == 1, "language subscription is duplicate-safe");
            consumer.ApplyLanguage();
            Check(consumer.currentLanguage == 1, "existing exhibition Auto uses shared locale");
            languageService.OnLanguageChanged("ko");
            Check(consumer.currentLanguage == 2, "shared change notifies exhibition subscriber");
            consumer.UseEnglish(); languageService.OnLanguageChanged("ja");
            Check(consumer.currentLanguage == 0, "exhibition manual override remains local");
            consumer.UseAuto();
            Check(consumer.currentLanguage == 1, "exhibition returns to current shared locale");
            var liveLanguage = UnityEngine.Object.FindObjectsOfType<WorldLanguage>(true).Single(s => s.gameObject.scene.name == "TEST");
            var languageBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(liveLanguage);
            Check(languageBacking.programSource.SerializedProgramAsset.RetrieveProgram().EntryPoints.HasExportedSymbol("_onLanguageChanged"), "shared VRChat locale event is compiled");
            foreach (var exhibition in UnityEngine.Object.FindObjectsOfType<ExhibitionLanguage>(true).Where(s => s.gameObject.scene.name == "TEST"))
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(exhibition);
                Check(backing.publicVariables.TryGetVariableValue("worldLanguage", out UdonBehaviour reference) && reference == languageBacking, "exhibition Udon points to shared language service");
            }
            var live = UnityEngine.Object.FindObjectsOfType<WorldLocalSettings>(true).Single(s => s.gameObject.scene.name == "TEST");
            var settings = Child(host, "Settings").AddComponent<WorldLocalSettings>();
            var traffic = Child(host, "Traffic").AddComponent<TrafficSimulationManager>();
            EditorUtility.CopySerialized(live.traffic, traffic);
            var database = Child(host, "Database").AddComponent<TrafficLaneDatabase>();
            EditorUtility.CopySerialized(live.traffic.laneDatabase, database);
            traffic.laneDatabase = database; traffic.mainSignal = null;
            var pool = Child(host, "Visuals"); traffic.localVehicleVisualRoot = pool;
            traffic.vehicleRoots = Enumerable.Range(0, live.traffic.vehicleRoots.Length).Select(i => Child(pool, "Vehicle " + i).transform).ToArray();
            traffic.targetActiveVehicles = 1; traffic.enableAuthorityPhysicsObstacles = false;
            Check((bool)Call(traffic, "InitializeManager"), "isolated traffic initializes from a copy of baked lane data");
            Call(traffic, "ResetSlots"); Set(traffic, "initialized", true);
            traffic.localIsAuthority = true; Set(traffic, "authorityReady", true); Set(traffic, "running", true);
            Check((bool)Call(traffic, "TrySpawnOneVehicle"), "isolated authority spawns a vehicle");
            settings.traffic = traffic;
            settings.panels = new SpeakerMapPanel[2];
            for (int i = 0; i < 2; i++)
            {
                var panel = Child(host, "Panel " + i).AddComponent<SpeakerMapPanel>();
                panel.settings = settings; panel.labels = new[] { Child(host, "Static " + i).AddComponent<TextMeshProUGUI>() };
                panel.english = new[] { "Map" }; panel.japanese = new[] { "地図" };
                panel.vehicleLabel = Child(host, "Vehicles " + i).AddComponent<TextMeshProUGUI>();
                panel.ambientLabel = Child(host, "Ambient " + i).AddComponent<TextMeshProUGUI>();
                panel.vehicleOffMark = Child(host,"Vehicle off " + i);
                panel.ambientOffMark = Child(host,"Ambient off " + i);
                panel.vehicleSwitchTrack = Child(host,"Vehicle switch " + i).AddComponent<UnityEngine.UI.Image>();
                panel.ambientSwitchTrack = Child(host,"Ambient switch " + i).AddComponent<UnityEngine.UI.Image>();
                panel.vehicleSwitchThumb = Child(host,"Vehicle thumb " + i).AddComponent<RectTransform>();
                panel.ambientSwitchThumb = Child(host,"Ambient thumb " + i).AddComponent<RectTransform>();
                panel.vehicleSwitchLabel = Child(host,"Vehicle status " + i).AddComponent<TextMeshProUGUI>();
                panel.ambientSwitchLabel = Child(host,"Ambient status " + i).AddComponent<TextMeshProUGUI>();
                settings.panels[i] = panel;
                panel._RefreshLabels();
                foreach (string language in new[] { "ko", "ko-KR", "KO", "ja", "ja-JP", "en", "fr", "" })
                {
                    panel.OnLanguageChanged(language);
                    string expected = "地図\n<size=70%>Map</size>";
                    Check(panel.labels[0].text == expected, "panel " + i + " language " + language);
                }
                panel.OnLanguageChanged("ko");
            }
            var zone = Child(host, "Ambient zone");
            var enabled = Child(zone, "Enabled audio").AddComponent<AudioSource>();
            var disabled = Child(zone, "Initially disabled audio").AddComponent<AudioSource>(); disabled.enabled = false;
            settings.ambientSources = new[] { enabled, disabled, null };
            settings.panels[0]._ToggleAmbient();
            Check(!settings.ambientEnabled && !enabled.enabled && !disabled.enabled, "ambient OFF disables sources without enabling inactive ones");
            zone.SetActive(false); zone.SetActive(true);
            Check(!enabled.enabled, "zone re-entry cannot override ambient OFF");
            Check(settings.panels.All(p => p.ambientLabel.text == "Ambient sound" && !p.ambientOffMark.activeSelf && p.ambientSwitchThumb.anchoredPosition.x == 31), "both boards show ambient OFF with single-line fallback caption and right white half");
            settings.panels[1]._ToggleAmbient();
            Check(settings.ambientEnabled && enabled.enabled && !disabled.enabled, "ambient ON restores original component states");
            Check(settings.panels.All(p => !p.ambientOffMark.activeSelf), "other board can turn ambient ON and clear slash");
            Check(settings.panels.All(p => !p.ambientSwitchLabel.gameObject.activeSelf && p.ambientSwitchThumb.anchoredPosition.x == -31), "both ambient switches show ON with left white half and hidden text");
            bool[] active = Get<bool[]>(traffic, "vehicleActive"); int slot = Array.FindIndex(active, a => a);
            bool[] maneuverBefore = (bool[])Get<bool[]>(traffic, "maneuverPathValid").Clone();
            float beforePosition = Get<float[]>(traffic, "vehicleS")[slot];
            settings.panels[0]._ToggleVehicles();
            Check(!pool.activeSelf && traffic.gameObject.activeInHierarchy && traffic.enabled, "vehicle OFF hides only pool, not authority manager");
            Check(Get<bool>(traffic, "running") && Get<bool>(traffic, "authorityReady"), "vehicle OFF preserves running/authority flags");
            Check(active[slot], "vehicle OFF preserves active simulation slots");
            Check(maneuverBefore.SequenceEqual(Get<bool[]>(traffic, "maneuverPathValid")), "vehicle OFF does not invalidate maneuver paths");
            for (int i = 0; i < 20; i++) Call(traffic, "SimulateStep", .05f);
            Check(Get<float[]>(traffic, "vehicleS")[slot] > beforePosition, "hidden authority vehicle continues advancing");
            Set(traffic, "simulationAccumulator", traffic.simulationInterval);
            double timeBefore = Get<double>(traffic, "simulationStateTime");
            Call(traffic, "UpdateAuthoritySimulation");
            Check(Get<double>(traffic, "simulationStateTime") > timeBefore, "authority simulation clock advances while hidden");
            Check(settings.panels.All(p => p.vehicleLabel.text == "Vehicles" && !p.vehicleOffMark.activeSelf), "both boards retain single-line vehicle captions");
            Check(settings.panels.All(p => !p.vehicleSwitchLabel.gameObject.activeSelf && p.vehicleSwitchThumb.anchoredPosition.x == 31), "both vehicle switches show OFF with right white half and hidden text");
            settings.panels[1]._ToggleVehicles();
            Check(pool.activeSelf && settings.vehiclesEnabled, "second board restores vehicles");
            Check(settings.panels.All(p => !p.vehicleOffMark.activeSelf), "both boards show vehicle ON without slash");
            Check(Get<bool[]>(traffic, "visualActive")[slot], "vehicle restoration uses current simulated slot");
            foreach (var panel in live.panels)
                foreach (var button in panel.GetComponentsInChildren<UnityEngine.UI.Button>())
                    Check(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) != null, "serialized click handler " + panel.name + "/" + button.name);
            Check(live.ambientSources.Length > 0 && live.panels.Length == 2, "live settings reference two boards and existing ambient sources");
            Check(!live.traffic.localVehicleVisualRoot.GetComponentsInChildren<Collider>(true).Any(c => ((1 << c.gameObject.layer) & live.traffic.authorityObstacleLayerMask) != 0),
                "vehicle pool colliders are outside authority obstacle sensing layers");
            var settingsBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(live);
            Check(settingsBacking.publicVariables.TryGetVariableValue("traffic", out UdonBehaviour trafficReference) && trafficReference == UdonSharpEditorUtility.GetBackingUdonBehaviour(live.traffic), "Udon settings retain the traffic reference");
            foreach (var panel in live.panels)
            {
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
                Check(panel.vehicleState.sprite && panel.ambientState.sprite && panel.vehicleOffMark && panel.ambientOffMark, "integrated icons and OFF slashes connected " + panel.name);
                Check(!panel.vehicleOffMark.activeSelf && !panel.ambientOffMark.activeSelf && !panel.vehicleSwitchLabel.gameObject.activeSelf && !panel.ambientSwitchLabel.gameObject.activeSelf, "redundant slashes and status text hidden " + panel.name);
                Check(((RectTransform)panel.vehicleState.transform).anchoredPosition.x == (live.vehiclesEnabled ? 31 : -31) && ((RectTransform)panel.ambientState.transform).anchoredPosition.x == (live.ambientEnabled ? 31 : -31), "white icons occupy exposed colored half " + panel.name);
                Check(panel.vehicleState.transform.parent == panel.vehicleSwitchTrack.transform && panel.ambientState.transform.parent == panel.ambientSwitchTrack.transform, "icons are inside switches " + panel.name);
                Check(panel.vehicleSwitchTrack && panel.ambientSwitchTrack && panel.vehicleSwitchThumb && panel.ambientSwitchThumb && panel.vehicleSwitchLabel && panel.ambientSwitchLabel, "rectangular switch references connected " + panel.name);
                Check(backing.publicVariables.TryGetVariableValue("vehicleSwitchThumb", out RectTransform thumbReference) && thumbReference == panel.vehicleSwitchThumb, "compiled Udon switch thumb reference " + panel.name);
                Check(backing.publicVariables.TryGetVariableValue("settings", out UdonBehaviour shared) && shared == settingsBacking, "Udon shared settings reference " + panel.name);
                var entries = backing.programSource.SerializedProgramAsset.RetrieveProgram().EntryPoints;
                Check(entries.HasExportedSymbol("_ToggleVehicles") && entries.HasExportedSymbol("_ToggleAmbient") && entries.HasExportedSymbol("_RefreshLabels") && !entries.HasExportedSymbol("_onLanguageChanged"), "compiled Udon buttons and locale-independent labels " + panel.name);
                VerifyRaycasts(host, panel);
            }
            File.WriteAllText(Output + "/controls-check.done", "PASS " + count + " checks. Actual C# with isolated Unity objects, not a VRChat multiplayer test.\n" + report);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
    private static void VerifyRaycasts(GameObject host, SpeakerMapPanel panel)
    {
        var canvas = panel.transform.Find("MapCanvas").GetComponent<Canvas>();
        var footer=(RectTransform)canvas.transform.Find("LocalSettings");
        var outline=(RectTransform)footer.Find("SettingsOutline");
        var floor=(RectTransform)canvas.transform.Find("FloorPlan");
        Vector2 boxCenter=footer.anchoredPosition+outline.anchoredPosition-floor.anchoredPosition;
        float leftMargin=boxCenter.x-outline.sizeDelta.x*.5f+floor.sizeDelta.x*.5f;
        float bottomMargin=boxCenter.y-outline.sizeDelta.y*.5f+floor.sizeDelta.y*.5f;
        Check(Mathf.Abs(leftMargin-24)<.01f && Mathf.Abs(bottomMargin-24)<.01f, "settings outline equal 24px left and bottom padding " + panel.name);
        Check(outline.sizeDelta==new Vector2(600,156), "B compact panel trims 80px only from right " + panel.name);
        Check(!outline.GetComponent<UnityEngine.UI.Image>().raycastTarget, "settings outline cannot block clicks " + panel.name);
        Check(footer.Cast<Transform>().Count(t=>t.name=="SettingsOutline" && t.gameObject.activeSelf)==1, "one visible settings outline " + panel.name);
        var heading=footer.Find("LocalOnly").GetComponent<TextMeshProUGUI>();
        string expectedHeading=panel.worldLanguage ? panel.worldLanguage.SelectText("Local Settings","ローカル設定","로컬 설정") : "Local Settings";
        Check(heading.text==expectedHeading && Mathf.Abs(heading.rectTransform.anchoredPosition.y-(outline.anchoredPosition.y+outline.sizeDelta.y*.5f)+30)<.01f, "localized heading inset inside B outline " + panel.name);
        Check(panel.settingsHeadingRule==null && !outline.Find("HeadingRule").gameObject.activeSelf, "B has continuous border without old notch rule " + panel.name);
        var divider=(RectTransform)footer.Find("UsageDivider");
        Check(divider.sizeDelta==new Vector2(1.5f,94) && !divider.GetComponent<UnityEngine.UI.Image>().raycastTarget, "subtle divider is display-only " + panel.name);
        var usage=(RectTransform)canvas.transform.Find("SpeakerUsage");
        var usageTitle=usage.Find("Title").GetComponent<TextMeshProUGUI>();
        var usageSummary=usage.Find("Summary").GetComponent<TextMeshProUGUI>();
        float statusLeft=usage.anchoredPosition.x+usageTitle.rectTransform.anchoredPosition.x-usageTitle.rectTransform.sizeDelta.x*.5f;
        Check(Mathf.Abs(statusLeft-footer.anchoredPosition.x-divider.anchoredPosition.x-16)<.01f && usage.sizeDelta.x==196, "compact speaker column has 16px divider gap " + panel.name);
        Check(usage.Cast<Transform>().Count(t=>t.name=="Title" && t.gameObject.activeSelf)==1 && !usageTitle.raycastTarget && !usageSummary.raycastTarget && usage.GetComponentsInChildren<Button>().Length==0, "one noninteractive speaker status column " + panel.name);
        Check(usage.anchoredPosition.x+usage.sizeDelta.x*.5f<boxCenter.x+outline.sizeDelta.x*.5f, "speaker status inside common B outline " + panel.name);
        foreach(string name in new[]{"Vehicles","Ambient"})
        {
            var column=(RectTransform)footer.Find(name);var track=(RectTransform)column.Find("Switch");
            var caption=column.Find("Caption").GetComponent<TextMeshProUGUI>();
            Check(column.sizeDelta.x==164 && track.anchoredPosition.x==-20 && caption.rectTransform.anchoredPosition.x==0 && caption.alignment==TextAlignmentOptions.MidlineLeft,
                "equal-width left-aligned B control column "+panel.name+"/"+name);
            Check(Mathf.Abs(footer.anchoredPosition.y+column.anchoredPosition.y+caption.rectTransform.anchoredPosition.y-usage.anchoredPosition.y-usageTitle.rectTransform.anchoredPosition.y)<.01f,
                "B three captions share baseline "+panel.name+"/"+name);
            Check(Mathf.Abs(footer.anchoredPosition.y+column.anchoredPosition.y+track.anchoredPosition.y-usage.anchoredPosition.y-usageSummary.rectTransform.anchoredPosition.y)<.01f,
                "B switches and count share centerline "+panel.name+"/"+name);
        }
        var originalCamera = canvas.worldCamera;
        var camera = Child(host, "Button raycast camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(canvas.transform.position - canvas.transform.forward * 5, canvas.transform.rotation);
        camera.orthographic = true; camera.orthographicSize = 1.2f;
        var texture = new RenderTexture(1960, 1580, 16);
        camera.targetTexture = texture; canvas.worldCamera = camera;
        try
        {
            Canvas.ForceUpdateCanvases();
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            foreach (var button in panel.GetComponentsInChildren<Button>())
            {
                Check(button.transform.Cast<Transform>().Count(t=>t.name=="Switch" && t.gameObject.activeSelf)==1, "exactly one visible switch " + panel.name + "/" + button.name);
                Check(!button.GetComponentsInChildren<TextMeshProUGUI>().Any(t=>t.name=="Status"), "no legacy ON/OFF text visible " + panel.name + "/" + button.name);
                var point = camera.WorldToScreenPoint(button.transform.position);
                var pointer = new PointerEventData(EventSystem.current) { position = point };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                raycaster.Raycast(pointer, hits);
                Check(hits.Any(h => h.gameObject == button.gameObject), "button receives UI raycast " + panel.name + "/" + button.name);
                pointer.position = camera.WorldToScreenPoint(button.transform.Find("Switch").position);
                hits.Clear(); raycaster.Raycast(pointer,hits);
                Check(hits.Any(h => h.gameObject == button.gameObject), "switch receives parent button raycast " + panel.name + "/" + button.name);
            }
        }
        finally { canvas.worldCamera = originalCamera; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(texture); }
    }
}
