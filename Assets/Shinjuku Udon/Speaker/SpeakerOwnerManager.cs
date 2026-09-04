
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 동기화된 사용자 ID를 기준으로 스피커 Pickup의 사용 가능 여부와 소유자 표시를 관리한다.
/// </summary>
public class SpeakerOwnerManager : UdonSharpBehaviour
{
    [UdonSynced] public int ownerId = 0;
    [SerializeField]
    private VRCPickup speakerPickup;
    [SerializeField]
    private TextMeshProUGUI ownerText;
    [SerializeField]
    private GameObject[] toggleTargetObject;
    [SerializeField]
    private float distanceLimit = 5f;

    private string isOwnerTag = "";

    void Start()
    {
        Networking.LocalPlayer.SetPlayerTag(isOwnerTag, "false");
    }

    void Update()
    {
        if (ownerId != Networking.LocalPlayer.playerId) return;

        Vector3 playerPosition = Networking.LocalPlayer.GetPosition();
        float currentDistance = (playerPosition - this.gameObject.transform.position).magnitude;

        if (distanceLimit < currentDistance)
        {
            ownerId = 0;
            Networking.LocalPlayer.SetPlayerTag(isOwnerTag, "false");
            foreach (GameObject _toggleTargetObject in toggleTargetObject)
            {
                _toggleTargetObject.SetActive(false);
            }
            togglePickup();
            RequestSerialization();
        }
    }

    public override void OnPickupUseDown()
    {
        if (!Networking.IsOwner(this.gameObject)) return;

        if (ownerId != Networking.LocalPlayer.playerId)
        {
            if (Networking.LocalPlayer.GetPlayerTag(isOwnerTag) == "true") return;

            ownerId = Networking.LocalPlayer.playerId;
            Networking.LocalPlayer.SetPlayerTag(isOwnerTag, "true");
            foreach (GameObject _toggleTargetObject in toggleTargetObject)
            {
                _toggleTargetObject.SetActive(true);
            }
        }
        else
        {
            ownerId = 0;
            Networking.LocalPlayer.SetPlayerTag(isOwnerTag, "false");
            foreach (GameObject _toggleTargetObject in toggleTargetObject)
            {
                _toggleTargetObject.SetActive(false);
            }
        }
        togglePickup();
        RequestSerialization();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (Networking.LocalPlayer == player)
        {
            togglePickup();
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (ownerId == player.playerId)
        {
            ownerId = 0;
            togglePickup();
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        if (!Networking.IsOwner(this.gameObject))
        {
            togglePickup();
        }
    }

    private void togglePickup()
    {
        if (ownerId == Networking.LocalPlayer.playerId)
        {
            speakerPickup.pickupable = true;
            ownerText.text = Networking.LocalPlayer.displayName;
        }
        else if (ownerId == 0)
        {
            speakerPickup.pickupable = true;
            ownerText.text = "NONE";
        }
        else
        {
            speakerPickup.pickupable = false;
            ownerText.text = VRCPlayerApi.GetPlayerById(ownerId).displayName;
        }
    }
}
