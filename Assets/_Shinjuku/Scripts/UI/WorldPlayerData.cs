using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

/// <summary>
/// 로컬 플레이어의 환경설정, 누적 체류·스피커 사용 분, 방문 날짜 수와 기록 시작 날짜 저장
/// </summary>
/// <remarks>
/// 복원 전에는 메모리에만 기록하여 기존 저장값 덮어쓰기 방지
/// 위치 검사 없이 30초 주기 저장과 기존 설정·스피커 이벤트 사용
/// 퇴장 시 마지막 저장 이후 시간과 분 미만 나머지는 누락될 수 있음
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WorldPlayerData : UdonSharpBehaviour
{
    public WorldLocalSettings settings;

    [HideInInspector] public bool isRestored;
    [HideInInspector] public bool vehiclesEnabled = true;
    [HideInInspector] public bool ambientEnabled = true;
    [HideInInspector] public int totalStayMinutes;
    [HideInInspector] public int visitedDays;
    [HideInInspector] public int totalSpeakerMinutes;
    // UTC+9 날짜의 0001-01-01 기준 일수. 0은 아직 기록되지 않은 상태.
    [HideInInspector] public int recordStartDay;

    private const string Prefix = "sjk.player.";
    private const int CurrentSchemaVersion = 2;
    // 현재 저장 버전이 올라가도 초 단위 데이터의 변환 대상은 변경하지 않는다.
    private const int LastSecondsSchemaVersion = 1;
    // DateTime의 마지막 날짜(9999-12-31)를 0001-01-01 기준 일수로 표현한 값.
    private const int MaxSupportedDay = 3652058;
    private const float CheckpointSeconds = 30f;
    private bool initialized;
    // 더 새로운 형식의 저장값은 읽기만 허용하여 구버전 월드의 덮어쓰기 방지.
    private bool writesAllowed;
    // 복원 대기 중 직접 바꾼 항목만 이전 저장값보다 우선한다.
    private bool vehicleSettingChanged;
    private bool ambientSettingChanged;
    private bool saveQueued;
    private double lastAccountedTime;
    private int lastVisitedDay;
    private SpeakerController activeSpeaker;
    // 같은 스피커의 동일 배치 이벤트가 재전송되어도 사용 시작을 중복 처리하지 않음.
    private int activeGeneration;
    // 분 미만의 시간은 현재 접속에서만 유지. 여러 번의 짧은 스피커 사용도 합산.
    private double stayRemainderSeconds;
    private double speakerRemainderSeconds;

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        lastAccountedTime = Time.realtimeSinceStartupAsDouble;
        SendCustomEventDelayedSeconds(nameof(_Checkpoint), CheckpointSeconds);
    }

    /// <summary>
    /// 본인의 저장값을 한 번 복원하고 접속 중 메모리에 쌓인 기록과 병합
    /// </summary>
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal || isRestored) return;
        Initialize();
        // 메모리에 보관한 복원 전 활동은 기존 저장값에 더한다.
        int version;
        if (!PlayerData.TryGetInt(player, Prefix + "schema", out version)) version = 0;
        writesAllowed = version <= CurrentSchemaVersion;
        double seconds;
        int count;
        bool flag;
        if (PlayerData.TryGetInt(player, Prefix + "stayMinutes", out count) && count >= 0)
            totalStayMinutes = AddMinutes(totalStayMinutes, count);
        else if (version <= LastSecondsSchemaVersion && PlayerData.TryGetDouble(player, Prefix + "staySeconds", out seconds) && IsValidSeconds(seconds))
        {
            totalStayMinutes = AddMinutes(totalStayMinutes, WholeMinutes(seconds));
            stayRemainderSeconds += seconds % 60d;
        }
        if (PlayerData.TryGetInt(player, Prefix + "speakerMinutes", out count) && count >= 0)
            totalSpeakerMinutes = AddMinutes(totalSpeakerMinutes, count);
        else if (version <= LastSecondsSchemaVersion && PlayerData.TryGetDouble(player, Prefix + "speakerSeconds", out seconds) && IsValidSeconds(seconds))
        {
            totalSpeakerMinutes = AddMinutes(totalSpeakerMinutes, WholeMinutes(seconds));
            speakerRemainderSeconds += seconds % 60d;
        }
        if (PlayerData.TryGetInt(player, Prefix + "visitedDays", out count) && count > 0) visitedDays = count;
        if (PlayerData.TryGetInt(player, Prefix + "lastVisitedDay", out count) && count > 0) lastVisitedDay = count;
        // 과거 첫 방문일을 추정하지 않는다. 이 필드가 도입된 뒤 처음 복원한 날만 기록.
        if (PlayerData.TryGetInt(player, Prefix + "recordStartDay", out count) && count > 0 && count <= MaxSupportedDay)
            recordStartDay = count;
        else if (writesAllowed)
            recordStartDay = GetTodayDay();
        // 로딩 중 사용자가 직접 바꾼 설정은 늦게 도착한 저장값보다 우선한다.
        if (!vehicleSettingChanged && PlayerData.TryGetBool(player, Prefix + "vehicles", out flag)) vehiclesEnabled = flag;
        if (!ambientSettingChanged && PlayerData.TryGetBool(player, Prefix + "ambient", out flag)) ambientEnabled = flag;

        // 이전 초 단위 나머지와 이번 접속의 나머지를 합쳐 생긴 온전한 분을 반영.
        NormalizeMinutes();
        isRestored = true;
        if (settings != null) settings._ApplySavedSettings(vehiclesEnabled, ambientEnabled);
        if (!writesAllowed) Debug.LogWarning("[WorldPlayerData] Newer save schema detected; saving disabled for this world version.");
        _Save();
    }

    private bool IsValidSeconds(double value)
    {
        return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // 매우 큰 누적값도 음수로 넘치지 않도록 정수 최댓값에서 유지한다.
    private int WholeMinutes(double seconds)
    {
        return seconds >= (double)int.MaxValue * 60d ? int.MaxValue : (int)(seconds / 60d);
    }

    private int AddMinutes(int total, int minutes)
    {
        return minutes > int.MaxValue - total ? int.MaxValue : total + minutes;
    }

    private void NormalizeMinutes()
    {
        totalStayMinutes = AddMinutes(totalStayMinutes, WholeMinutes(stayRemainderSeconds));
        totalSpeakerMinutes = AddMinutes(totalSpeakerMinutes, WholeMinutes(speakerRemainderSeconds));
        stayRemainderSeconds %= 60d;
        speakerRemainderSeconds %= 60d;
    }

    private void AccountTime()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double elapsed = now - lastAccountedTime;
        lastAccountedTime = now;
        if (elapsed <= 0d) return;
        stayRemainderSeconds += elapsed;
        if (activeSpeaker != null) speakerRemainderSeconds += elapsed;
        NormalizeMinutes();
    }

    private int GetTodayDay()
    {
        return (int)(DateTime.UtcNow.AddHours(9d).Ticks / TimeSpan.TicksPerDay);
    }

    private void CountVisitDay()
    {
        // 모든 방문자에게 동일한 UTC+9 날짜 적용. 자정 통과도 방문일로 인정.
        int today = GetTodayDay();
        // 같은 날 재접속과 기기 시계 역행은 방문 날짜 수를 늘리지 않는다.
        if (today <= lastVisitedDay) return;
        lastVisitedDay = today;
        if (visitedDays < int.MaxValue) visitedDays++;
    }

    /// <summary>
    /// 복원 전에는 메모리에만 누적하고, 복원 후에는 저장 함수에서 집계와 기록 처리
    /// </summary>
    public void _Checkpoint()
    {
        if (!initialized)
        {
            Initialize();
            return;
        }
        if (isRestored)
        {
            _Save();
        }
        else
        {
            AccountTime();
        }
        SendCustomEventDelayedSeconds(nameof(_Checkpoint), CheckpointSeconds);
    }

    /// <summary>
    /// 사용자가 직접 바꾼 차량 표시 설정을 기록하고 저장 예약
    /// </summary>
    public void _RecordVehicleSetting(bool value)
    {
        vehiclesEnabled = value;
        vehicleSettingChanged = true;
        QueueSave();
    }

    /// <summary>
    /// 사용자가 직접 바꾼 환경음 설정을 기록하고 저장 예약
    /// </summary>
    public void _RecordAmbientSetting(bool value)
    {
        ambientEnabled = value;
        ambientSettingChanged = true;
        QueueSave();
    }

    /// <summary>
    /// 본인의 실제 배치가 적용된 시점부터 스피커 사용 시간 집계 시작
    /// </summary>
    /// <remarks>
    /// 실제 발화 여부는 감지하지 않으며 배치부터 반환까지의 장비 사용 시간만 집계
    /// </remarks>
    public void _RecordSpeakerPlaced(SpeakerController speaker, int generation)
    {
        if (speaker == null || !speaker.IsLocalPerformer() || generation <= 0) return;
        if (activeSpeaker == speaker && activeGeneration == generation) return;
        Initialize();
        AccountTime();
        activeSpeaker = speaker;
        activeGeneration = generation;
        QueueSave();
    }

    /// <summary>
    /// 반환 상태가 적용되면 해당 스피커의 시간 집계를 종료하고 분 미만 나머지는 유지
    /// </summary>
    public void _RecordSpeakerReturned(SpeakerController speaker)
    {
        if (speaker == null || activeSpeaker != speaker) return;
        AccountTime();
        activeSpeaker = null;
        activeGeneration = 0;
        QueueSave();
    }

    private void QueueSave()
    {
        if (!isRestored || !writesAllowed || saveQueued) return;
        saveQueued = true;
        // 연속 토글/같은 동작에서 발생한 이벤트를 한 번의 저장으로 합친다.
        SendCustomEventDelayedSeconds(nameof(_FlushPendingSave), 1f);
    }

    /// <summary>
    /// 모아 둔 설정·스피커 이벤트의 저장 요청을 처리
    /// </summary>
    public void _FlushPendingSave()
    {
        // 정기 체크포인트가 먼저 저장했다면 남아 있는 지연 이벤트는 쓰기 생략.
        if (saveQueued) _Save();
    }

    /// <summary>
    /// 복원 후의 시간·방문 날짜를 집계하고 지원하는 저장 형식에만 기록
    /// </summary>
    public void _Save()
    {
        saveQueued = false;
        if (!isRestored || !Utilities.IsValid(Networking.LocalPlayer)) return;
        AccountTime();
        CountVisitDay();
        // 미래 버전 데이터는 덮어쓰지 않되 현재 접속의 메모리 집계는 계속 유지.
        if (!writesAllowed) return;
        PlayerData.SetInt(Prefix + "schema", CurrentSchemaVersion);
        PlayerData.SetBool(Prefix + "vehicles", vehiclesEnabled);
        PlayerData.SetBool(Prefix + "ambient", ambientEnabled);
        PlayerData.SetInt(Prefix + "stayMinutes", totalStayMinutes);
        PlayerData.SetInt(Prefix + "visitedDays", visitedDays);
        PlayerData.SetInt(Prefix + "lastVisitedDay", lastVisitedDay);
        PlayerData.SetInt(Prefix + "speakerMinutes", totalSpeakerMinutes);
        PlayerData.SetInt(Prefix + "recordStartDay", recordStartDay);
    }
}
