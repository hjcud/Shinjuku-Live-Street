
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class PosterSlide : UdonSharpBehaviour
{
    [Header("포스터 오브젝트 설정")]
    public GameObject posterPrefab;
    public Sprite[] sprites;
    public RectTransform PosterCanvas;
    public RectTransform createTarget;

    [Header("포스터 시간 설정")]
    public float slideDuration = 1.0f; // 포스터 이동 시간
    public float interval = 10.0f; // 포스터 생성 간격 (초)

    private int currentIndex = 0;
    private GameObject currentPoster;
    private GameObject nextPoster;
    private float timer = 0f;
    private bool isSliding = false;
    private float slideProgress = 0f;
    private float canvasWidth = 1000f;

    void Start()
    {
        if (sprites.Length > 0)
        {
            if (PosterCanvas != null)
            {
                canvasWidth = PosterCanvas.rect.width;
            }
            else
            {
                Debug.LogWarning("PosterCanvas is null. Using default canvasWidth.");
            }
            currentPoster = CreatePoster(sprites[currentIndex], Vector2.zero);
        }    
        else
        {
            Debug.LogError("Sprites array is empty. Please assign at least one sprite.");
        }   
    }

    GameObject CreatePoster(Sprite sprite, Vector2 position)
    {
        if (posterPrefab == null)
        {
            Debug.LogError("Poster Prefab is not assigned.");
            return null;
        }
        GameObject newPoster = Instantiate(posterPrefab, createTarget);
        newPoster.GetComponent<Image>().sprite = sprite;
        RectTransform rt = newPoster.GetComponent<RectTransform>();
        rt.anchoredPosition = position;
        return newPoster;
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
        nextPoster = CreatePoster(sprites[currentIndex], new Vector2(canvasWidth, 0)); // 오른쪽에서 시작
        isSliding = true;
        slideProgress = 0f;
    }

    void SlidePosters()
    {
        slideProgress += Time.deltaTime / slideDuration;
        float newX = Mathf.Lerp(canvasWidth, 0, slideProgress);
        float oldX = Mathf.Lerp(0, -canvasWidth, slideProgress);

        nextPoster.GetComponent<RectTransform>().anchoredPosition = new Vector2(newX, 0);
        if (currentPoster != null)
        {
            currentPoster.GetComponent<RectTransform>().anchoredPosition = new Vector2(oldX, 0);
        }

        if (slideProgress >= 1f)
        {
            isSliding = false;
            if (currentPoster != null)
            {
                Destroy(currentPoster);
            }
            currentPoster = nextPoster;
            nextPoster = null;
        }
    }
}
