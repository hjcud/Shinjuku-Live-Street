
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PortalToggle : UdonSharpBehaviour
{
    [SerializeField] int PortalNum = 0;
    [SerializeField] GameObject[] portals;

    private void Interact()
    {
        for (int i = 0; i < portals.Length; i++)
        {
            if (i == PortalNum)
            {
                if (!portals[i].activeSelf)
                {
                    portals[i].SetActive(true);
                }
                else
                {
                    portals[i].SetActive(false);
                }
            }
            else
            {
                portals[i].SetActive(false);
            }
        }
    }
}
