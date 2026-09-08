
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 두 개의 포스터 출력을 재사용하며 등록된 Sprite를 좌우 이동으로 교체
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PosterSlide : UdonSharpBehaviour
{
    [Header("포스터 오브젝트 설정")]
    public GameObject posterPrefab;
    public Sprite[] sprites;
    public RectTransform PosterCanvas;
    public RectTransform createTarget;

    [Header("포스터 시간 설정")]
    public float slideDuration = 1.0f;
    public float interval = 10.0f;

    private int currentIndex = 0;
    private Image currentPoster;
    private Image nextPoster;
    private RectTransform currentRect;
    private RectTransform nextRect;
    private float timer = 0f;
    private bool isSliding = false;
    private float slideProgress = 0f;
    private float canvasWidth = 1000f;

    void Start()
    {
        // Cuding Edit: 잘못된 설정에서는 Update까지 진행하지 않고 한 번만 오류 안내
        if (sprites == null || sprites.Length == 0 || posterPrefab == null || createTarget == null)
        {
            Debug.LogError("[PosterSlide] Sprite, prefab and create target are required.");
            enabled = false;
            return;
        }
        if (posterPrefab.GetComponent<Image>() == null || posterPrefab.GetComponent<RectTransform>() == null)
        {
            Debug.LogError("[PosterSlide] Poster prefab requires Image and RectTransform.");
            enabled = false;
            return;
        }
        if (PosterCanvas != null) canvasWidth = PosterCanvas.rect.width;
        slideDuration = Mathf.Max(0.01f, slideDuration);
        interval = Mathf.Max(0.1f, interval);

        // Cuding Edit: 시작할 때만 출력 생성 및 컴포넌트 캐시. 교체 중 생성/삭제는 하지 않음
        currentPoster = Instantiate(posterPrefab, createTarget).GetComponent<Image>();
        currentRect = currentPoster.rectTransform;
        currentPoster.sprite = sprites[0];
        currentRect.anchoredPosition = Vector2.zero;
        if (sprites.Length == 1) { enabled = false; return; }
        nextPoster = Instantiate(posterPrefab, createTarget).GetComponent<Image>();
        nextRect = nextPoster.rectTransform;
        nextPoster.gameObject.SetActive(false);
    }

    void Update()
    {
        timer += Time.deltaTime;
        
        if (!isSliding && timer >= interval)
        {
            timer = 0f;
            StartSlide();
        }

        if (isSliding)
        {
            SlidePosters();
        }
    }

    void StartSlide()
    {
        currentIndex = (currentIndex + 1) % sprites.Length;
        nextPoster.sprite = sprites[currentIndex];
        nextRect.anchoredPosition = new Vector2(canvasWidth, 0);
        nextPoster.gameObject.SetActive(true);
        isSliding = true;
        slideProgress = 0f;
    }

    void SlidePosters()
    {
        slideProgress += Time.deltaTime / slideDuration;
        float newX = Mathf.Lerp(canvasWidth, 0, slideProgress);
        float oldX = Mathf.Lerp(0, -canvasWidth, slideProgress);

        nextRect.anchoredPosition = new Vector2(newX, 0);
        currentRect.anchoredPosition = new Vector2(oldX, 0);

        if (slideProgress >= 1f)
        {
            isSliding = false;
            currentPoster.gameObject.SetActive(false);
            Image reusablePoster = currentPoster;
            RectTransform reusableRect = currentRect;
            currentPoster = nextPoster;
            currentRect = nextRect;
            nextPoster = reusablePoster;
            nextRect = reusableRect;
        }
    }
}
