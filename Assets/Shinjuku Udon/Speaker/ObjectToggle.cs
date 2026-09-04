
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 대상 GameObject의 활성 상태를 네트워크 이벤트로 전환하고 새 참가자에게 현재 상태를 전달한다.
/// </summary>
public class ObjectToggle : UdonSharpBehaviour
{
    [SerializeField] private GameObject targetObject;

    /// <summary>
    /// 모든 사용자에게 대상의 활성 상태 전환을 요청한다.
    /// </summary>
    public void ButtonTrigger()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ToggleObject");
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if(Networking.IsMaster)
        {
            if(targetObject.activeSelf)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ToggleTargetTrue");
            }
            else
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ToggleTargetFalse");
            }
        }
    }

    /// <summary>
    /// 현재 로컬 대상의 활성 상태를 반전한다.
    /// </summary>
    public void ToggleObject()
    {
        targetObject.SetActive(!targetObject.activeSelf);
    }

    /// <summary>
    /// 새 참가자 상태 복원에 사용할 활성화 이벤트이다.
    /// </summary>
    public void ToggleTargetTrue()
    {
        targetObject.SetActive(true);
    }

    /// <summary>
    /// 새 참가자 상태 복원에 사용할 비활성화 이벤트이다.
    /// </summary>
    public void ToggleTargetFalse()
    {
        targetObject.SetActive(false);
    }
}
