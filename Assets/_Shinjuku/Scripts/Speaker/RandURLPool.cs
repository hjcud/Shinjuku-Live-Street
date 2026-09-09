
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
        // 정수 난수의 상한은 제외되므로 Length까지 전달. 빈 목록은 안전하게 반환
        if (vrcUrlPool == null || vrcUrlPool.Length == 0) return VRCUrl.Empty;
        int index = Random.Range(0, vrcUrlPool.Length);
        return vrcUrlPool[index];
    }
}
