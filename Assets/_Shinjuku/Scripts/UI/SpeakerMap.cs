using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// 입구 지도에 설치된 스피커 위치와 공연자 이름 표시
/// </summary>
/// <remarks>
/// 스피커의 기존 동기화 결과를 사용하며 지도 자체는 네트워크 동기화하지 않음
/// 시작 시 표시를 준비하고 스피커 상태 변경 및 지도 재활성화 시에만 갱신
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SpeakerMap : UdonSharpBehaviour
{
    #region Inspector

    [Header("지도 UI 설정")]
    [SerializeField] private RectTransform mapRect;
    [Tooltip("스피커 아이콘과 이름 TMP를 자식으로 가진 비활성 UI 원본")]
    [SerializeField] private RectTransform markerTemplate;

    [Header("지도 좌표 설정")]
    [Tooltip("지도 왼쪽 아래에 해당하는 월드 위치. 로컬 X축은 지도 오른쪽, Z축은 위쪽")]
    [SerializeField] private Transform mapOrigin;
    [Tooltip("지도 전체에 해당하는 월드 가로/세로 길이. 원점 오브젝트의 Scale은 1로 설정")]
    [SerializeField] private Vector2 worldSize = new Vector2(100f, 100f);
    [Tooltip("원본 지도에서 실제 표시하는 UV 영역 (x, y, width, height). 월드 원점/범위는 원본 기준 유지")]
    public Vector4 mapViewport = new Vector4(0f, 0f, 1f, 1f);

    [Header("표시할 스피커")]
    [SerializeField] private SpeakerController[] speakerControllers;

    [Header("전체 스피커 사용 현황 (로컬 설정과 독립)")]
    [SerializeField] private TextMeshProUGUI usageSummary;
    public TextMeshProUGUI usageTitle;
    [SerializeField] private Image usageDot;
    public WorldLanguage worldLanguage;
    public SpeakerMapNameLayout nameLayout;

    #endregion

    private RectTransform[] markers;
    private TextMeshProUGUI[] playerNames;

    private void Start()
    {
        if (worldLanguage != null) worldLanguage.Register(this);
        if (mapRect == null || markerTemplate == null || mapOrigin == null ||
            speakerControllers == null || worldSize.x <= 0f || worldSize.y <= 0f)
        {
            Debug.LogError("[SpeakerMap] 지도 UI, 좌표 및 스피커 연결을 확인하세요.");
            return;
        }
        if (markerTemplate == mapRect || markerTemplate.gameObject == gameObject ||
            markerTemplate.GetComponentInChildren<TextMeshProUGUI>(true) == null)
        {
            Debug.LogError("[SpeakerMap] 별도의 표시 원본과 자식 이름 TMP가 필요합니다.");
            return;
        }

        // 스피커 수만큼 한 번 생성한 뒤 설치와 반환 때 같은 표시 재사용
        markerTemplate.gameObject.SetActive(false);
        markers = new RectTransform[speakerControllers.Length];
        playerNames = new TextMeshProUGUI[speakerControllers.Length];
        for (int i = 0; i < speakerControllers.Length; i++)
        {
            GameObject marker = Instantiate(markerTemplate.gameObject);
            marker.transform.SetParent(mapRect, false);
            markers[i] = marker.GetComponent<RectTransform>();
            playerNames[i] = marker.GetComponentInChildren<TextMeshProUGUI>(true);
            // Rich Text와 Raycast Target은 표시 원본에서 미리 끔 (Udon에서 변경 불가)
            if (speakerControllers[i] != null) speakerControllers[i]._RegisterMap(this);
        }
        _RefreshMap();
    }

    private void OnEnable()
    {
        _RefreshMap();
    }

    public void _OnWorldLanguageChanged() { _RefreshMap(); }

    /// <summary>원본 월드 좌표를 잘라낸 지도 영역의 앵커로 변환한다. 범위 밖 값은 고정하지 않는다.</summary>
    public Vector2 WorldToMapAnchor(Vector3 worldPosition)
    {
        if (mapOrigin == null || worldSize.x <= 0f || worldSize.y <= 0f || mapViewport.z <= 0f || mapViewport.w <= 0f)
            return new Vector2(-1f, -1f);
        Vector3 position = mapOrigin.InverseTransformPoint(worldPosition);
        return new Vector2((position.x / worldSize.x - mapViewport.x) / mapViewport.z,
            (position.z / worldSize.y - mapViewport.y) / mapViewport.w);
    }

    public string FormatUsage(int used, int total)
    {
        string count = used + " / " + total;
        if (usageTitle != null)
            return worldLanguage == null ? count + " in use"
                : worldLanguage.SelectText(count + " in use", count + " 使用中", count + " 사용 중");
        return worldLanguage == null ? "Speakers " + count + " in use"
            : worldLanguage.SelectText("Speakers " + count + " in use", "スピーカー " + count + " 使用中", "스피커 " + count + " 사용 중");
    }

    /// <summary>
    /// 현재 배치 상태에서 스피커 위치와 이름을 다시 읽어 지도 표시 갱신
    /// </summary>
    /// <remarks>네트워크 호출 불가. 스피커의 로컬 상태 적용이 끝난 뒤 호출</remarks>
    public void _RefreshMap()
    {
        if (usageTitle != null) usageTitle.text = worldLanguage == null ? "Speakers"
            : worldLanguage.SelectText("Speakers", "スピーカー", "스피커");
        // 기존 배치/반환 알림으로만 갱신. 지도 밖에 설치된 스피커도 합산한다.
        int total = 0;
        int used = 0;
        if (speakerControllers != null)
        {
            for (int i = 0; i < speakerControllers.Length; i++)
            {
                if (speakerControllers[i] == null) continue;
                total++;
                if (speakerControllers[i].isSpeakerTaken) used++;
            }
        }
        if (usageSummary != null)
        {
            string summary = FormatUsage(used, total);
            if (usageSummary.text != summary) usageSummary.text = summary;
        }
        if (usageDot != null) usageDot.color = used > 0
            ? new Color(.12f, .66f, .39f, 1f) : new Color(.65f, .70f, .72f, 1f);
        if (markers == null || mapOrigin == null || worldSize.x <= 0f || worldSize.y <= 0f) return;

        for (int i = 0; i < markers.Length; i++)
        {
            SpeakerController speaker = speakerControllers[i];
            VRCPlayerApi performer = speaker == null ? null : VRCPlayerApi.GetPlayerById(speaker.GetPerformerId());
            bool visible = speaker != null && speaker.isSpeakerTaken && Utilities.IsValid(performer);
            playerNames[i].text = visible ? performer.displayName : "";

            if (visible)
            {
                // 원점 기준 XZ 위치를 0~1 앵커로 변환해 UI 크기와 Pivot에 독립적으로 배치
                Vector2 anchor = WorldToMapAnchor(speaker.transform.position);
                visible = anchor.x >= 0f && anchor.x <= 1f && anchor.y >= 0f && anchor.y <= 1f;
                markers[i].anchorMin = anchor;
                markers[i].anchorMax = anchor;
                markers[i].anchoredPosition3D = Vector3.zero;
                if (visible && nameLayout == null) LayoutPerformerName(markers[i], playerNames[i], anchor);
            }

            // 범위 밖 스피커를 지도 가장자리의 잘못된 위치로 표시하지 않음
            markers[i].gameObject.SetActive(visible);
        }
        if (nameLayout != null) nameLayout.Layout(markers, playerNames);
    }

    /// <summary>아이콘의 실제 위치는 유지하고 이름 영역만 지도 안으로 보정한다.</summary>
    public void LayoutPerformerName(RectTransform marker, TextMeshProUGUI label, Vector2 anchor)
    {
        if (mapRect == null || marker == null || label == null) return;
        // 고정 이름 영역은 유지하되 가장자리에서는 글자를 아이콘 쪽 끝에 정렬한다.
        // Udon에서 지원하지 않는 preferredWidth 조회 없이 짧은 이름의 과도한 밀림 방지.
        const float padding = 8f;
        float mapWidth = mapRect.rect.width;
        float mapHeight = mapRect.rect.height;
        float width = Mathf.Min(280f, Mathf.Max(1f, mapWidth - padding * 2f));
        float height = Mathf.Min(50f, Mathf.Max(1f, mapHeight - padding * 2f));
        float x = anchor.x * mapWidth;
        float y = anchor.y * mapHeight;
        bool atLeft = x < padding + width * .5f;
        bool atRight = x > mapWidth - padding - width * .5f;
        // TMP alignment setter is not exposed to Udon: use authored variants, only one visible.
        Transform left = marker.Find("PerformerNameLeft");
        Transform right = marker.Find("PerformerNameRight");
        if (left != null && right != null)
        {
            left.gameObject.SetActive(atLeft);
            right.gameObject.SetActive(!atLeft && atRight);
            label.gameObject.SetActive(!atLeft && !atRight);
            if (atLeft || atRight)
            {
                TextMeshProUGUI edgeLabel = (atLeft ? left : right).GetComponent<TextMeshProUGUI>();
                edgeLabel.text = label.text;
                label = edgeLabel;
            }
        }
        RectTransform nameRect = label.GetComponent<RectTransform>();
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(.5f, .5f);
        nameRect.pivot = new Vector2(.5f, .5f);
        nameRect.sizeDelta = new Vector2(width, height);
        float offsetY = -38f;
        // 下端付近だけ名前を上へ。左右は名前ボックスの端を基準に制限。
        if (y + offsetY - height * .5f < padding) offsetY = 38f;
        float labelX = Mathf.Clamp(x, padding + width * .5f, mapWidth - padding - width * .5f);
        if (atLeft)
        {
            labelX = Mathf.Clamp(x - marker.rect.width * .5f, padding, mapWidth - padding - width) + width * .5f;
        }
        else if (atRight)
        {
            labelX = Mathf.Clamp(x + marker.rect.width * .5f, padding + width, mapWidth - padding) - width * .5f;
        }
        float labelY = Mathf.Clamp(y + offsetY, padding + height * .5f, mapHeight - padding - height * .5f);
        nameRect.anchoredPosition = new Vector2(labelX - x, labelY - y);
    }
}
