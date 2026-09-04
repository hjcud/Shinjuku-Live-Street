
using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;


namespace QvPen.UdonScript
{
    /// <summary>
    /// Pen이 지정 구역을 벗어나면 QvPen Manager를 통해 원래 위치로 되돌린다.
    /// </summary>
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
