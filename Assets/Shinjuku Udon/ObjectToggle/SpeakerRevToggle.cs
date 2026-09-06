
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 소유권자가 스피커의 AudioReverbFilter 활성 상태를 변경하고 동기화
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
    /// 로컬 사용자의 소유권 확보 후 Reverb 상태 변경 및 직렬화 요청
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
    /// 스피커 반환 과정에서 Reverb의 기본 활성 상태 복구 및 직렬화
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
    /// 동기화된 상태를 모든 AudioReverbFilter와 버튼 색상에 로컬 반영
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
