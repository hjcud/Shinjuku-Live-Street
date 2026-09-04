
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

/// <summary>
/// 배치된 스피커의 소유자, 위치, 음량, 연결된 미디어 기능의 초기화를 관리한다.
/// </summary>
/// <remarks>
/// 스피커를 반환하는 상태 변경은 소유권자만 시작한다. 배치와 반환 결과는 네트워크
/// 호출로 적용하며, 늦게 참가한 사용자는 자신을 대상으로 전달된 위치를 복원한다.
/// </remarks>
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
                speakerManager.speakerOwned = false;
                SpeakerReturn();
            }
        }

        // 배치 직후 반환 입력을 막는 대기 시간을 매 프레임 줄인다.
        if (despawnWaitTime > 0f) despawnWaitTime -= Time.deltaTime;
    }

    /// <summary>
    /// UI 슬라이더 값을 현재 스피커 소유자의 음성 증폭값에 적용한다.
    /// </summary>
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

    /// <summary>
    /// 버튼 입력으로 스피커 반환을 요청한다.
    /// </summary>
    /// <remarks>현재 소유권자이며 배치 직후 대기 시간이 끝난 경우에만 처리한다.</remarks>
    public void SpeakerReturnTrigger()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) return;
        if (despawnWaitTime > 0f) return;

        speakerManager.speakerOwned = false;
        SpeakerReturn();
    }

    /// <summary>
    /// 소유권자에서 연결된 기능을 초기화하고 전체 클라이언트에 반환 상태를 전달한다.
    /// </summary>
    public void SpeakerReturn()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            Debug.Log("[SpeakerController] Player Trying to Return Speaker is NOT Owner");
            return;
        }

        Debug.Log("[SpeakerController] Speaker Returning");
        // 스피커와 연결된 공유 기능을 먼저 끈 다음 표시 상태를 반환한다.
        speakerRevToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        sketchGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        screenGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        imageLoader.SendCustomNetworkEvent(NetworkEventTarget.All, "ResetTex");
        Debug.Log("[SpeakerController] Toggle Object turned off");

        // 다음 사용자가 이전 스트림을 이어받지 않도록 TopazChat 상태와 URL을 초기화한다.
        topazURLSync.ResetPlayer();
        VRCUrl baseUrl = topazPlayer.GetPlatformDefaultStreamURL(Platform.Windows);
        urlInputField.SetUrl(baseUrl);
        urlAddress.text = "";
        Debug.Log("[SpeakerController] topazchat　Reset");

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SpeakerReturnAll));
    }

    /// <summary>
    /// 모든 클라이언트에서 스피커를 원래 위치로 되돌리고 로컬 표시 상태를 초기화한다.
    /// </summary>
    [NetworkCallable]
    public void SpeakerReturnAll()
    {
        Debug.Log("[SpeakerController] Speaker Local Returning");
        Transform tempTransform = transform;
        var parent = tempTransform.parent;
        tempTransform.position = parent.position;
        tempTransform.rotation = parent.rotation;
        isSpeakerTaken = false;

        // 이 클라이언트에서 변경했던 소유자의 음성 증폭값만 기본값으로 되돌린다.
        if (localOwnerId != 0)
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
        // 원격 수신자가 없으면 위치를 다시 전송할 필요가 없다.
        if (VRCPlayerApi.GetPlayerCount() <= 1) return;

        if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            if (!isSpeakerTaken) return;

            Transform tempTransform = transform;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlaceSpeaker), player.playerId, tempTransform.position, tempTransform.rotation);
        }
    }

    /// <summary>
    /// 네트워크로 전달된 위치에 스피커를 배치하고 소유자 정보를 갱신한다.
    /// </summary>
    /// <param name="playerId">
    /// 양수이면 늦게 참가한 해당 사용자만 위치를 적용한다. 0 이하이면 최초 배치이다.
    /// </param>
    /// <param name="targetPosition">적용할 스피커의 월드 위치이다.</param>
    /// <param name="targetRotation">적용할 스피커의 월드 회전이다.</param>
    [NetworkCallable]
    public void PlaceSpeaker(int playerId, Vector3 targetPosition, Quaternion targetRotation)
    {
        Debug.Log("[SpeakerController] Placing Speaker...");
        if (playerId > 0)
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
