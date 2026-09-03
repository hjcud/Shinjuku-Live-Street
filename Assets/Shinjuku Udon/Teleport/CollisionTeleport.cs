
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;

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

    public void TeleportPlayer()
    {
        Networking.LocalPlayer.TeleportTo(TeleportTarget.position, TeleportTarget.rotation);
        isTping = false;
        DarkSlideAnimator.SetBool("IsTping", isTping);
    }
}
