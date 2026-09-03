
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SpeakerRevToggle : UdonSharpBehaviour
{
    [SerializeField] private AudioReverbFilter[] targetObjects;
    [UdonSynced] bool isObjectActive = false;
    public Text ButtonText;

    void Start()
    {
        isObjectActive = targetObjects[0].enabled;
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if(Networking.LocalPlayer == player)
        {
            ToggleTarget();
        }
    }

    public void ButtonTrigger()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        isObjectActive = !isObjectActive;

        ToggleTarget();
        RequestSerialization();
    }

    public void OwnerDisableTarget()
    {
        isObjectActive = true;
        ToggleTarget();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        ToggleTarget();
    }

    public void ToggleTarget()
    {
        foreach(AudioReverbFilter targetObject in targetObjects)
        {
            targetObject.enabled = isObjectActive;
        }
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }
}
