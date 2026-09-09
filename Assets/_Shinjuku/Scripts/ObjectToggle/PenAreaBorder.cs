
using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;


namespace QvPen.UdonScript
{
    /// <summary>
    /// Pen이 지정 구역을 벗어나면 QvPen Manager를 통해 원래 위치로 복귀
    /// </summary>
    public class PenAreaBorder : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_PenManager qvPen_Manager;
        private QvPen_Pen targetPen;

        private void Start()
        {
            // 기존 Manager 연결에서 해당 Pen만 캐시하여 다른 소품은 무시
            if (Utilities.IsValid(qvPen_Manager))
                targetPen = (QvPen_Pen)qvPen_Manager.GetProgramVariable("pen");
        }

        void OnTriggerExit(Collider other)
        {
            if (!Utilities.IsValid(targetPen) || !Utilities.IsValid(other)) return;
            if (!other.transform.IsChildOf(targetPen.transform)) return;
            // 여러 클라이언트에서 같은 경계 이벤트가 발생해도 Pen 소유자만 복귀 처리
            if (!Networking.IsOwner(targetPen.gameObject)) return;
            qvPen_Manager.Respawn();
        }
        
    }
}
