
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Image;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 동기화된 URL의 이미지를 내려받아 화면 비율을 유지한 채 스피커 화면에 표시
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ImageLoader : UdonSharpBehaviour
{
    [UdonSynced] public VRCUrl syncedUrl = VRCUrl.Empty;
    [UdonSynced] private int imageRevision;
    [UdonSynced] private int speakerGeneration;
    [SerializeField] private VRCUrlInputField inputField;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private Text systemText;
    private VRCImageDownloader imageDownloader;
    private RawImage image;
    private UdonBehaviour udon;
    private IVRCImageDownload downloadInfo;
    private float maxWidth;
    private float maxHeight;
    private bool initialized;
    private bool speakerActive;
    private int activeSpeakerGeneration;
    private int pendingResetGeneration;
    private int appliedRevision = -1;
    private string appliedUrl = "";
    private float messageUntil;

    public void Start()
    {
        LoadImage();
    }

    /// <summary>
    /// 새 배치 세대를 시작하고 이전 배치의 로컬 이미지와 다운로드를 제거
    /// </summary>
    public void BeginSpeakerPlacement(int generation)
    {
        if (generation <= 0 || generation < activeSpeakerGeneration) return;
        activeSpeakerGeneration = generation;
        speakerActive = true;
        ClearLocalImage();

        // 동기화가 배치 이벤트보다 먼저 도착한 경우 현재 세대의 이미지를 복원
        if (speakerGeneration >= generation) LoadImage();
    }

    /// <summary>
    /// 반환된 스피커에서 늦게 도착한 동기화가 이미지를 다시 표시하지 않도록 차단
    /// </summary>
    public void EndSpeakerPlacement(int generation)
    {
        if (generation <= 0 || generation < activeSpeakerGeneration) return;
        activeSpeakerGeneration = generation;
        speakerActive = false;
        ClearLocalImage();
    }

    /// <summary>
    /// 현재 스피커 사용자가 이미지 객체의 소유권을 확보하고 빈 상태를 동기화
    /// </summary>
    public void ResetSpeakerImage(int generation)
    {
        if (generation <= 0 || generation < activeSpeakerGeneration) return;
        activeSpeakerGeneration = generation;
        ClearLocalImage();
        pendingResetGeneration = generation;
        TryResetSpeakerImage();
    }

    private void TryResetSpeakerImage()
    {
        if (pendingResetGeneration <= 0 || !Utilities.IsValid(Networking.LocalPlayer)) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (!Networking.IsOwner(gameObject)) return;

        speakerGeneration = pendingResetGeneration;
        pendingResetGeneration = 0;
        syncedUrl = VRCUrl.Empty;
        imageRevision++;
        LoadImage();
        RequestSerialization();
    }

    public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
    {
        if (newOwner == Networking.LocalPlayer) TryResetSpeakerImage();
    }

    // 입장/역직렬화 순서와 관계없이 출력 참조와 원래 표시 크기를 한 번만 준비
    private bool Initialize()
    {
        if (initialized) return true;
        if (!Utilities.IsValid(rectTransform)) return false;
        image = rectTransform.GetComponent<RawImage>();
        if (!Utilities.IsValid(image)) return false;
        image.enabled = false;
        udon = (UdonBehaviour)GetComponent(typeof(UdonBehaviour));
        maxWidth = Mathf.Max(1f, rectTransform.rect.width);
        maxHeight = Mathf.Max(1f, rectTransform.rect.height);
        initialized = true;
        return true;
    }

    /// <summary>
    /// 소유권자가 빈 URL을 동기화하여 현재 사용자와 늦은 참가자 모두 이미지 초기화
    /// </summary>
    [NetworkCallable]
    public void ResetTex()
    {
        // All 이벤트를 받아도 이미지 소유자 한 명만 영속 상태 변경
        if (!Networking.IsOwner(gameObject)) return;
        int generation = Mathf.Max(activeSpeakerGeneration, speakerGeneration);
        if (generation <= 0) return;
        ResetSpeakerImage(generation);
    }

    /// <summary>
    /// 입력된 URL의 소유권 확보 후 이미지 다운로드와 직렬화 시작
    /// </summary>
    public void OnEndUrlEdit()
    {
        if (!speakerActive || !Utilities.IsValid(inputField) || !Utilities.IsValid(Networking.LocalPlayer)) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (!Networking.IsOwner(gameObject)) return;
        pendingResetGeneration = 0;
        speakerGeneration = activeSpeakerGeneration;
        syncedUrl = inputField.GetUrl();
        imageRevision++;
        
        LoadImage();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        if (!speakerActive || speakerGeneration < activeSpeakerGeneration)
        {
            ClearLocalImage();
            return;
        }
        activeSpeakerGeneration = speakerGeneration;
        LoadImage();
    }

    private void LoadImage()
    {
        if (!Initialize()) return;
        if (!speakerActive || speakerGeneration < activeSpeakerGeneration)
        {
            ClearLocalImage();
            return;
        }
        string url = Utilities.IsValid(syncedUrl) ? syncedUrl.ToString() : "";
        if (appliedRevision == imageRevision && appliedUrl == url) return;
        appliedRevision = imageRevision;
        appliedUrl = url;

        // 이전 요청과 텍스처는 교체 시 해제. 같은 URL 재요청도 새 핸들로 구분
        ClearDownload();
        if (url == "")
        {
            if (Utilities.IsValid(inputField)) inputField.SetUrl(VRCUrl.Empty);
            SetMessage("");
            return;
        }
        if (!url.StartsWith("https://") && !url.StartsWith("http://"))
        {
            SetMessage("Invalid image URL");
            return;
        }
        if (imageDownloader == null) imageDownloader = new VRCImageDownloader();
        SetMessage("Downloading...");
        downloadInfo = imageDownloader.DownloadImage(syncedUrl, null, udon);
    }
    
    public override void OnImageLoadSuccess(IVRCImageDownload result) {
        // 취소/초기화 전의 늦은 응답은 출력과 입력 UI를 변경하지 않음
        if (result == null) return;
        if (result != downloadInfo) { result.Dispose(); return; }
        Texture2D tex = result.Result;
        if (!Utilities.IsValid(tex)) { ClearDownload(); SetMessage("Image is unavailable"); return; }
        image.texture = tex;
        image.enabled = true;
        SetMessage("Download Complete");
        ClearSubmittedInput();

        float texWidth = tex.width;
        float texHeight = tex.height;
        float texRatio = texWidth / texHeight;
        float targetWidth = maxWidth;
        float targetHeight = maxHeight;

        if (texRatio > (maxWidth / maxHeight)) {
            targetHeight = maxWidth / texRatio;
        } else {
            targetWidth = maxHeight * texRatio;
        }

        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
    }

    public override void OnImageLoadError(IVRCImageDownload result) {
        if (result == null) return;
        if (result != downloadInfo) { result.Dispose(); return; }
        string error = result.ErrorMessage;
        ClearDownload();
        SetMessage("Error(" + error + ")");
        ClearSubmittedInput();
    }

    private void ClearSubmittedInput()
    {
        if (Utilities.IsValid(inputField) && inputField.GetUrl().ToString() == appliedUrl)
            inputField.SetUrl(VRCUrl.Empty);
    }

    private void ClearLocalImage()
    {
        appliedRevision = -1;
        appliedUrl = "";
        ClearDownload();
        if (Utilities.IsValid(inputField)) inputField.SetUrl(VRCUrl.Empty);
        SetMessage("");
    }

    private void ClearDownload()
    {
        if (Utilities.IsValid(image)) { image.enabled = false; image.texture = null; }
        IVRCImageDownload previous = downloadInfo;
        downloadInfo = null;
        if (previous != null) previous.Dispose();
    }

    private void OnDestroy()
    {
        ClearDownload();
        if (imageDownloader != null) imageDownloader.Dispose();
    }

    private void SetMessage(string message)
    {
        if (!Utilities.IsValid(systemText)) return;
        systemText.text = message;
        messageUntil = Time.time + 5f;
        if (message != "") SendCustomEventDelayedSeconds(nameof(resetSystemText), 5f);
    }

    /// <summary>
    /// 다운로드 결과 안내 표시 후 상태 문구 제거
    /// </summary>
    public void resetSystemText() {
        // 이전 안내의 예약 이벤트가 새 안내까지 지우지 않도록 표시 기한 확인
        if (Utilities.IsValid(systemText) && Time.time >= messageUntil) systemText.text = "";
    }
}
