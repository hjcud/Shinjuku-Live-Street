
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 등록된 VRCUrl 목록에서 임의의 주소를 선택해 제공한다.
/// </summary>
public class RandURLPool : UdonSharpBehaviour
{
    [UdonSynced]
    public VRCUrl[] vrcUrlPool;

    /// <summary>
    /// URL 목록에서 임의의 항목을 반환한다.
    /// </summary>
    /// <returns>선택된 VRCUrl이다.</returns>
    public VRCUrl GetRandUrl()
    {
        int index = Random.Range(0, vrcUrlPool.Length - 1);
        return vrcUrlPool[index];
    }
}
