
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectToggle : UdonSharpBehaviour
{
    [SerializeField] private GameObject targetObject;

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

    public void ToggleObject()
    {
        targetObject.SetActive(!targetObject.activeSelf);
    }

    public void ToggleTargetTrue()
    {
        targetObject.SetActive(true);
    }

    public void ToggleTargetFalse()
    {
        targetObject.SetActive(false);
    }
}
