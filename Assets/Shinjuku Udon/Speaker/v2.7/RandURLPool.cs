
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 등록된 VRCUrl 목록에서 임의의 주소 선택 및 제공
/// </summary>
public class RandURLPool : UdonSharpBehaviour
{
    [UdonSynced]
    public VRCUrl[] vrcUrlPool;

    /// <summary>
    /// URL 목록에서 임의의 항목 반환
    /// </summary>
    /// <returns>선택된 VRCUrl</returns>
    public VRCUrl GetRandUrl()
    {
        int index = Random.Range(0, vrcUrlPool.Length - 1);
        return vrcUrlPool[index];
    }
}
