
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ObjectGlobalToggle : UdonSharpBehaviour
{
    [SerializeField] private bool activeDefault;
    [SerializeField] private GameObject targetObject;
    [UdonSynced] bool isObjectActive = false;
    public Text ButtonText;

    void Start()
    {
        targetObject.SetActive(activeDefault);
        isObjectActive = targetObject.activeSelf;
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
        isObjectActive = false;
        RequestSerialization();
        ToggleTarget();
    }

    public override void OnDeserialization()
    {
        ToggleTarget();
    }

    public void ToggleTarget()
    {
        targetObject.SetActive(isObjectActive);
        ButtonText.color = isObjectActive ? new Color(171/255f , 171/255f, 171/255f) : new Color(64/255f , 64/255f, 64/255f);
    }
}