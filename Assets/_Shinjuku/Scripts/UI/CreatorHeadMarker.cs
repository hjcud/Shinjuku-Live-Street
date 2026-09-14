using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 지정한 표시 이름의 플레이어 머리 위에 피드백 안내 표시
/// </summary>
/// <remarks>
/// 소유권 이전이나 네트워크 동기화 없이 각 방문자 시점에서 위치와 방향 갱신
/// 표시 이름 비교는 안내 전용이며 관리자 권한 확인 용도로 사용 금지
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CreatorHeadMarker : UdonSharpBehaviour
{
    [Tooltip("안내 대상의 VRChat 표시 이름, 대소문자를 포함한 정확한 이름 비교")]
    public string targetDisplayName = "Cuding";

    [Tooltip("안내 표시 전용 자식 Transform 연결, 스크립트가 있는 부모 연결 금지")]
    public Transform markerRoot;

    [Tooltip("안내 대상 본인의 시야에서 표시 숨김")]
    public bool hideForTarget = true;

    [Tooltip("머리와 안내 하단 사이의 기본 간격")]
    public float headClearance = 0.55f;

    [Tooltip("안내 영역의 UI 높이, 거리별 확대 시 머리와의 겹침 방지")]
    public float markerHeight = 220f;

    [Tooltip("가까운 거리의 UI 기본 배율")]
    public float baseScale = 0.0018f;

    [Tooltip("거리에 따른 안내 확대 시작 거리")]
    public float enlargementDistance = 12f;

    [Tooltip("먼 거리에서의 과도한 확대 방지")]
    public float maximumEnlargement = 2f;

    private VRCPlayerApi targetPlayer;
    private VRCPlayerApi[] players = new VRCPlayerApi[100];
    private float nextSearchTime;

    private void Start()
    {
        SetVisible(false);
        FindTarget();
    }

    private bool Matches(VRCPlayerApi player)
    {
        return Utilities.IsValid(player) && player.displayName == targetDisplayName;
    }

    private void FindTarget()
    {
        nextSearchTime = Time.time + 5f;
        targetPlayer = null;
        int count = VRCPlayerApi.GetPlayerCount();
        if (players.Length < count) players = new VRCPlayerApi[count];
        VRCPlayerApi.GetPlayers(players);
        for (int i = 0; i < count; i++)
        {
            if (!Matches(players[i])) continue;
            targetPlayer = players[i];
            break;
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (Matches(player)) targetPlayer = player;
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player != targetPlayer) return;
        targetPlayer = null;
        SetVisible(false);
        nextSearchTime = Time.time + 0.5f;
    }

    public override void PostLateUpdate()
    {
        if (markerRoot == null || markerRoot == transform) return;
        var viewer = Networking.LocalPlayer;
        if (!Utilities.IsValid(targetPlayer))
        {
            SetVisible(false);
            // 대상 부재 시에만 낮은 빈도로 재검색
            if (Time.time >= nextSearchTime) FindTarget();
            return;
        }

        if (!Utilities.IsValid(viewer) || (hideForTarget && targetPlayer.isLocal))
        {
            SetVisible(false);
            return;
        }

        // IK 갱신 이후의 머리 위치 사용으로 한 프레임 늦은 추적 방지
        Vector3 head = targetPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 viewerHead = viewer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        float distance = Vector3.Distance(head, viewerHead);
        float enlargement = Mathf.Clamp(distance / Mathf.Max(1f, enlargementDistance),
            1f, Mathf.Max(1f, maximumEnlargement));
        float scale = Mathf.Max(0.0001f, baseScale) * enlargement;
        markerRoot.localScale = Vector3.one * scale;
        markerRoot.position = head + Vector3.up * (headClearance + markerHeight * scale * 0.5f);

        // UI 정면을 각 방문자 시점으로 정렬, 극단적인 수직 시점에서 회전 불안정 방지
        Vector3 direction = markerRoot.position - viewerHead;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.99f
                ? viewer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.up
                : Vector3.up;
            markerRoot.rotation = Quaternion.LookRotation(direction, up);
        }
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (markerRoot != null && markerRoot != transform && markerRoot.gameObject.activeSelf != visible)
            markerRoot.gameObject.SetActive(visible);
    }

    private void OnDisable()
    {
        SetVisible(false);
    }
}
