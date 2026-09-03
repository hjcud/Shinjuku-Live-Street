
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class RandURLPool : UdonSharpBehaviour
{
    [UdonSynced]
    public VRCUrl[] vrcUrlPool;

    public VRCUrl GetRandUrl()
    {
        int index = Random.Range(0, vrcUrlPool.Length - 1);
        return vrcUrlPool[index];
    }
}
