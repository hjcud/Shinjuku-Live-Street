
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;  

/// <summary>
/// VR과 데스크톱 입력에 따른 스피커 설치 위치 미리 표시 및 사용 가능한 스피커 배치
/// </summary>
/// <remarks>
/// 실제 스피커 상태와 소유권은 각 <see cref="SpeakerController"/>에서 관리
/// 이 클래스에서는 로컬 설치 입력과 전체 인스턴스의 사용 가능 수 표시 조정
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SpeakerManager : UdonSharpBehaviour
{
    #region Inspector

    [Header("스피커 오브젝트 설정")]
    [SerializeField] private SpeakerController[] speakerControllers;

    // Cuding Edit: 매니저 소유자가 슬롯 배정을 직렬 처리. 배정 중인 슬롯도 빈자리로 재사용하지 않음
    [UdonSynced] private int[] allocatedPlayerIds = new int[0];
    private bool placementPending;

    private int UsableSpeakerCount = 0;
    private bool isVrUser;
    public bool speakerOwned = false;

    [Header("설치 트리거 시간 설정")]
    [SerializeField] private float requiredHoldTimeVR = 2f;
    [SerializeField] private float requiredHoldTimeDesktop = 1f;

    private float currentHoldTime = 0f;
    private bool isPlacingSpeaker = false;
    private bool isRightStickDown = false;

    [Header("레이케스트 설정")]
    [SerializeField] private float rayMaxDistance = 3f;
    [SerializeField] private int rayLayerMask = 0;

    [Header("홀로그램 오브젝트 설정")]
    [SerializeField] private GameObject speakerPlacements;
    [SerializeField] private GameObject holoSpeaker;
    [SerializeField] private Animator holoAnimator;
    [SerializeField] private LineRenderer lineRenderer;

    private bool isHoloDisabled = false;

    [Header("로딩 고리 설정")]
    [SerializeField] private GameObject ringObject;
    [SerializeField] private Animator ringAnimator;

    [Header("안내 UI 설정")]
    [SerializeField] private GameObject messageUI;

    #endregion

    private void Start()
    {
        isVrUser = Networking.LocalPlayer.IsUserInVR();
        speakerOwned = false;
        if (Networking.IsOwner(gameObject))
        {
            EnsureAllocationTable();
            RequestSerialization();
        }
        RecalculateUsableCount();
    }

    private void Update()
    {
        HandlePlacementInput();
    }

    public override void InputLookVertical(float value, UdonInputEventArgs args)
    {
        isRightStickDown = value < -0.9f;
    }

    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!value) return;

        if (isVrUser)
        {
            if (args.handType == HandType.LEFT && isPlacingSpeaker)
            {
                CancelPlacement();
            }
            else if (args.handType == HandType.RIGHT && isPlacingSpeaker)
            {
                ConfirmPlacement();
            }
        }
        else
        {
            if (isPlacingSpeaker)
            {
                ConfirmPlacement();
            }
        }
    }

    public override void InputDrop(bool value, UdonInputEventArgs args)
    {
        if (!value || isVrUser) return;

        if (isPlacingSpeaker)
        {
            CancelPlacement();
        }
    }

    private void HandlePlacementInput()
    {
        bool isHolding = isVrUser ? isRightStickDown : Input.GetKey(KeyCode.G);
        float requiredTime = isVrUser ? requiredHoldTimeVR : requiredHoldTimeDesktop;

        if (isHolding && !isPlacingSpeaker)
        {
            currentHoldTime += Time.deltaTime;
            ringObject.SetActive(true);

            if (currentHoldTime > requiredTime)
            {
                isPlacingSpeaker = true;
                speakerPlacements.SetActive(true);
            }

            UpdateUITransform(requiredTime);
        }
        else if (!isHolding && !isPlacingSpeaker)
        {
            currentHoldTime = 0f;
            ringObject.SetActive(false);
        }

        if (isPlacingSpeaker)
        {
            UpdatePlacementPosition();
            UpdateUITransform(requiredTime);
        }
    }

    private void UpdateUITransform(float requiredTime)
    {
        var head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        var targetPos = head.position + (head.rotation * Vector3.forward);

        Vector3 lookAt = head.position;
        SetUITransform(messageUI, targetPos, lookAt);
        SetUITransform(ringObject, targetPos, lookAt);
        ringAnimator.SetFloat("RingTime", Mathf.Clamp01(currentHoldTime / requiredTime));
    }

    private void SetUITransform(GameObject obj, Vector3 pos, Vector3 lookAt)
    {
        obj.transform.position = pos;
        obj.transform.LookAt(lookAt, Vector3.up);
    }

    private void ConfirmPlacement()
    {
        // 확인 입력 직전에도 설치면과 가용 상태를 다시 검사
        UpdatePlacementPosition();
        if (isHoloDisabled || placementPending) return;

        isPlacingSpeaker = false;
        speakerPlacements.SetActive(false);
        TryPlacingSpeaker();
    }

    private void CancelPlacement()
    {
        isPlacingSpeaker = false;
        speakerPlacements.SetActive(false);
    }

    /// <summary>
    /// 손 또는 시선 기준으로 설치 후보 위치를 탐색하고 홀로그램과 안내선 갱신
    /// </summary>
    private void UpdatePlacementPosition()
    {
        var trackingType = isVrUser ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.Head;
        var tracking = Networking.LocalPlayer.GetTrackingData(trackingType);
        Vector3 origin = tracking.position;
        Quaternion rotation = tracking.rotation;

        if (isVrUser)
        {
            // 오른손 추적 회전에 로컬 Y축 40도 보정을 적용해 설치 레이 방향 설정
            rotation *= Quaternion.AngleAxis(40f, Vector3.up);
        }
        else
        {
            // 시선 기준 아래쪽과 오른쪽으로 시작점 이동 후 로컬 Y축 -3도 방향 보정
            origin.y -= 0.1f;
            origin += rotation * Vector3.right * 0.1f;
            rotation *= Quaternion.AngleAxis(3f, Vector3.down);
        }

        Vector3 direction = rotation * Vector3.forward;
        Vector3 endPoint = origin + direction * rayMaxDistance;

        RaycastHit hit;
        bool didHit = Physics.Raycast(origin, direction, out hit, rayMaxDistance, rayLayerMask);
        bool hasSurface = didHit;
        // 전방에 닿는 면이 없으면 최대 거리 지점 아래의 설치면 탐색
        // 직선과 곡선 안내선 구분을 위해 didHit은 전방 검사 결과로 유지
        if (!didHit && Physics.Raycast(endPoint, Vector3.down, out hit, Mathf.Infinity, rayLayerMask))
        {
            hasSurface = true;
            endPoint = hit.point;
        }
        else if (didHit)
        {
            endPoint = hit.point;
        }

        holoSpeaker.transform.position = endPoint;
        // Cuding Edit: 두 Raycast가 모두 실패하면 영벡터를 설치면으로 취급하지 않음
        if (!hasSurface)
        {
            SetHoloStatus(true, 1);
            SetLineRenderer(didHit, origin, direction, endPoint);
            return;
        }
        SetHoloRotation(origin, endPoint, hit.normal);

        ValidatePlacement(hit.normal);
        SetLineRenderer(didHit, origin, direction, endPoint);
    }

    private void SetHoloRotation(Vector3 from, Vector3 to, Vector3 normal)
    {
        // 사용자 쪽 방향을 설치면에 투영해 바닥 기울기에 맞춘 홀로그램 회전 적용
        var direction = (from - to).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(direction, normal);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.right, normal);
        var rotation = Quaternion.LookRotation(forward, normal);
        holoSpeaker.transform.rotation = rotation;
    }

    private void ValidatePlacement(Vector3 surfaceNormal)
    {
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        if (angle > 30f)
        {
            SetHoloStatus(true, 1); // Animator 상태 1: 경사면 경고 표시
        }
        else if (UsableSpeakerCount < 1)
        {
            SetHoloStatus(true, 2); // Animator 상태 2: 전체 수량 초과 경고 표시
        }
        else if (speakerOwned || placementPending)
        {
            SetHoloStatus(true, 3); // Animator 상태 3: 개인 수량 초과 경고 표시
        }
        else
        {
            SetHoloStatus(false, 0);
        }
    }

    private void SetHoloStatus(bool disabled, int messageStatus)
    {
        isHoloDisabled = disabled;
        holoAnimator.SetInteger("MessageStatus", messageStatus);
        holoAnimator.SetBool("HoloDisabled", disabled);
    }

    private void SetLineRenderer(bool didHit, Vector3 origin, Vector3 direction, Vector3 end)
    {
        if (didHit)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, origin);
            lineRenderer.SetPosition(1, end);
        }
        else
        {
            DrawCurve(origin, origin + direction * rayMaxDistance, end);
        }
    }

    private void DrawCurve(Vector3 start, Vector3 control, Vector3 end)
    {
        int steps = 15;
        lineRenderer.positionCount = steps + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 point = (1 - t) * (1 - t) * start +
                            2 * (1 - t) * t * control +
                            t * t * end; // 반복 호출 비용 절감을 위해 Mathf.Pow 대신 곱셈 사용
            lineRenderer.SetPosition(i, point);
        }
    }

    private void TryPlacingSpeaker()
    {
        if (placementPending || speakerOwned) return;
        placementPending = true;
        Transform target = holoSpeaker.transform;
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestPlacement), target.position, target.rotation);
    }

    private void EnsureAllocationTable()
    {
        if (allocatedPlayerIds != null && allocatedPlayerIds.Length == speakerControllers.Length) return;
        allocatedPlayerIds = new int[speakerControllers.Length];
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            if (speakerControllers[i].isSpeakerTaken)
                allocatedPlayerIds[i] = Networking.GetOwner(speakerControllers[i].gameObject).playerId;
        }
    }

    /// <summary>요청자의 빈 슬롯을 소유권자 한 명이 확정한 뒤 결과 전달</summary>
    [NetworkCallable]
    public void RequestPlacement(Vector3 position, Quaternion rotation)
    {
        if (!Networking.IsOwner(gameObject)) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        EnsureAllocationTable();
        int slot = -1;
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            if (allocatedPlayerIds[i] == caller.playerId)
            {
                // 매니저 소유권 이전 중 응답을 놓친 요청은 기존 예약 슬롯으로 재응답
                slot = speakerControllers[i].isSpeakerTaken ? -1 : i;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, slot, position, rotation);
                return;
            }
            if (speakerControllers[i].isSpeakerTaken && Networking.GetOwner(speakerControllers[i].gameObject) == caller)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, -1, position, rotation);
                return;
            }
            if (slot < 0 && allocatedPlayerIds[i] == 0 && !speakerControllers[i].isSpeakerTaken) slot = i;
        }
        if (slot >= 0)
        {
            allocatedPlayerIds[slot] = caller.playerId;
            RequestSerialization();
            RecalculateUsableCount();
        }
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, slot, position, rotation);
    }

    /// <summary>매니저가 승인한 사용자만 해당 스피커 소유권을 확보하고 위치 전달</summary>
    [NetworkCallable]
    public void ReceivePlacement(int playerId, int slot, Vector3 position, Quaternion rotation)
    {
        if (NetworkCalling.CallingPlayer != Networking.GetOwner(gameObject)) return;
        if (Networking.LocalPlayer.playerId != playerId || !placementPending) return;
        placementPending = false;
        if (slot >= 0 && slot < speakerControllers.Length)
        {
            if (!speakerControllers[slot]._PlaceGrantedSpeaker(position, rotation))
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReleaseAllocation), slot);
        }
        RecalculateUsableCount();
    }

    public void _ReleaseSpeaker(SpeakerController speaker)
    {
        for (int i = 0; i < speakerControllers.Length; i++)
            if (speakerControllers[i] == speaker)
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReleaseAllocation), i);
                return;
            }
    }

    [NetworkCallable]
    public void ReleaseAllocation(int slot)
    {
        if (!Networking.IsOwner(gameObject) || slot < 0 || slot >= speakerControllers.Length) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        EnsureAllocationTable();
        int allocated = allocatedPlayerIds[slot];
        if (allocated == 0) return;
        if (allocated != caller.playerId &&
            (Utilities.IsValid(VRCPlayerApi.GetPlayerById(allocated)) || caller != Networking.GetOwner(speakerControllers[slot].gameObject))) return;
        allocatedPlayerIds[slot] = 0;
        RequestSerialization();
        RecalculateUsableCount();
    }

    public override void OnDeserialization() { RecalculateUsableCount(); }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        placementPending = false;
        if (Networking.IsOwner(gameObject))
        {
            EnsureAllocationTable();
            RequestSerialization();
        }
        RecalculateUsableCount();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject)) return;
        EnsureAllocationTable();
        bool changed = false;
        for (int i = 0; i < allocatedPlayerIds.Length; i++)
            if (allocatedPlayerIds[i] == player.playerId) { allocatedPlayerIds[i] = 0; changed = true; }
        if (changed) { RequestSerialization(); RecalculateUsableCount(); }
    }

    /// <summary>
    /// 현재 비어 있는 스피커 슬롯을 다시 세어 로컬 사용 가능 수 복구
    /// </summary>
    public void RecalculateUsableCount()
    {
        int count = 0;
        bool localOwned = false;
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            SpeakerController speaker = speakerControllers[i];
            bool reserved = allocatedPlayerIds != null && i < allocatedPlayerIds.Length && allocatedPlayerIds[i] != 0;
            if (!speaker.isSpeakerTaken && !reserved) count++;
            if (speaker.IsLocalPerformer()) localOwned = true;
        }
        UsableSpeakerCount = count;
        speakerOwned = localOwned;
    }
}
