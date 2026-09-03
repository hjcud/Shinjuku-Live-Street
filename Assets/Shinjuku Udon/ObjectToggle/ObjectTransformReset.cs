
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ObjectTransformReset : UdonSharpBehaviour
{
    [SerializeField] private Transform[] objects;

    public void ButtonTrigger()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ResetObject");
    }

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
