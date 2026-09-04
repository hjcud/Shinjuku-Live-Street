
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 무대 사용자의 음성 거리와 증폭값 변경을 사용자 ID와 함께 동기화한다.
/// </summary>
public class VoiceRange : UdonSharpBehaviour
{
    [Header("소리 범위 설정")]
    public float DefaultVoiceRange = 25f;
    public float ChangedVoiceRange = 50f;
    [Header("소리 크기기 설정")]
    public float DefaultVoiceGain = 15f;
    public float ChangedVoiceGain = 20f;

    [Header("버튼 UI")]
    public Text[] ButtonText;

    [Header("동기화용 변수")]
    public bool OnStageLocal = false;
    [UdonSynced] string SyncedUserId;

    /// <summary>
    /// 로컬 사용자의 무대 상태를 바꾸고 사용자 ID가 포함된 상태를 직렬화한다.
    /// </summary>
    public void ButtonTrigger()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
        }

        if (!OnStageLocal)
        {
            for (int i = 0; i < ButtonText.Length; i++)
            {
                if (ButtonText[i] != null)
                {
                    ButtonText[i].color = new Color(171/255f , 171/255f, 171/255f);
                }
            }
            OnStageLocal = true;
            SyncedUserId = "T" + Networking.LocalPlayer.playerId;
            RequestSerialization();
        }
        else
        {
            for (int i = 0; i < ButtonText.Length; i++)
            {
                if (ButtonText[i] != null)
                {
                    ButtonText[i].color = new Color(64/255f , 64/255f, 64/255f);
                }
            }
            OnStageLocal = false;
            SyncedUserId = "F" + Networking.LocalPlayer.playerId;
            RequestSerialization();
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (OnStageLocal)
        {    
            if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            }
            SyncedUserId = "T" + Networking.LocalPlayer.playerId;
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        ChangeVoiceGlobal();
    }

    /// <summary>
    /// 동기화된 사용자 ID에 해당하는 플레이어의 음성 거리와 증폭값을 적용한다.
    /// </summary>
    public void ChangeVoiceGlobal()
    {
        var players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; 
        VRCPlayerApi.GetPlayers(players);

		foreach (var player in players)
		{
            if (String.Equals("T" + player.playerId, SyncedUserId))
            {
                player.SetVoiceGain(ChangedVoiceGain);
		        player.SetVoiceDistanceFar(ChangedVoiceRange);
            }
            else if (String.Equals("F" + player.playerId, SyncedUserId))
            {
                player.SetVoiceGain(DefaultVoiceGain);
		        player.SetVoiceDistanceFar(DefaultVoiceRange);
            }
        }
    }
}
