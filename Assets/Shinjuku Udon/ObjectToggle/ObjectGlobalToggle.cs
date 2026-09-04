
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 하나의 GameObject 활성 상태를 소유권자가 변경하고 모든 사용자에게 동기화한다.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ObjectGlobalToggle : UdonSharpBehaviour
{
    [SerializeField] private bool activeDefault;
    [SerializeField] private GameObject targetObject;
    [UdonSynced] bool isObjectActive = false;
    public Text ButtonText;

    void Start()
    {
        targetObject.SetActive(activeDefault);
        isObjectActive = targetObject.activeSelf;
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if(Networking.LocalPlayer == player)
        {
            ToggleTarget();
        }
    }

    /// <summary>
    /// 로컬 사용자가 소유권을 확보한 뒤 대상 상태를 바꾸고 수동 직렬화를 요청한다.
    /// </summary>
    public void ButtonTrigger()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        isObjectActive = !isObjectActive;

        ToggleTarget();
        RequestSerialization();
    }

    /// <summary>
    /// 현재 소유권자에서 대상을 비활성화하고 변경된 상태를 직렬화한다.
    /// </summary>
    public void OwnerDisableTarget()
    {
        isObjectActive = false;
        RequestSerialization();
        ToggleTarget();
    }

    public override void OnDeserialization()
    {
        ToggleTarget();
    }

    /// <summary>
    /// 동기화된 활성 상태를 대상과 버튼 색상에 로컬로 반영한다.
    /// </summary>
    public void ToggleTarget()
    {
        targetObject.SetActive(isObjectActive);
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }
}
