
using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;


namespace QvPen.UdonScript
{
    public class PenAreaBorder : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_PenManager qvPen_Manager;

        void OnTriggerExit(Collider other)
        {
            qvPen_Manager.Respawn();
        }
        
    }
}
