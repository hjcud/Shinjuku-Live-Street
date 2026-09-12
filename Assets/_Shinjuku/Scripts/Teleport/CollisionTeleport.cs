
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;

/// <summary>
/// 로컬 사용자가 Trigger에 들어오면 화면 전환 연출 후 지정 위치로 이동
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CollisionTeleport : UdonSharpBehaviour
{
    [SerializeField] Transform TeleportTarget;
    [SerializeField] Animator DarkSlideAnimator;

    private bool isTping = false;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            if (!isTping)
            {
                isTping = true;
                DarkSlideAnimator.SetBool("IsTping", isTping);
                SendCustomEventDelayedSeconds(nameof(_TeleportPlayer), 0.58f, EventTiming.Update);
            }
        }
    }

    /// <summary>
    /// 로컬 사용자의 목적지 이동 및 화면 전환 상태 종료
    /// </summary>
    public void _TeleportPlayer()
    {
        if (!isTping) return;

        Networking.LocalPlayer.TeleportTo(TeleportTarget.position, TeleportTarget.rotation);
        isTping = false;
        DarkSlideAnimator.SetBool("IsTping", isTping);
    }
}
