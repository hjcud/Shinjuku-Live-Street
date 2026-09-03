
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MirrorLocalToggle : UdonSharpBehaviour
{
    [SerializeField]
    private int activeStep = 0;
    [SerializeField]
    private GameObject targetMirrorBase;
    [SerializeField]
    private GameObject targetMirrorLow;
    [SerializeField]
    private GameObject targetMirrorHigh;

    public override void Interact()
    {
        activeStep += 1;
        activeStep %= 3;

        switch (activeStep)
        {
            case 0:
                targetMirrorHigh.SetActive(false);
                targetMirrorBase.SetActive(true);
                break;
            case 1:
                targetMirrorBase.SetActive(false);
                targetMirrorLow.SetActive(true);
                break;
            case 2:
                targetMirrorLow.SetActive(false);
                targetMirrorHigh.SetActive(true);
                break;
            default:
                targetMirrorBase.SetActive(true);
                targetMirrorLow.SetActive(false);
                targetMirrorHigh.SetActive(false);
                break;
        }
    }
}