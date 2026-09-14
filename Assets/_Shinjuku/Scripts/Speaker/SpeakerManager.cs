
using UdonSharp;
using TMPro;
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

    // 매니저 소유자가 슬롯 배정을 직렬 처리. 배정 중인 슬롯도 빈자리로 재사용하지 않음
    [UdonSynced] private int[] allocatedPlayerIds = new int[0];
    [UdonSynced] private int[] allocationGenerations = new int[0];
    private bool placementPending;
    private int placementRequestId;
    private Vector3 pendingPlacementPosition;
    private Quaternion pendingPlacementRotation;
    private float nextPlacementRetryTime;

    private int UsableSpeakerCount = 0;
    private bool isVrUser;
    public bool speakerOwned = false;

    [Header("설치 트리거 시간 설정")]
    [SerializeField] private float requiredHoldTimeVR = 2f;
    [SerializeField] private float requiredHoldTimeDesktop = 1f;

    private float currentHoldTime = 0f;
    private bool isPlacingSpeaker = false;
    private bool isRightStickDown = false;
    private bool waitForPlacementRelease;

    [Header("레이케스트 설정")]
    [SerializeField] private float rayMaxDistance = 3f;
    [SerializeField] private int rayLayerMask = 0;

    [Header("홀로그램 오브젝트 설정")]
    [SerializeField] private GameObject speakerPlacements;
    [SerializeField] private GameObject holoSpeaker;
    [SerializeField] private Animator holoAnimator;
    [SerializeField] private LineRenderer lineRenderer;

    private bool isHoloDisabled = false;

    [Header("모델 전환 미리보기")]
    [Tooltip("위치 판정용 루트가 아닌 홀로그램의 외형 Transform")]
    [SerializeField] private Transform modelPreviewVisual;
    private const float ModelSwitchDuration = .32f;
    // 현재 제공 모델은 하나. 같은 모델도 전환 피드백은 끝까지 재생한다.
    private const int PreviewModelCount = 1;
    private int selectedPreviewModel;
    private bool modelSwitchActive;
    private bool modelSwitchMidpoint;
    private int modelSwitchDirection;
    private float modelSwitchElapsed;
    private bool modelVisualCached;
    private Vector3 modelVisualScale;
    private Quaternion modelVisualRotation;
    private bool modelStickArmed;

    [Header("로딩 고리 설정")]
    [SerializeField] private GameObject ringObject;
    [SerializeField] private Animator ringAnimator;

    [Header("안내 UI 설정")]
    [SerializeField] private GameObject messageUI;
    [SerializeField] private WorldLanguage worldLanguage;
    [SerializeField] private TextMeshProUGUI placementGuideText;
    [SerializeField] private TextMeshProUGUI placementStatusText;
    [SerializeField] private GameObject desktopPlacementHints;
    [SerializeField] private GameObject vrPlacementHints;
    [SerializeField] private RectTransform modelSelector;
    [SerializeField] private TextMeshProUGUI modelNameLabel;
    [SerializeField] private RectTransform modelNameRect;
    private bool modelNameCached;
    private Vector2 modelNamePosition;
    private Color modelNameColor;
    [SerializeField] private GameObject desktopModelKeys;
    [SerializeField] private GameObject vrModelKeys;
    [SerializeField] private TextMeshProUGUI[] placeActionLabels;
    [SerializeField] private TextMeshProUGUI[] cancelActionLabels;
    [SerializeField] private UnityEngine.UI.Image[] previousModelAccents;
    [SerializeField] private UnityEngine.UI.Image[] nextModelAccents;

    [Header("소리 범위 및 설치 간격")]
    [Tooltip("오디오 감쇠 곡선에서 구한 주요 영향 반경. 절대적인 청취 한계가 아님")]
    [SerializeField] private float audibleRange = 12f;
    [Tooltip("다른 스피커와 이 거리보다 가까우면 설치 불가")]
    [SerializeField] private float minimumSpeakerDistance = 8f;
    // Migration reference only: the obsolete blue range circle is never drawn.
    [SerializeField, HideInInspector] private LineRenderer audibleRangeRenderer;
    [Range(24, 128)] [SerializeField] private int audibleRangeSegments = 72;
    [SerializeField] private float[] speakerWarningRadii;
    [SerializeField] private LineRenderer[] placedSpeakerRings;
    [SerializeField] private LineRenderer[] placedSpeakerExclusionRings;
    [SerializeField] private LineRenderer[] placedSpeakerBeacons;
    [SerializeField] private MeshRenderer[] placedSpeakerHeatmaps;
    [SerializeField] private MeshRenderer[] placedSpeakerBadges;
    [SerializeField] private MeshRenderer[] placedSpeakerGroundMarks;
    [SerializeField] private Material quietGuideMaterial;
    [SerializeField] private Material focusedGuideMaterial;
    [SerializeField] private MeshRenderer placementEscapeArrow;
    [SerializeField] private bool distanceFadedRipples;
    [SerializeField] private float rippleApproachMargin = 3f;
    [SerializeField] private float rippleCullHysteresis = .5f;
    private bool[] rippleVisible;
    private Vector4[] rippleFadeStates;
    private MaterialPropertyBlock ripplePropertyBlock;
    private Vector3[] indicatedPositions;
    private bool[] indicatedActive;
    private float nextRangeUpdate;

    private const int PlacementReady = 0;
    private const int PlacementInvalidSurface = 1;
    private const int PlacementInstanceLimit = 2;
    private const int PlacementAlreadyOwned = 3;
    private const int PlacementTooClose = 4;
    private const int PlacementOverlapWarning = 5;
    private int placementState;
    private int languageIndex;

    #endregion

    private void Start()
    {
        isVrUser = Networking.LocalPlayer.IsUserInVR();
        speakerOwned = false;
        if (worldLanguage != null)
        {
            worldLanguage.Register(this);
            worldLanguage.GetLanguage();
            ApplyLanguage(worldLanguage.languageCode);
        }
        else
        {
            ApplyLanguage(VRCPlayerApi.GetCurrentLanguage());
        }
        ConfigureAudibleRangeRenderer();
        if (Networking.IsOwner(gameObject))
        {
            EnsureAllocationTable();
            RequestSerialization();
        }
        RecalculateUsableCount();
    }

    public override void OnLanguageChanged(string language)
    {
        if (worldLanguage == null) ApplyLanguage(language);
    }

    public void _OnWorldLanguageChanged()
    {
        if (worldLanguage != null) ApplyLanguage(worldLanguage.languageCode);
    }

    private void Update()
    {
        HandlePlacementInput();
    }

    public override void InputLookVertical(float value, UdonInputEventArgs args)
    {
        isRightStickDown = value < -0.9f;
        HandleModelStick(value);
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
            // 양쪽 버튼을 함께 눌렀을 때는 취소를 우선한다.
            if (isPlacingSpeaker && !Input.GetMouseButton(1) &&
                !Input.GetKeyDown(KeyCode.Q) && !Input.GetKeyDown(KeyCode.E))
            {
                ConfirmPlacement();
            }
        }
    }

    public override void InputDrop(bool value, UdonInputEventArgs args)
    {
        TryCancelDesktop(value);
    }

    private void HandlePlacementInput()
    {
        if (TryCancelDesktop(Input.GetMouseButtonDown(1))) return;
        HandleDesktopModelKeys(Input.GetKeyDown(KeyCode.Q), Input.GetKeyDown(KeyCode.E));
        bool isHolding = isVrUser ? isRightStickDown : Input.GetKey(KeyCode.G);
        // 취소할 때 G/스틱을 누르고 있었다면 먼저 놓아야 다시 시작할 수 있다.
        if (waitForPlacementRelease)
        {
            if (!isHolding) waitForPlacementRelease = false;
            return;
        }
        float requiredTime = isVrUser ? requiredHoldTimeVR : requiredHoldTimeDesktop;

        if (isHolding && !isPlacingSpeaker)
        {
            currentHoldTime += Time.deltaTime;
            ringObject.SetActive(true);

            if (currentHoldTime > requiredTime)
            {
                isPlacingSpeaker = true;
                ResetModelTransition();
                modelStickArmed = false; // 꺼내기에 사용한 스틱은 먼저 중앙으로 되돌린다.
                ConfigureAudibleRangeRenderer();
                nextRangeUpdate = 0f;
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
            AdvanceModelTransition(Time.deltaTime);
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
        if (isPlacingSpeaker) UpdateModelSelectorPose(head.position, head.rotation);
        ringAnimator.SetFloat("RingTime", Mathf.Clamp01(currentHoldTime / requiredTime));
    }

    private void UpdateModelSelectorPose(Vector3 headPosition, Quaternion headRotation)
    {
        if (modelSelector == null || holoSpeaker == null) return;
        Vector3 towardViewer = Vector3.ProjectOnPlane(headPosition - holoSpeaker.transform.position, Vector3.up).normalized;
        Vector3 alongSurface = Vector3.ProjectOnPlane(towardViewer, holoSpeaker.transform.up).normalized;
        modelSelector.position = holoSpeaker.transform.position + alongSurface * .48f + Vector3.up * .12f;
        Vector3 forward = modelSelector.position - headPosition;
        if (forward.sqrMagnitude > .0001f)
            modelSelector.rotation = Quaternion.LookRotation(forward, headRotation * Vector3.up);
    }

    private void SetUITransform(GameObject obj, Vector3 pos, Vector3 lookAt)
    {
        obj.transform.position = pos;
        obj.transform.LookAt(lookAt, Vector3.up);
    }

    private void ConfirmPlacement()
    {
        if (modelSwitchActive) return;
        // 확인 입력 직전에도 설치면과 가용 상태를 다시 검사
        UpdatePlacementPosition();
        if (isHoloDisabled || placementPending) return;

        isPlacingSpeaker = false;
        ResetModelTransition();
        speakerPlacements.SetActive(false);
        TryPlacingSpeaker();
    }

    private void CancelPlacement()
    {
        isPlacingSpeaker = false;
        ResetModelTransition();
        modelStickArmed = false;
        currentHoldTime = 0f;
        waitForPlacementRelease = true;
        speakerPlacements.SetActive(false);
        ringObject.SetActive(false);
        ringAnimator.SetFloat("RingTime", 0f);
    }

    private bool TryCancelDesktop(bool cancelPressed)
    {
        if (isVrUser || !cancelPressed || (!isPlacingSpeaker && currentHoldTime <= 0f)) return false;
        CancelPlacement();
        return true;
    }

    private void HandleDesktopModelKeys(bool previous, bool next)
    {
        if (isVrUser || previous == next) return;
        RequestModelSwitch(previous ? -1 : 1);
    }

    private void HandleModelStick(float value)
    {
        if (!isVrUser || !isPlacingSpeaker) { modelStickArmed = false; return; }
        if (Mathf.Abs(value) < .25f) { modelStickArmed = true; return; }
        if (!modelStickArmed || Mathf.Abs(value) < .75f) return;
        modelStickArmed = false; // 길게 기울이거나 중간 영역에서 흔들어도 한 번만 전환.
        RequestModelSwitch(value > 0f ? -1 : 1);
    }

    private void RequestModelSwitch(int direction)
    {
        if (!isPlacingSpeaker || placementPending || modelSwitchActive || modelPreviewVisual == null) return;
        if (!modelVisualCached)
        {
            modelVisualScale = modelPreviewVisual.localScale;
            modelVisualRotation = modelPreviewVisual.localRotation;
            modelVisualCached = true;
        }
        modelSwitchDirection = direction < 0 ? -1 : 1;
        modelSwitchElapsed = 0f;
        modelSwitchMidpoint = false;
        modelSwitchActive = true;
        if (!modelNameCached && modelNameLabel != null && modelNameRect != null)
        {
            modelNamePosition = modelNameRect.anchoredPosition;
            modelNameColor = modelNameLabel.color;
            modelNameCached = true;
        }
        SetModelHintFeedback(modelSwitchDirection);
    }

    private void AdvanceModelTransition(float deltaTime)
    {
        if (!modelSwitchActive || modelPreviewVisual == null) return;
        modelSwitchElapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(modelSwitchElapsed / ModelSwitchDuration);
        if (!modelSwitchMidpoint && t >= .5f)
        {
            selectedPreviewModel = (selectedPreviewModel + modelSwitchDirection + PreviewModelCount) % PreviewModelCount;
            modelSwitchMidpoint = true;
        }
        if (t >= 1f) { ResetModelTransition(); return; }
        float phase = t < .5f ? t * 2f : (1f - t) * 2f;
        float eased = phase * phase * (3f - 2f * phase);
        // 외형만 축소·복귀. 설치 좌표, 소리 범위, 판정, 재질은 그대로 유지한다.
        modelPreviewVisual.localScale = modelVisualScale * Mathf.Lerp(1f, .06f, eased);
        modelPreviewVisual.localRotation = modelVisualRotation * Quaternion.Euler(0f, modelSwitchDirection * 22f * eased, 0f);
        if (modelNameCached && modelNameLabel != null && modelNameRect != null)
        {
            // 다음 이름은 오른쪽에서, 이전 이름은 왼쪽에서 진입. 교체 순간에는 완전히 투명하다.
            float side = t < .5f ? -modelSwitchDirection : modelSwitchDirection;
            modelNameRect.anchoredPosition = modelNamePosition + Vector2.right * (side * 38f * eased);
            Color color = modelNameColor;
            color.a *= 1f - eased;
            modelNameLabel.color = color;
        }
    }

    private void ResetModelTransition()
    {
        if (modelVisualCached && modelPreviewVisual != null)
        {
            modelPreviewVisual.localScale = modelVisualScale;
            modelPreviewVisual.localRotation = modelVisualRotation;
        }
        modelSwitchActive = false;
        modelSwitchElapsed = 0f;
        modelSwitchMidpoint = false;
        if (modelNameCached && modelNameLabel != null && modelNameRect != null)
        {
            modelNameRect.anchoredPosition = modelNamePosition;
            modelNameLabel.color = modelNameColor;
        }
        SetModelHintFeedback(0);
    }

    private void SetModelHintFeedback(int direction)
    {
        Color normal = new Color(.90f, .88f, .81f, .85f);
        Color pressed = new Color(1f, .82f, .52f, 1f);
        if (previousModelAccents != null)
            for (int i = 0; i < previousModelAccents.Length; i++)
                if (previousModelAccents[i] != null) previousModelAccents[i].color = direction < 0 ? pressed : normal;
        if (nextModelAccents != null)
            for (int i = 0; i < nextModelAccents.Length; i++)
                if (nextModelAccents[i] != null) nextModelAccents[i].color = direction > 0 ? pressed : normal;
    }

    private void RefreshPlacementHints()
    {
        bool iconHints = desktopPlacementHints != null && vrPlacementHints != null;
        if (placementGuideText != null) placementGuideText.gameObject.SetActive(!iconHints);
        if (desktopPlacementHints != null) desktopPlacementHints.SetActive(!isVrUser);
        if (vrPlacementHints != null) vrPlacementHints.SetActive(isVrUser);
        if (desktopModelKeys != null) desktopModelKeys.SetActive(!isVrUser);
        if (vrModelKeys != null) vrModelKeys.SetActive(isVrUser);
        string place = languageIndex == 2 ? "설치" : languageIndex == 1 ? "設置" : languageIndex == 3 ? "放置" : "Place";
        string cancel = languageIndex == 2 ? "취소" : languageIndex == 1 ? "キャンセル" : languageIndex == 3 ? "取消" : "Cancel";
        if (placeActionLabels != null)
            for (int i = 0; i < placeActionLabels.Length; i++) if (placeActionLabels[i] != null) placeActionLabels[i].text = place;
        if (cancelActionLabels != null)
            for (int i = 0; i < cancelActionLabels.Length; i++) if (cancelActionLabels[i] != null) cancelActionLabels[i].text = cancel;
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
        UpdateAudibleRange(endPoint);
        // 두 Raycast가 모두 실패하면 영벡터를 설치면으로 취급하지 않음
        if (!hasSurface)
        {
            SetPlacementState(true, PlacementInvalidSurface);
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
            SetPlacementState(true, PlacementInvalidSurface);
        }
        else if (UsableSpeakerCount < 1)
        {
            SetPlacementState(true, PlacementInstanceLimit);
        }
        else if (speakerOwned || placementPending)
        {
            SetPlacementState(true, PlacementAlreadyOwned);
        }
        else
        {
            float closestDistance = GetClosestPlacedSpeakerDistanceXZ(holoSpeaker.transform.position);
            if (closestDistance < minimumSpeakerDistance)
                SetPlacementState(true, PlacementTooClose);
            else if (IsInsideSpeakerInfluence(holoSpeaker.transform.position, placementState == PlacementOverlapWarning ? .6f : 0f))
                SetPlacementState(false, PlacementOverlapWarning);
            else
                SetPlacementState(false, PlacementReady);
        }
    }

    private void SetPlacementState(bool disabled, int state)
    {
        placementState = state;
        isHoloDisabled = disabled;

        // 기존 홀로그램 색상 애니메이션은 유지하고, 새 상태는 가장 가까운 기존 색상으로 대응
        int animatorMessage = state == PlacementInstanceLimit ? 2 :
            state == PlacementAlreadyOwned ? 3 :
            disabled ? 1 : 0;
        holoAnimator.SetInteger("MessageStatus", animatorMessage);
        holoAnimator.SetBool("HoloDisabled", disabled);
        holoAnimator.SetBool("HoloWarning", state == PlacementOverlapWarning);

        HidePlacementStatus();
    }

    private void HidePlacementStatus()
    {
        // 설치 판정/홀로그램 색은 유지하고 화면에는 조작 안내만 표시한다.
        if (placementStatusText == null) return;
        placementStatusText.text = "";
        placementStatusText.gameObject.SetActive(false);
    }

    private void ConfigureAudibleRangeRenderer()
    {
        if (audibleRangeRenderer != null)
        {
            audibleRangeRenderer.enabled = false;
            audibleRangeRenderer.positionCount = 0;
        }
        indicatedPositions = new Vector3[speakerControllers.Length];
        indicatedActive = new bool[speakerControllers.Length];
        if (distanceFadedRipples)
        {
            rippleVisible = new bool[speakerControllers.Length];
            rippleFadeStates = new Vector4[speakerControllers.Length];
            if (ripplePropertyBlock == null) ripplePropertyBlock = new MaterialPropertyBlock();
            if (placedSpeakerHeatmaps != null)
                for (int i = 0; i < placedSpeakerHeatmaps.Length; i++)
                    if (placedSpeakerHeatmaps[i] != null) placedSpeakerHeatmaps[i].enabled = false;
        }
    }

    private void UpdateAudibleRange(Vector3 center)
    {
        if (Time.time < nextRangeUpdate) return;
        nextRangeUpdate = Time.time + .1f;
        if (indicatedPositions == null) ConfigureAudibleRangeRenderer();
        // Modern indicators must keep updating even after the legacy circle has been deleted.
        UpdatePlacedSpeakerIndicators();
    }

    private void DrawGroundRing(LineRenderer ring, Vector3 center, float radius)
    {
        if (ring == null) return;
        ring.useWorldSpace = true;
        int segments = Mathf.Max(24, audibleRangeSegments);
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle)*radius,0f,Mathf.Sin(angle)*radius);
            // Legacy fallback also stays flat; do not trace every vertex against obstacles.
            ring.SetPosition(i,point+Vector3.up*.08f);
        }
    }

    public float GetSpeakerWarningRadius(int index)
    {
        return speakerWarningRadii != null && index >= 0 && index < speakerWarningRadii.Length && speakerWarningRadii[index] > 0f
            ? speakerWarningRadii[index] : audibleRange;
    }

    public bool IsInsideSpeakerInfluence(Vector3 position, float margin)
    {
        for(int i=0;i<speakerControllers.Length;i++)
        {
            var speaker=speakerControllers[i];
            if(speaker==null || !speaker.isSpeakerTaken)continue;
            float radius=GetSpeakerWarningRadius(i)+margin;
            if((speaker.transform.position-position).sqrMagnitude < radius*radius)return true;
        }
        return false;
    }

    private void UpdatePlacedSpeakerIndicators()
    {
        if(placedSpeakerRings==null || placedSpeakerBeacons==null || placedSpeakerExclusionRings==null)return;
        if(indicatedPositions==null || (distanceFadedRipples && rippleVisible==null))ConfigureAudibleRangeRenderer();
        bool quietMode=quietGuideMaterial!=null && focusedGuideMaterial!=null;
        bool warning=placementState==PlacementOverlapWarning || placementState==PlacementTooClose;
        int focus=quietMode && warning ? GetGuideFocus(holoSpeaker.transform.position) : -1;
        for(int i=0;i<speakerControllers.Length && i<placedSpeakerRings.Length && i<placedSpeakerBeacons.Length && i<placedSpeakerExclusionRings.Length;i++)
        {
            var speaker=speakerControllers[i];
            bool show=speaker!=null && speaker.isSpeakerTaken;
            var ring=placedSpeakerRings[i];var exclusion=placedSpeakerExclusionRings[i];var beacon=placedSpeakerBeacons[i];
            if(ring==null || beacon==null || exclusion==null)continue;
            MeshRenderer heat = placedSpeakerHeatmaps != null && i < placedSpeakerHeatmaps.Length ? placedSpeakerHeatmaps[i] : null;
            ring.enabled=exclusion.enabled=show && heat==null;
            beacon.enabled=show && !quietMode;
            if(heat!=null)
            {
                if(distanceFadedRipples)UpdateRippleVisibility(i,heat,show,holoSpeaker.transform.position);
                else heat.enabled=show;
            }
            if(quietMode)
            {
                if(heat!=null)
                {
                    // Distance controls intensity continuously; do not snap between
                    // quiet/focused colors when a different speaker becomes nearest.
                    Material desired=distanceFadedRipples || i==focus ? focusedGuideMaterial : quietGuideMaterial;
                    if(heat.sharedMaterial!=desired)heat.sharedMaterial=desired;
                }
                if(placedSpeakerBadges!=null && i<placedSpeakerBadges.Length && placedSpeakerBadges[i]!=null)placedSpeakerBadges[i].enabled=show;
                if(placedSpeakerGroundMarks!=null && i<placedSpeakerGroundMarks.Length && placedSpeakerGroundMarks[i]!=null)placedSpeakerGroundMarks[i].enabled=show;
            }
            if(show && (!indicatedActive[i] || (speaker.transform.position-indicatedPositions[i]).sqrMagnitude>.01f))
            {
                Vector3 position=speaker.transform.position;
                if(heat!=null)
                {
                    heat.transform.position=position+Vector3.up*.06f;
                    heat.transform.rotation=Quaternion.identity;
                    float diameter=GetSpeakerWarningRadius(i)*2f;
                    Vector3 parentScale=heat.transform.parent.lossyScale;
                    heat.transform.localScale=new Vector3(diameter/parentScale.x,1f/parentScale.y,diameter/parentScale.z);
                }
                else
                {
                    DrawGroundRing(ring,position,GetSpeakerWarningRadius(i));
                    DrawGroundRing(exclusion,position,minimumSpeakerDistance);
                }
                beacon.SetPosition(0,position+Vector3.up*.15f);
                beacon.SetPosition(1,position+Vector3.up*5f);
                indicatedPositions[i]=position;
                if(quietMode)
                {
                    if(placedSpeakerBadges!=null && i<placedSpeakerBadges.Length && placedSpeakerBadges[i]!=null)
                        placedSpeakerBadges[i].transform.position=position+Vector3.up*1.25f;
                    if(placedSpeakerGroundMarks!=null && i<placedSpeakerGroundMarks.Length && placedSpeakerGroundMarks[i]!=null)
                        placedSpeakerGroundMarks[i].transform.position=position+Vector3.up*.065f;
                }
            }
            indicatedActive[i]=show;
        }
        if(placementEscapeArrow!=null)
        {
            Vector3 candidate=holoSpeaker.transform.position;
            Vector3 direction=focus>=0 ? GetGuideEscapeDirection(candidate) : Vector3.zero;
            placementEscapeArrow.enabled=direction.sqrMagnitude>.1f;
            if(placementEscapeArrow.enabled)
            {
                placementEscapeArrow.transform.position=candidate+Vector3.up*.075f;
                placementEscapeArrow.transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
            }
        }
    }

    private void UpdateRippleVisibility(int index, MeshRenderer renderer, bool occupied, Vector3 candidate)
    {
        Vector4 fade = rippleFadeStates[index];
        float now = Time.time;
        float current = Mathf.Lerp(fade.x, fade.y, Mathf.Clamp01((now-fade.z)/Mathf.Max(.001f,fade.w)));
        float target = 0f;
        if (occupied)
        {
            float distance = Vector3.Distance(candidate, speakerControllers[index].transform.position);
            float outer = Mathf.Max(minimumSpeakerDistance+.1f, GetSpeakerWarningRadius(index)+rippleApproachMargin);
            if (!rippleVisible[index] && distance < outer) rippleVisible[index] = true;
            else if (rippleVisible[index] && distance > outer+rippleCullHysteresis) rippleVisible[index] = false;
            if (rippleVisible[index])
            {
                float weight = Mathf.Clamp01((outer-distance)/(outer-minimumSpeakerDistance));
                target = weight*weight*(3f-2f*weight);
            }
        }
        else
        {
            rippleVisible[index] = false;
            current = 0f; // Returned speakers disappear immediately.
        }
        if (!occupied || (!rippleVisible[index] && current <= .001f))
        {
            renderer.enabled = false;
            rippleFadeStates[index] = Vector4.zero;
            return;
        }
        if (fade.y != target || !renderer.enabled)
        {
            fade = new Vector4(current, target, now, .2f);
            rippleFadeStates[index] = fade;
            // The shader completes interpolation itself. Do not resend an
            // unchanged target on every distance tick; initialize again on reentry.
            ripplePropertyBlock.Clear();
            ripplePropertyBlock.SetVector("_DistanceFade", fade);
            renderer.SetPropertyBlock(ripplePropertyBlock);
            renderer.enabled = true;
        }
    }

    public int GetGuideFocus(Vector3 candidate)
    {
        int result=-1;float closest=0.6f;
        for(int i=0;i<speakerControllers.Length;i++)
        {
            if(speakerControllers[i]==null || !speakerControllers[i].isSpeakerTaken)continue;
            float clearance=Vector3.Distance(candidate,speakerControllers[i].transform.position)-GetSpeakerWarningRadius(i);
            if(clearance<closest){closest=clearance;result=i;}
        }
        return result;
    }

    private float GuideClearance(Vector3 candidate)
    {
        float clearance=10000f;
        for(int i=0;i<speakerControllers.Length;i++)
        {
            if(speakerControllers[i]==null || !speakerControllers[i].isSpeakerTaken)continue;
            clearance=Mathf.Min(clearance,Vector3.Distance(candidate,speakerControllers[i].transform.position)-GetSpeakerWarningRadius(i));
        }
        return clearance;
    }

    public Vector3 GetGuideEscapeDirection(Vector3 candidate)
    {
        // Suggest increasing sound clearance, not a navigable route through obstacles.
        Vector3 result=Vector3.zero;float best=GuideClearance(candidate)+.05f;
        for(int i=0;i<16;i++)
        {
            float angle=i*Mathf.PI/8f;Vector3 direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            float score=GuideClearance(candidate+direction*2f);
            if(score>best){best=score;result=direction;}
        }
        return result;
    }

    private float GetClosestPlacedSpeakerDistanceXZ(Vector3 position)
    {
        float closest = float.PositiveInfinity;
        Vector2 candidate = new Vector2(position.x, position.z);
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            SpeakerController speaker = speakerControllers[i];
            if (speaker == null || !speaker.isSpeakerTaken) continue;
            Vector3 other = speaker.transform.position;
            float distance = Vector2.Distance(candidate, new Vector2(other.x, other.z));
            if (distance < closest) closest = distance;
        }
        return closest;
    }

    private bool IsTooCloseToPlacedSpeaker(Vector3 position)
    {
        return GetClosestPlacedSpeakerDistanceXZ(position) < minimumSpeakerDistance;
    }

    private void ApplyLanguage(string code)
    {
        code = string.IsNullOrEmpty(code) ? "en" : code.ToLowerInvariant();
        languageIndex = code == "ja" || code.StartsWith("ja-") ? 1 :
            code == "ko" || code.StartsWith("ko-") ? 2 :
            code == "zh" || code.StartsWith("zh-") ? 3 : 0;
        if (placementGuideText != null) placementGuideText.text = LocalizedGuide();
        RefreshPlacementHints();
        HidePlacementStatus();
    }

    private string LocalizedGuide()
    {
        if (isVrUser)
        {
            if (languageIndex == 2) return "오른손 사용: 설치 | 왼손 사용: 취소\n오른쪽 스틱 위/아래: 모델 변경";
            if (languageIndex == 1) return "右手の使用：設置 | 左手の使用：キャンセル\n右スティック上下：モデル切替";
            if (languageIndex == 3) return "右手使用：放置 | 左手使用：取消\n右摇杆上/下：切换模型";
            return "Right Use: Place | Left Use: Cancel\nRight Stick Up/Down: Model";
        }
        if (languageIndex == 2) return "좌클릭: 설치 | 우클릭: 취소\nQ/E: 모델 변경";
        if (languageIndex == 1) return "左クリック：設置 | 右クリック：キャンセル\nQ/E：モデル切替";
        if (languageIndex == 3) return "左键：放置 | 右键：取消\nQ/E：切换模型";
        return "Left Click: Place | Right Click: Cancel\nQ/E: Model";
    }

    private string LocalizedStatus(int state)
    {
        if (state == PlacementInvalidSurface)
        {
            if (languageIndex == 2) return "여기에는 설치할 수 없습니다";
            if (languageIndex == 1) return "ここには設置できません";
            if (languageIndex == 3) return "无法在此处放置音箱";
            return "You can't place a speaker here";
        }
        if (state == PlacementInstanceLimit)
        {
            if (languageIndex == 2) return "사용 가능한 스피커가 없습니다";
            if (languageIndex == 1) return "空きスピーカーがありません";
            if (languageIndex == 3) return "暂无空闲音箱";
            return "No speakers are available";
        }
        if (state == PlacementAlreadyOwned)
        {
            if (languageIndex == 2) return "이미 스피커를 설치했습니다";
            if (languageIndex == 1) return "スピーカーは設置済みです";
            if (languageIndex == 3) return "你已经放置了音箱";
            return "You already have a speaker placed";
        }
        if (state == PlacementTooClose)
        {
            if (languageIndex == 2) return "설치 불가 | 다른 스피커와 거리를 두세요";
            if (languageIndex == 1) return "設置できません | 他のスピーカーから離してください";
            if (languageIndex == 3) return "无法放置 | 请远离其他音箱";
            return "Can't place here | Please move farther from other speakers";
        }
        if (state == PlacementOverlapWarning)
        {
            if (languageIndex == 2) return "소리 겹침 주의 | 점선 밖에 설치해 주세요";
            if (languageIndex == 1) return "音の重なりに注意 | 破線の外に設置してください";
            if (languageIndex == 3) return "声音重叠提醒 | 请在虚线外放置";
            return "Sound overlap warning | Please place outside the dashes";
        }
        if (languageIndex == 2) return "설치할 수 있습니다";
        if (languageIndex == 1) return "設置できます";
        if (languageIndex == 3) return "可以在此处放置音箱";
        return "You can place a speaker here";
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
        placementRequestId++;
        Transform target = holoSpeaker.transform;
        pendingPlacementPosition = target.position;
        pendingPlacementRotation = target.rotation;
        nextPlacementRetryTime = 0f;
        _RetryPlacement();
    }

    // 소유권 전환 중 승인을 놓쳐도 같은 요청/위치로 재확인. 새 슬롯을 중복 예약하지 않음
    public void _RetryPlacement()
    {
        if (!placementPending || Time.time < nextPlacementRetryTime) return;
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestPlacement),
            placementRequestId, pendingPlacementPosition, pendingPlacementRotation);
        if (!placementPending) return; // 로컬 소유자의 즉시 응답이면 재시도 불필요
        nextPlacementRetryTime = Time.time + 3f;
        SendCustomEventDelayedSeconds(nameof(_RetryPlacement), 3f);
    }

    private void EnsureAllocationTable()
    {
        bool rebuildOwners = allocatedPlayerIds == null || allocatedPlayerIds.Length != speakerControllers.Length;
        if (rebuildOwners) allocatedPlayerIds = new int[speakerControllers.Length];
        if (allocationGenerations == null || allocationGenerations.Length != speakerControllers.Length)
            allocationGenerations = new int[speakerControllers.Length];
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            SpeakerController speaker = speakerControllers[i];
            // 소유권 인계 시에는 검증한 승인만 복원한다. 수신자가 주장한 번호는 사용하지 않는다.
            if (speaker._GetApprovedGeneration() > allocationGenerations[i] || rebuildOwners)
            {
                allocatedPlayerIds[i] = speaker._GetAllocatedPlayerId();
                allocationGenerations[i] = Mathf.Max(allocationGenerations[i], speaker._GetApprovedGeneration());
            }
        }
    }

    /// <summary>요청자의 빈 슬롯을 소유권자 한 명이 확정한 뒤 결과 전달</summary>
    [NetworkCallable]
    public void RequestPlacement(int requestId, Vector3 position, Quaternion rotation)
    {
        if (!Networking.IsOwner(gameObject)) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        // 두 사용자가 거의 동시에 설치해도 최소 간격을 우회하지 못하도록 승인 직전에 재검사
        if (IsTooCloseToPlacedSpeaker(position))
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, requestId, -2, position, rotation, 0);
            return;
        }
        EnsureAllocationTable();
        int slot = -1;
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            SpeakerController speaker = speakerControllers[i];
            // 소유권 인계와 실제 설치를 구분하여 다른 사용자의 설치 제한 방지
            if (speaker.IsPerformer(caller))
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, requestId, -1, position, rotation, 0);
                return;
            }
            if (!speaker.IsAvailableForPlacement()) continue;
            if (allocatedPlayerIds[i] == caller.playerId)
            {
                // 매니저 소유권 이전 중 응답을 놓친 요청은 기존 예약 슬롯으로 재응답
                slot = i;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, requestId, slot, position, rotation, allocationGenerations[slot]);
                return;
            }
            if (slot < 0 && allocatedPlayerIds[i] == 0 && allocationGenerations[i] < int.MaxValue) slot = i;
        }
        if (slot >= 0)
        {
            allocatedPlayerIds[slot] = caller.playerId;
            allocationGenerations[slot]++;
            RequestSerialization();
            RecalculateUsableCount();
        }
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ReceivePlacement), caller.playerId, requestId, slot, position, rotation, slot >= 0 ? allocationGenerations[slot] : 0);
    }

    /// <summary>매니저가 승인한 사용자만 해당 스피커 소유권을 확보하고 위치 전달</summary>
    [NetworkCallable]
    public void ReceivePlacement(int playerId, int requestId, int slot, Vector3 position, Quaternion rotation, int generation)
    {
        if (NetworkCalling.CallingPlayer != Networking.GetOwner(gameObject)) return;
        // 승인 사실은 모든 수신자가 기록하고, 실제 설치 입력은 요청자만 이어간다.
        VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
        if (slot >= 0 && slot < speakerControllers.Length && Utilities.IsValid(player))
            speakerControllers[slot]._ApplyAllocation(player, generation);
        // 이전 요청의 늦은 승인으로 새 요청 위치가 덮어써지지 않도록 요청 번호 확인
        if (Networking.LocalPlayer.playerId != playerId || !placementPending || requestId != placementRequestId) return;
        placementPending = false;
        if (slot == -2)
        {
            isPlacingSpeaker = true;
            ConfigureAudibleRangeRenderer();
            nextRangeUpdate = 0f;
            speakerPlacements.SetActive(true);
            UpdatePlacementPosition();
            return;
        }
        if (slot >= 0 && slot < speakerControllers.Length)
        {
            if (!speakerControllers[slot]._PlaceGrantedSpeaker(position, rotation, generation))
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReleaseAllocation), slot, generation);
        }
        RecalculateUsableCount();
    }

    public void _ReleaseSpeaker(SpeakerController speaker, int generation)
    {
        for (int i = 0; i < speakerControllers.Length; i++)
            if (speakerControllers[i] == speaker)
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReleaseAllocation), i, generation);
                return;
            }
    }

    [NetworkCallable]
    public void ReleaseAllocation(int slot, int generation)
    {
        if (!Networking.IsOwner(gameObject) || slot < 0 || slot >= speakerControllers.Length) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        EnsureAllocationTable();
        if (generation <= 0 || generation != allocationGenerations[slot]) return;
        int allocated = allocatedPlayerIds[slot];
        if (allocated == 0) return;
        SpeakerController speaker = speakerControllers[slot];
        if (allocated != caller.playerId && caller != Networking.GetOwner(speaker.gameObject)) return;
        if (speaker.isSpeakerTaken && !speaker.IsPerformer(caller)) return;
        allocatedPlayerIds[slot] = 0;
        ApplyAllocationTable();
        RequestSerialization();
        RecalculateUsableCount();
    }

    private void ApplyAllocationTable()
    {
        if (allocatedPlayerIds == null || allocationGenerations == null ||
            allocatedPlayerIds.Length != speakerControllers.Length || allocationGenerations.Length != speakerControllers.Length) return;
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            int playerId = allocatedPlayerIds[i];
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            // 참가자 정보가 아직 도착하지 않은 배정을 반환으로 오인하지 않는다.
            if (playerId != 0 && !Utilities.IsValid(player)) continue;
            speakerControllers[i]._ApplyAllocation(playerId == 0 ? null : player, allocationGenerations[i]);
        }
    }

    public override void OnDeserialization()
    {
        ApplyAllocationTable();
        RecalculateUsableCount();
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { ApplyAllocationTable(); }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // 대기 중 요청은 유지. 예약된 마지막 슬롯도 UI 재입력 없이 새 소유자에게 재확인
        if (Networking.IsOwner(gameObject))
        {
            EnsureAllocationTable();
            // 인계받은 오브젝트 소유자가 아닌 실제 설치자의 접속 상태로 배정표 복구
            for (int i = 0; i < speakerControllers.Length; i++)
            {
                if (speakerControllers[i].isSpeakerTaken &&
                    speakerControllers[i].GetPlacementGeneration() == allocationGenerations[i])
                    allocatedPlayerIds[i] = speakerControllers[i].GetPerformerId();
                else if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(allocatedPlayerIds[i])))
                    allocatedPlayerIds[i] = 0;
            }
            ApplyAllocationTable();
            RequestSerialization();
        }
        RecalculateUsableCount();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        EnsureAllocationTable();
        bool changed = false;
        for (int i = 0; i < allocatedPlayerIds.Length; i++)
        {
            if (allocatedPlayerIds[i] != player.playerId) continue;
            // 같은 숫자 ID의 새 설치가 확인된 슬롯은 이전 접속의 퇴장 처리에서 제외
            SpeakerController speaker = speakerControllers[i];
            if (speaker.GetPerformerId() == player.playerId && !speaker.IsPerformer(player)) continue;
            allocatedPlayerIds[i] = 0;
            changed = true;
        }
        // 소유권 이전 전에도 각 클라이언트의 퇴장자 예약 해제, 동기화는 현재 소유자만 수행
        if (changed)
        {
            ApplyAllocationTable();
            if (Networking.IsOwner(gameObject)) RequestSerialization();
            RecalculateUsableCount();
        }
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
            if (speaker.IsAvailableForPlacement() && !reserved) count++;
            if (speaker.IsLocalPerformer()) localOwned = true;
        }
        UsableSpeakerCount = count;
        speakerOwned = localOwned;
    }
}
