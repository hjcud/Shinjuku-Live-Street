
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
    private VRCPlayerApi voiceGainPlayer;
    public float despawnWaitTime;
    private float nextDistanceCheckTime;
    // 설치 당시의 접속 참조를 유지하여 퇴장 후 같은 숫자 ID를 받은 사용자와 구분
    private VRCPlayerApi placementOwner;
    private bool placedByLocalPlayer;
    private bool departedCleanupPending;
    // 슬롯별로 매니저가 발급한 배치 번호. 반환 뒤에도 유지해 지연 메시지를 거부한다.
    private int placementGeneration;
    private bool placementClosed;
    // 매니저가 확인한 배정. 실제 배치 상태와 분리하여 선행 승인도 기억한다.
    private int approvedGeneration;
    private VRCPlayerApi approvedPlayer;
    private bool allocationClosed;
    // 승인보다 먼저 온 메시지는 호출자별로 보관한다. 다른 호출자가 덮어쓸 수 없다.
    private VRCPlayerApi[] waitingCallers = new VRCPlayerApi[0];
    private int[] waitingGenerations = new int[0];
    private Vector3[] waitingPositions = new Vector3[0];
    private Quaternion[] waitingRotations = new Quaternion[0];
    private bool[] waitingReturns = new bool[0];
    private bool[] waitingInitialPlacements = new bool[0];

    [Header("스피커 음량 설정")]
    [SerializeField] Slider volumeSlider;

    [Header("토글 초기화 대상 스크립트")]
    [SerializeField] ObjectGlobalToggle sketchGlobalToggle;
    [SerializeField] ObjectGlobalToggle screenGlobalToggle;
    [SerializeField] ImageLoader imageLoader;

    [Header("초기화 대상 토파즈쳇")]
    // 0.3.0의 재생/URL 초기화는 스피커별 Player 참조 하나로 처리
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
        voiceGainPlayer = null;
        despawnWaitTime = 0f;
    }

    void Update()
    {
        // 배치 직후 반환 입력 방지용 대기 시간은 거리 검사 주기와 별도로 유지
        if (despawnWaitTime > 0f) despawnWaitTime -= Time.deltaTime;

        // 소유자 거리 검사는 0.2초 간격으로 제한하고 제곱 거리 사용
        if (Time.time < nextDistanceCheckTime) return;
        nextDistanceCheckTime = Time.time + 0.2f;
        ApplyWaitingMessages();
        // 퇴장 이벤트와 소유권 이전의 처리 순서 차이를 다음 검사에서 재확인
        if (departedCleanupPending)
        {
            _RetryDepartedCleanup();
            return;
        }
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (isSpeakerTaken && !Utilities.IsValid(placementOwner))
        {
            departedCleanupPending = true;
            ApplyLocalReturn(false);
            _RetryDepartedCleanup();
            return;
        }
        if (IsLocalPerformer() && Networking.IsOwner(localPlayer, this.gameObject))
        {
            Vector3 playerPosition = localPlayer.GetPosition();
            float sqrDistance = (playerPosition - transform.position).sqrMagnitude;

            if (distanceLimit * distanceLimit < sqrDistance)
            {
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
            // SDK 소유권 전파가 늦어도 표시된 공연자에게만 음량 적용
            VRCPlayerApi targetPlayer = placementOwner;
            if (!Utilities.IsValid(targetPlayer)) return;
            voiceGainPlayer = targetPlayer;
            float targetGain = Mathf.Lerp(0f, 24f, volumeSlider.value);
            targetPlayer.SetVoiceGain(targetGain);
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
    {
        if (departedCleanupPending)
        {
            _RetryDepartedCleanup();
            return;
        }
        // 관리 권한의 인계만으로 직접 설치한 상태로 변경하지 않도록 구분
        // 실제 설치자의 퇴장 확인 전에는 소유권 전파 중인 정상 배치 유지
        if (isSpeakerTaken && !Utilities.IsValid(placementOwner))
        {
            departedCleanupPending = true;
            ApplyLocalReturn(false);
            _RetryDepartedCleanup();
        }
        else UpdateSpeakerData();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!isSpeakerTaken || placementOwner != player) return;
        // 퇴장한 사용자 대신 남은 각 클라이언트에서 표시 상태 즉시 반환
        departedCleanupPending = true;
        ApplyLocalReturn(false);
        _RetryDepartedCleanup();
    }

    public void _RetryDepartedCleanup()
    {
        if (!departedCleanupPending || !Networking.IsOwner(gameObject)) return;
        SpeakerReturn();
    }

    /// <summary>
    /// 버튼 입력에 따른 스피커 반환 요청
    /// </summary>
    /// <remarks>현재 소유권자이며 배치 직후 대기 시간이 끝난 경우에만 처리</remarks>
    public void SpeakerReturnTrigger()
    {
        if (!IsLocalPerformer() || !Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) return;
        if (despawnWaitTime > 0f) return;

        SpeakerReturn();
    }

    /// <summary>
    /// 소유권자에서 연결된 기능 초기화 및 전체 클라이언트에 반환 상태 전달
    /// </summary>
    public void SpeakerReturn()
    {
        if (!isSpeakerTaken && !departedCleanupPending) return;
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

        // 반환하는 스피커의 Player만 초기화하며 다른 스피커의 스트림은 유지
        topazPlayer.ResetPlayer();
        VRCUrl baseUrl = topazPlayer.DefaultStreamURL;
        urlInputField.SetUrl(baseUrl);
        urlAddress.text = "";
        Debug.Log("[SpeakerController] topazchat　Reset");

        int returningGeneration = placementGeneration;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SpeakerReturnAll), returningGeneration);
        speakerManager._ReleaseSpeaker(this, returningGeneration);
    }

    /// <summary>
    /// 모든 클라이언트에서 스피커 원위치 복귀 및 로컬 표시 상태 초기화
    /// </summary>
    [NetworkCallable]
    public void SpeakerReturnAll(int generation)
    {
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        if (generation <= 0 || generation < placementGeneration) return;
        WaitForAllocation(caller, generation, Vector3.zero, Quaternion.identity, true, false);
    }

    private void ApplyLocalReturn(bool cleanupCompleted)
    {
        Debug.Log("[SpeakerController] Speaker Local Returning");
        Transform tempTransform = transform;
        var parent = tempTransform.parent;
        tempTransform.SetPositionAndRotation(parent.position, parent.rotation);
        isSpeakerTaken = false;
        placementClosed = true;
        placementOwner = null;
        placedByLocalPlayer = false;
        if (cleanupCompleted)
        {
            departedCleanupPending = false;
        }

        // 이 클라이언트에서 변경했던 소유자의 음성 증폭값만 기본값으로 복원
        if (voiceGainPlayer != null)
        {
            if (Utilities.IsValid(voiceGainPlayer)) voiceGainPlayer.SetVoiceGain(17.5f);
            volumeSlider.value = 0.7292f;
            voiceGainPlayer = null;
        }

        UpdateSpeakerData();
    }

    #endregion

    #region Speaker Sync

    // 매니저 승인 후에만 소유권 확보. 비동기 소유권 콜백에서 자동 반환하지 않도록 기록
    public bool _PlaceGrantedSpeaker(Vector3 position, Quaternion rotation, int generation)
    {
        if (!IsAvailableForPlacement() || generation <= placementGeneration) return false;
        if (allocationClosed || generation != approvedGeneration || approvedPlayer != Networking.LocalPlayer) return false;
        placementOwner = Networking.LocalPlayer;
        placedByLocalPlayer = true;
        isSpeakerTaken = false;
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (!Networking.IsOwner(gameObject))
        {
            placementOwner = null;
            placedByLocalPlayer = false;
            return false;
        }
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlaceSpeaker), 0, position, rotation, generation);
        return true;
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        // 원격 수신자가 없으면 위치 재전송 생략
        if (VRCPlayerApi.GetPlayerCount() <= 1) return;

        // 인계받은 관리자가 아닌 실제 설치자만 자신의 배치 상태 재전송
        if (IsLocalPerformer() && Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            if (!isSpeakerTaken) return;

            Transform tempTransform = transform;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PlaceSpeaker), player.playerId, tempTransform.position, tempTransform.rotation, placementGeneration);
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
    /// <param name="generation">매니저가 슬롯에 발급한 단조 증가 배치 번호</param>
    [NetworkCallable]
    public void PlaceSpeaker(int playerId, Vector3 targetPosition, Quaternion targetRotation, int generation)
    {
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        if (generation <= 0 || generation < placementGeneration) return;
        if (playerId > 0 && Networking.LocalPlayer.playerId != playerId) return;
        WaitForAllocation(caller, generation, targetPosition, targetRotation, false, playerId <= 0);
    }

    // 네트워크 호출 불가. 매니저의 승인 이벤트 또는 동기화된 배정표에서만 갱신한다.
    public void _ApplyAllocation(VRCPlayerApi player, int generation)
    {
        if (generation <= 0 || generation < approvedGeneration) return;
        if (generation == approvedGeneration)
        {
            if (allocationClosed) return;
            if (player != null && approvedPlayer != player) return;
        }
        else
        {
            approvedGeneration = generation;
            approvedPlayer = player;
        }
        allocationClosed = player == null;
        if (allocationClosed && generation >= placementGeneration)
        {
            // 퇴장 후 미디어 정리는 기존 소유권 인계 절차가 끝까지 수행한다.
            if (isSpeakerTaken && !Utilities.IsValid(placementOwner)) departedCleanupPending = true;
            placementGeneration = generation;
            ApplyLocalReturn(!departedCleanupPending);
            _RetryDepartedCleanup();
        }
        ApplyWaitingMessages();
    }

    public int _GetApprovedGeneration() { return approvedGeneration; }

    public int _GetAllocatedPlayerId()
    {
        if (placementClosed && placementGeneration == approvedGeneration) return 0;
        return !allocationClosed && Utilities.IsValid(approvedPlayer) ? approvedPlayer.playerId : 0;
    }

    private void WaitForAllocation(VRCPlayerApi caller, int generation, Vector3 position, Quaternion rotation, bool returning, bool initialPlacement)
    {
        if (generation < approvedGeneration ||
            (generation == approvedGeneration && allocationClosed && !(returning && departedCleanupPending))) return;
        if (generation == approvedGeneration && !returning && caller != approvedPlayer) return;
        int index = -1;
        for (int i = 0; i < waitingCallers.Length; i++)
        {
            if (waitingCallers[i] == caller)
            {
                if (generation < waitingGenerations[i]) return;
                if (generation == waitingGenerations[i] && waitingReturns[i]) return;
                index = i;
                break;
            }
            if (index < 0 && !Utilities.IsValid(waitingCallers[i])) index = i;
        }
        if (index < 0)
        {
            // 한 접속당 한 항목만 유지하고 비어 있거나 퇴장한 항목은 재사용한다.
            index = waitingCallers.Length;
            VRCPlayerApi[] callers = new VRCPlayerApi[index + 1];
            int[] generations = new int[index + 1];
            Vector3[] positions = new Vector3[index + 1];
            Quaternion[] rotations = new Quaternion[index + 1];
            bool[] returns = new bool[index + 1];
            bool[] initialPlacements = new bool[index + 1];
            for (int i = 0; i < index; i++)
            {
                callers[i] = waitingCallers[i];
                generations[i] = waitingGenerations[i];
                positions[i] = waitingPositions[i];
                rotations[i] = waitingRotations[i];
                returns[i] = waitingReturns[i];
                initialPlacements[i] = waitingInitialPlacements[i];
            }
            waitingCallers = callers;
            waitingGenerations = generations;
            waitingPositions = positions;
            waitingRotations = rotations;
            waitingReturns = returns;
            waitingInitialPlacements = initialPlacements;
        }
        waitingCallers[index] = caller;
        waitingGenerations[index] = generation;
        waitingPositions[index] = position;
        waitingRotations[index] = rotation;
        waitingReturns[index] = returning;
        waitingInitialPlacements[index] = initialPlacement;
        ApplyWaitingMessages();
    }

    private void ApplyWaitingMessages()
    {
        for (int i = 0; i < waitingCallers.Length; i++)
        {
            VRCPlayerApi caller = waitingCallers[i];
            int generation = waitingGenerations[i];
            if (!Utilities.IsValid(caller) || generation < approvedGeneration || generation < placementGeneration ||
                (generation == approvedGeneration && allocationClosed && !(waitingReturns[i] && departedCleanupPending)))
            {
                waitingCallers[i] = null;
                continue;
            }
            if (generation != approvedGeneration) continue;
            if (waitingReturns[i] && caller != approvedPlayer)
            {
                // 정리 담당자의 메시지가 퇴장/소유권 정보보다 먼저 왔다면 계속 보류한다.
                if (Utilities.IsValid(approvedPlayer) || caller != Networking.GetOwner(gameObject)) continue;
            }
            waitingCallers[i] = null;
            if (waitingReturns[i])
            {
                placementGeneration = generation;
                ApplyLocalReturn(true);
            }
            else if (caller == approvedPlayer)
            {
                ApplyPlacement(caller, generation, waitingPositions[i], waitingRotations[i], waitingInitialPlacements[i]);
            }
        }
    }

    private void ApplyPlacement(VRCPlayerApi caller, int generation, Vector3 targetPosition, Quaternion targetRotation, bool initialPlacement)
    {
        if (generation == placementGeneration &&
            (placementClosed || (isSpeakerTaken && caller != placementOwner))) return;
        Debug.Log("[SpeakerController] Placing Speaker...");
        if (initialPlacement)
        {
            if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                despawnWaitTime = 1f;
            }
        }

        // SDK 소유권 전파와 독립적으로 세대를 비교하므로 정상 선행 배치는 허용한다.
        if (generation > placementGeneration && isSpeakerTaken)
        {
            // 중간 반환을 놓쳤어도 이전 공연자의 로컬 음량 설정은 복구한다.
            bool localPlacement = placedByLocalPlayer && caller == Networking.LocalPlayer;
            ApplyLocalReturn(true);
            placedByLocalPlayer = localPlacement;
        }
        placementGeneration = generation;
        placementClosed = false;
        isSpeakerTaken = true;
        // 위치 이벤트와 SDK 소유권 전파 순서가 달라도 실제 배치 요청자를 기준으로 표시
        placementOwner = caller;
        if (caller != Networking.LocalPlayer) placedByLocalPlayer = false;
        departedCleanupPending = false;
        nextDistanceCheckTime = 0f;
        Transform tempTransform = transform;
        tempTransform.SetPositionAndRotation(targetPosition, targetRotation);
        UpdateSpeakerData();
    }

    private void UpdateSpeakerData()
    {
        if (isSpeakerTaken)
        {
            speakerObject.SetActive(true);
            VRCPlayerApi performer = placementOwner;
            ownerUsernameTM.text = Utilities.IsValid(performer) ? performer.displayName : "";
            Debug.Log("[SpeakerController] Speaker Placed!");
        }
        else
        {
            speakerObject.SetActive(false);
            ownerUsernameTM.text = "";
            Debug.Log("[SpeakerController] Speaker Hide");
        }

        // 최초 배치/반환/늦은 참가자 복원 모두 실제 슬롯 상태에서 수량 계산
        speakerManager.RecalculateUsableCount();

        bool showOwnerObjects = IsLocalPerformer() && Networking.IsOwner(Networking.LocalPlayer, this.gameObject);
        foreach (GameObject obj in ownerObjects)
            obj.SetActive(showOwnerObjects);
    }

    public bool IsLocalPerformer()
    {
        return placedByLocalPlayer && IsPerformer(Networking.LocalPlayer);
    }

    public bool IsPerformer(VRCPlayerApi player)
    {
        return isSpeakerTaken && Utilities.IsValid(placementOwner) && placementOwner == player;
    }

    public int GetPerformerId()
    {
        return isSpeakerTaken && Utilities.IsValid(placementOwner) ? placementOwner.playerId : 0;
    }

    public int GetPlacementGeneration()
    {
        return placementGeneration;
    }

    public bool IsAvailableForPlacement()
    {
        return !isSpeakerTaken && !departedCleanupPending;
    }

    #endregion
}
