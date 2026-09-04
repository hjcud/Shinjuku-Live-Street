using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 서버 시간을 기준으로 신호 주기를 계산하고 모든 클라이언트의 신호 Animator를 맞춘다.
/// </summary>
/// <remarks>
/// 최초 Master가 주기 시작 시간을 동기화하며, Master가 바뀌어도 기존 시작 시간을
/// 유지한다. 동기화가 끝나기 전에는 차량이 진입하지 않도록 적색 신호를 반환한다.
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
    /// 현재 서버 시간에 해당하는 신호 상태를 반환한다.
    /// </summary>
    /// <returns>적색, 녹색, 황색 중 하나의 신호 상수이다.</returns>
    public int GetSignalState()
    {
        if (!cycleInitialized)
        {
            // 동기화 전에는 차량이 교차로에 진입하지 않도록 적색으로 처리한다.
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
    /// 현재 신호 주기의 진행도를 Animator에서 사용할 0~1 범위로 반환한다.
    /// </summary>
    /// <returns>정규화된 신호 주기 진행도이다.</returns>
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
