
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 스피커의 AudioReverbFilter 활성 상태를 소유권자가 변경하고 동기화한다.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SpeakerRevToggle : UdonSharpBehaviour
{
    [SerializeField] private AudioReverbFilter[] targetObjects;
    [UdonSynced] bool isObjectActive = false;
    public Text ButtonText;

    void Start()
    {
        isObjectActive = targetObjects[0].enabled;
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
    /// 로컬 사용자가 소유권을 확보한 뒤 Reverb 상태를 바꾸고 직렬화를 요청한다.
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
    /// 스피커 반환 과정에서 Reverb를 기본 활성 상태로 복구하고 직렬화한다.
    /// </summary>
    public void OwnerDisableTarget()
    {
        isObjectActive = true;
        ToggleTarget();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        ToggleTarget();
    }

    /// <summary>
    /// 동기화된 상태를 모든 AudioReverbFilter와 버튼 색상에 로컬로 반영한다.
    /// </summary>
    public void ToggleTarget()
    {
        foreach(AudioReverbFilter targetObject in targetObjects)
        {
            targetObject.enabled = isObjectActive;
        }
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }
}
