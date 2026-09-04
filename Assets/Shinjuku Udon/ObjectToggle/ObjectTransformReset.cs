
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 등록된 Transform을 부모 위치와 회전으로 되돌리는 이벤트를 모든 사용자에게 전달한다.
/// </summary>
public class ObjectTransformReset : UdonSharpBehaviour
{
    [SerializeField] private Transform[] objects;

    /// <summary>
    /// 모든 사용자에게 등록된 Transform의 원위치 복귀를 요청한다.
    /// </summary>
    public void ButtonTrigger()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetObject");
    }

    /// <summary>
    /// 등록된 각 Transform을 부모의 현재 위치와 회전에 맞춘다.
    /// </summary>
    public void ResetObject()
    {
        if (objects.Length < 1) return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                objects[i].position = objects[i].parent.transform.position;
                objects[i].rotation = objects[i].parent.transform.rotation;
            }
        }
    }
}
