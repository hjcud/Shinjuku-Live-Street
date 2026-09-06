
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.SDK3.Components;
using VRC.SDK3.Image;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 동기화된 URL의 이미지를 내려받아 화면 비율을 유지한 채 스피커 화면에 표시
/// </summary>
public class ImageLoader : UdonSharpBehaviour
{
    [UdonSynced] public VRCUrl syncedUrl;
    [SerializeField] private VRCUrlInputField inputField;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private Text systemText;
    private VRCImageDownloader imageDownloader;
    private Material material;
    private UdonBehaviour udon;
    private IVRCImageDownload downloadInfo;
    private Texture2D tex;
    private float maxWidth;
    private float maxHeight;

    public void Start()
    {
        rectTransform.GetComponent<RawImage>().enabled = false;
        imageDownloader = new VRCImageDownloader();
        udon = transform.GetComponent<UdonBehaviour>();
        maxWidth = rectTransform.rect.width;
        maxHeight = rectTransform.rect.height;
    }

    /// <summary>
    /// 모든 클라이언트에서 내려받은 Texture와 화면 표시 초기화
    /// </summary>
    [NetworkCallable]
    public void ResetTex()
    {
        rectTransform.GetComponent<RawImage>().enabled = false;
        tex = null;
    }
    
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if(Networking.LocalPlayer == player)
        {
            LoadImage();
        }
    }

    /// <summary>
    /// 입력된 URL의 소유권 확보 후 이미지 다운로드와 직렬화 시작
    /// </summary>
    public void OnEndUrlEdit()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        syncedUrl = inputField.GetUrl();
        
        LoadImage();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        LoadImage();
    }

    private void LoadImage()
    {
        if ((syncedUrl == null) || (syncedUrl == VRCUrl.Empty)) return;
        else if (syncedUrl.ToString().Length < 11) return;
        else if (syncedUrl.ToString().Substring(0, 4) != "http") return;
        else
        {
            downloadInfo = imageDownloader.DownloadImage(syncedUrl, material, udon);
        }
    }
    
    public override void OnImageLoadSuccess(IVRCImageDownload result) {
        downloadInfo = null;
        rectTransform.GetComponent<RawImage>().enabled = true;
        systemText.text = "Download Complete";
        SendCustomEventDelayedSeconds("resetSystemText", 5f);
        inputField.SetUrl(VRCUrl.Empty);
        tex = result.Result;
        rectTransform.GetComponent<RawImage>().texture = tex;

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
        downloadInfo = null;
        rectTransform.GetComponent<RawImage>().enabled = false;
        systemText.text = "Error(" + result.ErrorMessage + ")";
        SendCustomEventDelayedSeconds("resetSystemText", 5f);
        inputField.SetUrl(VRCUrl.Empty);
        tex = null;
    }

    /// <summary>
    /// 다운로드 결과 안내 표시 후 상태 문구 제거
    /// </summary>
    public void resetSystemText() {
         systemText.text = "";
    }
}
