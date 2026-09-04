
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;  

/// <summary>
/// VR과 데스크톱 입력을 받아 스피커 설치 위치를 미리 보여주고 사용 가능한 스피커를 배치한다.
/// </summary>
/// <remarks>
/// 실제 스피커 상태와 소유권은 각 <see cref="SpeakerController"/>가 관리한다.
/// 이 클래스는 로컬 설치 입력과 전체 인스턴스의 사용 가능 수 표시를 조정한다.
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SpeakerManager : UdonSharpBehaviour
{
    #region Inspector

    [Header("스피커 오브젝트 설정")]
    [SerializeField] private SpeakerController[] speakerControllers;

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
        UsableSpeakerCount = speakerControllers.Length;
        RequestSerialization();
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
        if (isHoloDisabled) return;

        isPlacingSpeaker = false;
        speakerPlacements.SetActive(false);
        TryPlacingSpeaker();
    }

    private void CancelPlacement()
    {
        isPlacingSpeaker = false;
        speakerPlacements.SetActive(false);
    }

    private void UpdatePlacementPosition()
    {
        var trackingType = isVrUser ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.Head;
        var tracking = Networking.LocalPlayer.GetTrackingData(trackingType);
        Vector3 origin = tracking.position;
        Quaternion rotation = tracking.rotation;

        if (isVrUser)
        {
            rotation *= Quaternion.AngleAxis(40f, Vector3.up);
        }
        else
        {
            origin.y -= 0.1f;
            origin += rotation * Vector3.right * 0.1f;
            rotation *= Quaternion.AngleAxis(3f, Vector3.down);
        }

        Vector3 direction = rotation * Vector3.forward;
        Vector3 endPoint = origin + direction * rayMaxDistance;

        RaycastHit hit;
        bool didHit = Physics.Raycast(origin, direction, out hit, rayMaxDistance, rayLayerMask);
        if (!didHit && Physics.Raycast(endPoint, Vector3.down, out hit, Mathf.Infinity, rayLayerMask))
        {
            endPoint = hit.point;
        }
        else if (didHit)
        {
            endPoint = hit.point;
        }

        holoSpeaker.transform.position = endPoint;
        SetHoloRotation(origin, endPoint, hit.normal);

        ValidatePlacement(hit.normal);
        SetLineRenderer(didHit, origin, direction, endPoint);
    }

    private void SetHoloRotation(Vector3 from, Vector3 to, Vector3 normal)
    {
        var direction = (from - to).normalized;
        var rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, normal), normal);
        holoSpeaker.transform.rotation = rotation;
    }

    private void ValidatePlacement(Vector3 surfaceNormal)
    {
        float angle = Vector3.Angle(surfaceNormal, Vector3.up);
        if (angle > 30f)
        {
            SetHoloStatus(true, 1); // Animator 상태 1은 경사면 경고이다.
        }
        else if (UsableSpeakerCount < 1)
        {
            SetHoloStatus(true, 2); // Animator 상태 2는 전체 수량 초과 경고이다.
        }
        else if (speakerOwned)
        {
            SetHoloStatus(true, 3); // Animator 상태 3은 개인 수량 초과 경고이다.
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
                            t * t * end; // 반복 호출 비용을 줄이기 위해 Mathf.Pow를 사용하지 않는다.
            lineRenderer.SetPosition(i, point);
        }
    }

    private void TryPlacingSpeaker()
    {
        foreach (var speaker in speakerControllers)
        {
            if (!speaker.isSpeakerTaken)
            {
                Networking.SetOwner(Networking.LocalPlayer, speaker.gameObject);

                Transform targetTransform = holoSpeaker.transform;
                speaker.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(speaker.PlaceSpeaker), 0, targetTransform.position, targetTransform.rotation);
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(DecreaseUsableCount));
                speakerOwned = true;
                Debug.Log("[SpeakerManager] speakerController found and placed");

                return;
            }
        }

        Debug.Log("[SpeakerManager] No speakerController found");
    }

    /// <summary>
    /// 스피커 배치가 확정된 모든 클라이언트에서 사용 가능 수를 하나 줄인다.
    /// </summary>
    [NetworkCallable]
    public void DecreaseUsableCount()
    {
        if (UsableSpeakerCount > 0)
        {
            UsableSpeakerCount--;
            Debug.Log("[SpeakerManager] UsableSpeakerCount Decreased");
        }
        else Debug.LogError("[SpeakerManager] UsableSpeakerCount Cannot Be Decreased");
    }

    /// <summary>
    /// 현재 비어 있는 스피커 슬롯을 다시 세어 로컬 사용 가능 수를 복구한다.
    /// </summary>
    public void RecalculateUsableCount()
    {
        int count = 0;
        foreach (var speaker in speakerControllers)
        {
            if (!speaker.isSpeakerTaken)
            {
                count++;
            }
        }
        UsableSpeakerCount = count;
        Debug.Log("[SpeakerManager] UsableSpeakerCount recalculated: " + count);
    }
}
