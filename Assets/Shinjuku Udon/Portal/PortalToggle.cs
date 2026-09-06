
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 선택된 Portal 하나의 활성 상태를 로컬 전환하고 나머지 Portal 비활성화
/// </summary>
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
