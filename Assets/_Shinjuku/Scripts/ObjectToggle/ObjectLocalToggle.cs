
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 로컬 사용자에게만 여러 GameObject와 Switch 표시 상태 전환
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ObjectLocalToggle : UdonSharpBehaviour
{
    [SerializeField] private bool activeDefault;
    [SerializeField] private GameObject[] targetObjects;
    [SerializeField] private GameObject SwitchOn;
    [SerializeField] private GameObject SwitchOff;
    // 지정된 환경설정 스위치만 저장 서비스에 위임. 미지정이면 기존 오브젝트 토글 유지.
    public WorldLocalSettings worldSettings;
    public bool controlsVehicles;
    bool isObjectActive = false;

    void Start()
    {
        if (worldSettings != null)
        {
            _ApplySettingState(controlsVehicles ? worldSettings.vehiclesEnabled : worldSettings.ambientEnabled);
            return;
        }
        isObjectActive = activeDefault;
        ObjectToggle();
    }

    public override void Interact()
    {
        if (worldSettings != null)
        {
            if (controlsVehicles) worldSettings._ToggleVehicles();
            else worldSettings._ToggleAmbient();
            return;
        }
        ObjectToggle();
    }

    /// <summary>
    /// 복원된 설정에 맞춰 표시만 갱신하며 대상 오브젝트나 저장값은 변경하지 않음
    /// </summary>
    public void _ApplySettingState(bool active)
    {
        if (SwitchOn != null) SwitchOn.SetActive(active);
        if (SwitchOff != null) SwitchOff.SetActive(!active);
    }

    void ObjectToggle()
    {
        isObjectActive = !isObjectActive;
        bool active = !isObjectActive;
        SwitchOn.SetActive(active);
        SwitchOff.SetActive(!active);
        foreach (GameObject obj in targetObjects)
            obj.SetActive(active);
    }
}
