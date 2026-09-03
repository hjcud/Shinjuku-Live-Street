
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

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