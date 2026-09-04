
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 여러 GameObject와 Switch 표시 상태를 로컬 사용자에게만 전환한다.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ObjectLocalToggle : UdonSharpBehaviour
{
    [SerializeField] private bool activeDefault;
    [SerializeField] private GameObject[] targetObjects;
    [SerializeField] private GameObject SwitchOn;
    [SerializeField] private GameObject SwitchOff;
    bool isObjectActive = false;

    void Start()
    {
        isObjectActive = activeDefault;
        ObjectToggle();
    }

    public override void Interact()
    {
        ObjectToggle();
    }

    void ObjectToggle()
    {
        isObjectActive = !isObjectActive;
        
        if (isObjectActive)
        {
            SwitchOn.SetActive(false);
            SwitchOff.SetActive(true);

            foreach (GameObject obj in targetObjects)
            {
                obj.SetActive(false);
            }
        }
        else
        {
            SwitchOn.SetActive(true);
            SwitchOff.SetActive(false);

            foreach (GameObject obj in targetObjects)
            {
                obj.SetActive(true);
            }
        }
    }
}
