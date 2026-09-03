using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

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

    public int GetSignalState()
    {
        if (!cycleInitialized)
        {
            // 동기화 전에는 안전하게 정지 상태로 처리
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
