using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>지도 지명은 고정 병기, 로컬 설정은 공통 월드 언어로 한 줄 표시</summary>
/// <remarks>공연자의 표시 이름은 번역하거나 Rich Text로 해석하지 않음</remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SpeakerMapPanel : UdonSharpBehaviour
{
    [Header("지도 병기 — 같은 인덱스의 일본어와 영어 연결")]
    public TextMeshProUGUI[] labels;
    public string[] english;
    public string[] japanese;

    [Header("공유 로컬 설정")]
    public WorldLocalSettings settings;
    public WorldLanguage worldLanguage;
    public TextMeshProUGUI settingsTitle;
    public RectTransform settingsHeadingRule;
    public Vector3 settingsTitleWidths;
    public TextMeshProUGUI vehicleLabel;
    public TextMeshProUGUI ambientLabel;
    public Image vehicleState;
    public Image ambientState;
    public GameObject vehicleOffMark;
    public GameObject ambientOffMark;
    public UnityEngine.UI.Image vehicleSwitchTrack;
    public UnityEngine.UI.Image ambientSwitchTrack;
    public RectTransform vehicleSwitchThumb;
    public RectTransform ambientSwitchThumb;
    public TextMeshProUGUI vehicleSwitchLabel;
    public TextMeshProUGUI ambientSwitchLabel;

    private void Start()
    {
        if (worldLanguage != null) worldLanguage.Register(this);
        _RefreshLabels();
    }

    private void OnEnable() { _RefreshSettings(); }
    public void _OnWorldLanguageChanged() { _RefreshSettings(); }

    public void _RefreshLabels()
    {
        if (labels != null && japanese != null && english != null)
            for (int i = 0; i < labels.Length && i < japanese.Length && i < english.Length; i++)
                if (labels[i] != null) labels[i].text = Bilingual(japanese[i], english[i]);
        _RefreshSettings();
    }

    public string Bilingual(string primary, string secondary)
    {
        return primary + "\n<size=70%>" + secondary.Replace("\n", " ") + "</size>";
    }

    public void _ToggleVehicles() { if (settings != null) settings._ToggleVehicles(); }
    public void _ToggleAmbient() { if (settings != null) settings._ToggleAmbient(); }

    /// <summary>두 안내판에 동일한 현재 상태 표시</summary>
    public void _RefreshSettings()
    {
        bool vehicles = settings == null || settings.vehiclesEnabled;
        bool ambient = settings == null || settings.ambientEnabled;
        if (settingsTitle != null) settingsTitle.text = worldLanguage == null ? "Local Settings" : worldLanguage.SelectText("Local Settings", "ローカル設定", "로컬 설정");
        if (vehicleLabel != null) vehicleLabel.text = worldLanguage == null ? "Vehicles" : worldLanguage.SelectText("Vehicles", "車両", "차량");
        if (ambientLabel != null) ambientLabel.text = worldLanguage == null ? "Ambient sound" : worldLanguage.SelectText("Ambient sound", "環境音", "환경음");
        if (settingsHeadingRule != null)
        {
            int language = worldLanguage == null ? 0 : worldLanguage.GetLanguage();
            float width = language == 1 ? settingsTitleWidths.y : language == 2 ? settingsTitleWidths.z : settingsTitleWidths.x;
            float start = Mathf.Min(34 + width, 222);
            settingsHeadingRule.sizeDelta = new Vector2(222 - start, 1.5f);
            settingsHeadingRule.anchoredPosition = new Vector2(-188 + (start + 222) * .5f, 65.25f);
        }
        RefreshSwitchIcon(vehicleState,vehicles);
        RefreshSwitchIcon(ambientState,ambient);
        if (vehicleOffMark != null) vehicleOffMark.SetActive(false);
        if (ambientOffMark != null) ambientOffMark.SetActive(false);
        RefreshSwitch(vehicles, vehicleSwitchTrack, vehicleSwitchThumb, vehicleSwitchLabel);
        RefreshSwitch(ambient, ambientSwitchTrack, ambientSwitchThumb, ambientSwitchLabel);
    }

    private void RefreshSwitch(bool enabledState, UnityEngine.UI.Image track, RectTransform thumb, TextMeshProUGUI label)
    {
        if (track != null) track.color = enabledState ? new Color(.36f,.49f,.39f,1) : new Color(.51f,.52f,.47f,1);
        if (thumb != null)
        {
            thumb.anchoredPosition = new Vector2(enabledState ? -31 : 31,0);
            thumb.localScale = new Vector3(enabledState ? 1 : -1,1,1);
        }
        if (label != null)
        {
            label.text = enabledState ? "ON" : "OFF";
            label.gameObject.SetActive(false);
        }
    }

    private void RefreshSwitchIcon(UnityEngine.UI.Image icon,bool enabledState)
    {
        if (icon == null) return;
        icon.color = Color.white;
        ((RectTransform)icon.transform).anchoredPosition = new Vector2(enabledState ? 31 : -31,0);
    }
}
