
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;

/// <summary>
/// 로컬 사용자가 Trigger에 들어오면 화면 전환 연출 뒤 지정 위치로 이동시킨다.
/// </summary>
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
                SendCustomEventDelayedSeconds("TeleportPlayer", 0.58f, EventTiming.Update);
            }
        }
    }

    /// <summary>
    /// 로컬 사용자를 목적지로 이동시키고 화면 전환 상태를 종료한다.
    /// </summary>
    public void TeleportPlayer()
    {
        Networking.LocalPlayer.TeleportTo(TeleportTarget.position, TeleportTarget.rotation);
        isTping = false;
        DarkSlideAnimator.SetBool("IsTping", isTping);
    }
}
