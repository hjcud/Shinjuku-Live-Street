
using Nomlas.TopazChat;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;  

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SpeakerController : UdonSharpBehaviour
{
    #region Inspector

    [Header("스피커 좌표 싱크")]
    public bool isSpeakerTaken;

    [Header("스피커 오너 설정")]
    [SerializeField] SpeakerManager speakerManager;
    [SerializeField] private GameObject speakerObject;
    [SerializeField] private TextMeshProUGUI ownerUsernameTM;
    [SerializeField] private GameObject[] ownerObjects;
    [SerializeField] private float distanceLimit = 5f;
    private int localOwnerId;
    public float despawnWaitTime;

    [Header("스피커 음량 설정")]
    [SerializeField] Slider volumeSlider;

    [FormerlySerializedAs("SpeakerRevToggle")]
    [Header("토글 초기화 대상 스크립트")]
    [SerializeField] SpeakerRevToggle speakerRevToggle;
    [SerializeField] ObjectGlobalToggle sketchGlobalToggle;
    [SerializeField] ObjectGlobalToggle screenGlobalToggle;
    [SerializeField] ImageLoader imageLoader;

    [Header("초기화 대상 토파즈쳇")]
    [SerializeField] private Player topazPlayer;
    [SerializeField] private URLSync topazURLSync;
    [SerializeField] private VRCUrlInputField urlInputField;
    [SerializeField] private TextMeshProUGUI urlAddress;

    #endregion

    #region Speaker Despawn

    void Start()
    {
        isSpeakerTaken = false;
        localOwnerId = 0;
        despawnWaitTime = 0f;
    }

    void Update()
    {
        if (isSpeakerTaken && Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            Vector3 playerPosition = Networking.LocalPlayer.GetPosition();
            float currentDistance = (playerPosition - gameObject.transform.position).magnitude;

            if (distanceLimit < currentDistance)
            {
                // Despawn by distance
                speakerManager.speakerOwned = false;
                SpeakerReturn();
            }
        }

        // 스피커 디스폰 딜레이 계산
        if (despawnWaitTime > 0f) despawnWaitTime -= Time.deltaTime;
    }

    public void ChangeVolume()
    {
        if (isSpeakerTaken)
        {
            VRCPlayerApi targetPlayer = Networking.GetOwner(gameObject);
            localOwnerId = targetPlayer.playerId;
            float targetGain = Mathf.Lerp(0f, 24f, volumeSlider.value);
            targetPlayer.SetVoiceGain(targetGain);
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
    {
        if (isSpeakerTaken)
        {
            if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                SpeakerReturn();
            }
        }
    }

    public void SpeakerReturnTrigger() // Triggered By Button
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) return;
        if (despawnWaitTime > 0f) return;

        speakerManager.speakerOwned = false;
        SpeakerReturn();
    }

    public void SpeakerReturn() // 오너 처리 스피커 리셋 함수
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            Debug.Log("[SpeakerController] Player Trying to Return Speaker is NOT Owner");
            return;
        }

        Debug.Log("[SpeakerController] Speaker Returning");
        //Toggle Object turn off
        speakerRevToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        sketchGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        screenGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        imageLoader.SendCustomNetworkEvent(NetworkEventTarget.All, "ResetTex");
        Debug.Log("[SpeakerController] Toggle Object turned off");

        //토파즈쳇 리셋
        topazURLSync.ResetPlayer();
        VRCUrl baseUrl = topazPlayer.GetPlatformDefaultStreamURL(Platform.Windows);
        urlInputField.SetUrl(baseUrl);
        urlAddress.text = "";
        Debug.Log("[SpeakerController] topazchat　Reset");

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SpeakerReturnAll));
    }

    [NetworkCallable]
    public void SpeakerReturnAll()
    {
        Debug.Log("[SpeakerController] Speaker Local Returning");
        Transform tempTransform = transform;
        var parent = tempTransform.parent;
        tempTransform.position = parent.position;
        tempTransform.rotation = parent.rotation;
        isSpeakerTaken = false;

        if (localOwnerId != 0) // 오너 마이크 음량 설정 초기화
        {
            VRCPlayerApi targetPlayer = VRCPlayerApi.GetPlayerById(localOwnerId);
            if (Utilities.IsValid(targetPlayer)) targetPlayer.SetVoiceGain(17.5f);
            volumeSlider.value = 0.7292f;
            localOwnerId = 0;
        }

        UpdateSpeakerData();
    }

    #endregion

    #region Speaker Sync

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (VRCPlayerApi.GetPlayerCount() <= 1) return; // 플레이어가 한명일때는 동기화 필요 없음

        if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            if (!isSpeakerTaken) return;

            Transform tempTransform = transform;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlaceSpeaker), player.playerId, tempTransform.position, tempTransform.rotation);
        }
    }

    // 스피커 설치 기능 2.8
    [NetworkCallable]
    public void PlaceSpeaker(int playerId, Vector3 targetPosition, Quaternion targetRotation)
    {
        Debug.Log("[SpeakerController] Placing Speaker...");
        if (playerId > 0) // 플레이어 조인시 조인한 플레이어를 대상으로 위치 동기화 진행 (다른 플레이어는 실행하지 않음)
        {
            if (Networking.LocalPlayer.playerId != playerId) return;
        }
        else
        {
            if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                despawnWaitTime = 1f;
            }
        }

        isSpeakerTaken = true;
        Transform tempTransform = transform;
        tempTransform.position = targetPosition;
        tempTransform.rotation = targetRotation;
        UpdateSpeakerData();
    }

    private void UpdateSpeakerData()
    {
        if (isSpeakerTaken)
        {
            speakerObject.SetActive(true);
            ownerUsernameTM.text = Networking.GetOwner(gameObject).displayName;
            Debug.Log("[SpeakerController] Speaker Placed!");
        }
        else
        {
            speakerObject.SetActive(false);
            ownerUsernameTM.text = "";
            speakerManager.RecalculateUsableCount();
            Debug.Log("[SpeakerController] Speaker Hide");
        }

        if (isSpeakerTaken && Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            foreach (GameObject obj in ownerObjects)
                obj.SetActive(true);
        }
        else
        {
            foreach (GameObject obj in ownerObjects)
                obj.SetActive(false);
        }
    }

    #endregion
}