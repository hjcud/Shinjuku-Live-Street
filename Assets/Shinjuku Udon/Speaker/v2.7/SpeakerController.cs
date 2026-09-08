
using Nomlas.TopazChat;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;  

/// <summary>
/// 배치된 스피커의 소유자, 위치, 음량 및 연결된 미디어 기능의 초기화 관리
/// </summary>
/// <remarks>
/// 스피커 반환 상태 변경의 시작 권한을 소유권자로 제한
/// 배치와 반환 결과는 네트워크 호출로 적용하고, 늦게 참가한 사용자는 자신을 대상으로 전달된 위치 복원
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
    private float nextDistanceCheckTime;

    [Header("스피커 음량 설정")]
    [SerializeField] Slider volumeSlider;

    [Header("토글 초기화 대상 스크립트")]
    [SerializeField] ObjectGlobalToggle sketchGlobalToggle;
    [SerializeField] ObjectGlobalToggle screenGlobalToggle;
    [SerializeField] ImageLoader imageLoader;

    [Header("초기화 대상 토파즈쳇")]
    // Cuding Edit: 0.3.0의 재생/URL 초기화는 스피커별 Player 참조 하나로 처리
    // 기존 topazPlayer 연결을 유지하여 각 공연자의 독립 URL 재생과 소유권 보존
    // Assembly-CSharp에서 참조하므로 TopazChat 런타임 asmdef의 Auto Referenced 활성 필요
    [SerializeField] private Player topazPlayer;
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
        // 배치 직후 반환 입력 방지용 대기 시간은 거리 검사 주기와 별도로 유지
        if (despawnWaitTime > 0f) despawnWaitTime -= Time.deltaTime;

        // Cuding Edit: 소유자 거리 검사는 0.2초 간격으로 제한하고 제곱 거리 사용
        if (Time.time < nextDistanceCheckTime) return;
        nextDistanceCheckTime = Time.time + 0.2f;
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (isSpeakerTaken && Utilities.IsValid(localPlayer) && Networking.IsOwner(localPlayer, this.gameObject))
        {
            Vector3 playerPosition = localPlayer.GetPosition();
            float sqrDistance = (playerPosition - transform.position).sqrMagnitude;

            if (distanceLimit * distanceLimit < sqrDistance)
            {
                speakerManager.speakerOwned = false;
                SpeakerReturn();
            }
        }
    }

    /// <summary>
    /// UI 슬라이더 값을 현재 스피커 소유자의 음성 증폭값에 적용
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
    /// 버튼 입력에 따른 스피커 반환 요청
    /// </summary>
    /// <remarks>현재 소유권자이며 배치 직후 대기 시간이 끝난 경우에만 처리</remarks>
    public void SpeakerReturnTrigger()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) return;
        if (despawnWaitTime > 0f) return;

        speakerManager.speakerOwned = false;
        SpeakerReturn();
    }

    /// <summary>
    /// 소유권자에서 연결된 기능 초기화 및 전체 클라이언트에 반환 상태 전달
    /// </summary>
    public void SpeakerReturn()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            Debug.Log("[SpeakerController] Player Trying to Return Speaker is NOT Owner");
            return;
        }

        Debug.Log("[SpeakerController] Speaker Returning");
        // 스피커와 연결된 공유 기능을 먼저 끈 다음 표시 상태 반환
        sketchGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        screenGlobalToggle.SendCustomNetworkEvent(NetworkEventTarget.Owner, "OwnerDisableTarget");
        imageLoader.SendCustomNetworkEvent(NetworkEventTarget.All, "ResetTex");
        Debug.Log("[SpeakerController] Toggle Object turned off");

        // Cuding Edit: 반환하는 스피커의 Player만 초기화하며 다른 스피커의 스트림은 유지
        topazPlayer.ResetPlayer();
        VRCUrl baseUrl = topazPlayer.DefaultStreamURL;
        urlInputField.SetUrl(baseUrl);
        urlAddress.text = "";
        Debug.Log("[SpeakerController] topazchat　Reset");

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SpeakerReturnAll));
    }

    /// <summary>
    /// 모든 클라이언트에서 스피커 원위치 복귀 및 로컬 표시 상태 초기화
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

        // 이 클라이언트에서 변경했던 소유자의 음성 증폭값만 기본값으로 복원
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
        // 원격 수신자가 없으면 위치 재전송 생략
        if (VRCPlayerApi.GetPlayerCount() <= 1) return;

        if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            if (!isSpeakerTaken) return;

            Transform tempTransform = transform;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlaceSpeaker), player.playerId, tempTransform.position, tempTransform.rotation);
        }
    }

    /// <summary>
    /// 네트워크로 전달된 위치에 스피커 배치 및 소유자 정보 갱신
    /// </summary>
    /// <param name="playerId">
    /// 양수이면 늦게 참가한 해당 사용자만 위치 적용. 0 이하이면 최초 배치
    /// </param>
    /// <param name="targetPosition">적용할 스피커의 월드 위치</param>
    /// <param name="targetRotation">적용할 스피커의 월드 회전</param>
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
        nextDistanceCheckTime = 0f;
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
