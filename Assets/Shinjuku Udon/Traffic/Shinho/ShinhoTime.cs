using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 서버 시간 기준 신호 주기 계산 및 모든 클라이언트의 신호 Animator 동기화
/// </summary>
/// <remarks>
/// 최초 Master에서 주기 시작 시간 동기화 및 Master 변경 시 기존 시작 시간 유지
/// 동기화 완료 전 차량 진입 방지를 위해 적색 신호 반환
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ShinhoTime : UdonSharpBehaviour
{
    public const int SignalRed = 0;
    public const int SignalGreen = 1;
    public const int SignalYellow = 2;

    [Header("Visual")]
    [SerializeField]
    private Animator animator;

    [Header("Signal Program")]
    [SerializeField]
    private float loopTime = 160f;

    [SerializeField]
    private float redEndTime = 52.6667f;

    [SerializeField]
    private float yellowStartTime = 155f;

    [SerializeField]
    private float redStartTime = 159f;

    [UdonSynced]
    private double cycleStartServerTime;

    [UdonSynced]
    private bool cycleInitialized;

    private void Start()
    {
        if (Networking.IsMaster)
        {
            InitializeAsMaster();
        }

        ApplyAnimator();
    }

    private void Update()
    {
        ApplyAnimator();
    }

    public override void OnDeserialization()
    {
        ApplyAnimator();
    }

    public override void OnMasterTransferred(
        VRCPlayerApi newMaster)
    {
        if (newMaster == null ||
            !newMaster.isLocal)
        {
            return;
        }

        VRCPlayerApi localPlayer =
            Networking.LocalPlayer;

        if (localPlayer != null &&
            !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(
                localPlayer,
                gameObject
            );
        }

        if (!cycleInitialized)
        {
            cycleStartServerTime =
                Networking.GetServerTimeInSeconds();

            cycleInitialized = true;
        }

        RequestSerialization();
        ApplyAnimator();
    }

    /// <summary>
    /// 현재 서버 시간에 해당하는 신호 상태 반환
    /// </summary>
    /// <returns>적색, 녹색, 황색 중 하나의 신호 상수</returns>
    public int GetSignalState()
    {
        if (!cycleInitialized)
        {
            // 동기화 전 교차로 진입 방지를 위해 적색 신호 처리
            return SignalRed;
        }

        float cycleTime = GetCycleTime();

        if (cycleTime < redEndTime ||
            cycleTime >= redStartTime)
        {
            return SignalRed;
        }

        if (cycleTime < yellowStartTime)
        {
            return SignalGreen;
        }

        return SignalYellow;
    }

    /// <summary>
    /// 현재 신호 주기의 진행도를 Animator에서 사용할 0~1 범위로 반환
    /// </summary>
    /// <returns>정규화된 신호 주기 진행도</returns>
    public float GetNormalizedTime()
    {
        if (loopTime <= 0.01f)
        {
            return 0f;
        }

        return Mathf.Clamp01(
            GetCycleTime() / loopTime
        );
    }

    private void InitializeAsMaster()
    {
        VRCPlayerApi localPlayer =
            Networking.LocalPlayer;

        if (localPlayer != null &&
            !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(
                localPlayer,
                gameObject
            );
        }

        cycleStartServerTime =
            Networking.GetServerTimeInSeconds();

        cycleInitialized = true;

        RequestSerialization();
    }

    private float GetCycleTime()
    {
        if (!cycleInitialized ||
            loopTime <= 0.01f)
        {
            return 0f;
        }

        double elapsed =
            Networking.GetServerTimeInSeconds() -
            cycleStartServerTime;

        double wrapped =
            elapsed % loopTime;

        if (wrapped < 0.0)
        {
            wrapped += loopTime;
        }

        return (float)wrapped;
    }

    private void ApplyAnimator()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat(
            "ShinhoTime",
            GetNormalizedTime()
        );
    }
}
