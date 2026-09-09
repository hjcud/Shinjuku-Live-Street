
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 등록된 Transform을 부모 위치와 회전으로 되돌리는 이벤트를 모든 사용자에게 전달
/// </summary>
public class ObjectTransformReset : UdonSharpBehaviour
{
    [SerializeField] private Transform[] objects;

    /// <summary>
    /// 모든 사용자에게 등록된 Transform의 원위치 복귀 요청
    /// </summary>
    public void ButtonTrigger()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetObject");
    }

    /// <summary>
    /// 등록된 각 Transform에 부모의 현재 위치와 회전 적용
    /// </summary>
    public void ResetObject()
    {
        if (objects.Length < 1) return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                objects[i].SetPositionAndRotation(objects[i].parent.position, objects[i].parent.rotation);
            }
        }
    }
}
