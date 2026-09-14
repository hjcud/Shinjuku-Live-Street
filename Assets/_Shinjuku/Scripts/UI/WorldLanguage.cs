using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>로컬 사용자의 VRChat 언어를 한 곳에서 감지하고 구독자에게 전달</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WorldLanguage : UdonSharpBehaviour
{
    [HideInInspector] public string languageCode = "en";
    [HideInInspector] public int currentLanguage; // 0 English, 1 Japanese, 2 Korean
    [Tooltip("언어가 바뀌면 _OnWorldLanguageChanged 이벤트 수신")]
    public UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[0];
    private bool initialized;

    private void Start() { GetLanguage(); }

    public int ResolveLanguage(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        code = code.ToLowerInvariant();
        if (code == "ja" || code.StartsWith("ja-")) return 1;
        if (code == "ko" || code.StartsWith("ko-")) return 2;
        return 0;
    }

    public int GetLanguage()
    {
        if (!initialized)
        {
            languageCode = VRCPlayerApi.GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode)) languageCode = "en";
            currentLanguage = ResolveLanguage(languageCode);
            initialized = true;
        }
        return currentLanguage;
    }

    public override void OnLanguageChanged(string language)
    {
        languageCode = string.IsNullOrEmpty(language) ? "en" : language;
        currentLanguage = ResolveLanguage(languageCode);
        initialized = true;
        if (listeners == null) return;
        for (int i = 0; i < listeners.Length; i++)
            if (listeners[i] != null) listeners[i].SendCustomEvent("_OnWorldLanguageChanged");
    }

    public void Register(UdonSharpBehaviour listener)
    {
        if (listener == null) return;
        int count = listeners == null ? 0 : listeners.Length;
        for (int i = 0; i < count; i++) if (listeners[i] == listener) return;
        UdonSharpBehaviour[] expanded = new UdonSharpBehaviour[count + 1];
        for (int i = 0; i < count; i++) expanded[i] = listeners[i];
        expanded[count] = listener;
        listeners = expanded;
    }

    public string SelectText(string english, string japanese, string korean)
    {
        int language = GetLanguage();
        return language == 1 ? japanese : language == 2 ? korean : english;
    }
}
