using UdonSharp;
using UnityEngine;

/// <summary>
/// 지도 패널과 기존 월드 스위치에서 공유하는 로컬 차량 표시 및 환경음 설정
/// </summary>
/// <remarks>
/// 적용은 로컬 전용이며 저장해도 다른 플레이어의 설정과 교통 계산에는 영향 없음
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WorldLocalSettings : UdonSharpBehaviour
{
    [Header("로컬 설정 대상")]
    public TrafficSimulationManager traffic;
    public AudioSource[] ambientSources;
    public SpeakerMapPanel[] panels;
    public WorldPlayerData playerData;
    // 지도 패널이 없는 전시 씬에서도 기존 스위치의 표시를 복원한다.
    public ObjectLocalToggle vehicleSwitch;
    public ObjectLocalToggle ambientSwitch;

    [HideInInspector] public bool vehiclesEnabled = true;
    [HideInInspector] public bool ambientEnabled = true;

    private bool initialized;
    // 설정을 다시 켜도 원래 꺼져 있던 AudioSource는 활성화하지 않는다.
    private bool[] originalAudioEnabled;
    // 환경음 OFF 직전에 재생 중이던 음원을 기억하여 다시 켤 때 재생 여부 복구.
    private bool[] resumeAudio;

    private void Start()
    {
        Initialize();
        RefreshControls();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        int count = ambientSources == null ? 0 : ambientSources.Length;
        originalAudioEnabled = new bool[count];
        resumeAudio = new bool[count];
        for (int i = 0; i < count; i++)
            if (ambientSources[i] != null) originalAudioEnabled[i] = ambientSources[i].enabled;
    }

    /// <summary>
    /// 차량 표시만 전환하며 소유권자의 계산과 네트워크 송신은 계속 유지
    /// </summary>
    public void _ToggleVehicles()
    {
        if (traffic == null) return;
        vehiclesEnabled = !vehiclesEnabled;
        traffic._SetLocalVehiclesVisible(vehiclesEnabled);
        RefreshControls();
        if (playerData != null) playerData._RecordVehicleSetting(vehiclesEnabled);
    }

    /// <summary>
    /// 환경음만 전환하며 구역 트리거의 활성 상태와 원래 비활성 음원은 유지
    /// </summary>
    public void _ToggleAmbient()
    {
        Initialize();
        ApplyAmbient(!ambientEnabled);
        RefreshControls();
        if (playerData != null) playerData._RecordAmbientSetting(ambientEnabled);
    }

    /// <summary>
    /// 토글을 거치지 않고 저장된 상태를 직접 적용하여 중복 복원 시 상태 반전 방지
    /// </summary>
    public void _ApplySavedSettings(bool vehicles, bool ambient)
    {
        Initialize();
        vehiclesEnabled = vehicles;
        if (traffic != null) traffic._SetLocalVehiclesVisible(vehiclesEnabled);
        ApplyAmbient(ambient);
        RefreshControls();
    }

    private void ApplyAmbient(bool enabledState)
    {
        if (ambientEnabled == enabledState) return;
        ambientEnabled = enabledState;
        for (int i = 0; i < originalAudioEnabled.Length; i++)
        {
            AudioSource source = ambientSources[i];
            if (source == null) continue;
            if (!ambientEnabled)
            {
                resumeAudio[i] = source.isPlaying;
                source.enabled = false;
            }
            else
            {
                source.enabled = originalAudioEnabled[i];
                // 비활성 구역에서는 재생하지 않는다. Play는 재생 위치 이어하기를 보장하지 않음.
                if (source.enabled && source.gameObject.activeInHierarchy &&
                    (resumeAudio[i] || source.playOnAwake) && !source.isPlaying)
                {
                    source.Play();
                }
            }
        }
    }

    private void RefreshControls()
    {
        if (vehicleSwitch != null) vehicleSwitch._ApplySettingState(vehiclesEnabled);
        if (ambientSwitch != null) ambientSwitch._ApplySettingState(ambientEnabled);
        if (panels == null) return;
        for (int i = 0; i < panels.Length; i++)
            if (panels[i] != null) panels[i]._RefreshSettings();
    }
}
