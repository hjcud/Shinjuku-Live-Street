# 신주쿠 라이브 스트리트

VRChat 월드 **Shinjuku Live Street**를 위해 직접 작성한 C# / UdonSharp 코드만 공개하는 저장소입니다.

> 이 저장소는 완전한 Unity 프로젝트가 아닙니다. 씬, 프리팹, 모델, 이미지, 음원, 머티리얼, 애니메이션, 셰이더, Unity `.meta` 파일 및 외부 에셋의 소스는 포함하지 않습니다.

## 포함된 코드

- `Assets/Shinjuku Udon/Traffic`
  - 레인 데이터 베이킹 및 검증
  - 네트워크 권한 기반 교통 시뮬레이션
  - 차량 회피, 차선 변경, 신호 처리, 복구 로직
  - 런타임 디버그 및 키네마틱 차량 테스트 도구
- `Assets/Shinjuku Udon/Car`
  - 차량 패널 연출 및 에디터 설정 도구
- `Assets/Shinjuku Udon/ObjectToggle`
  - 로컬/글로벌 오브젝트 제어와 상태 초기화
- `Assets/Shinjuku Udon/Portal`, `Posters`, `Speaker`, `Teleport`
  - 월드 상호작용과 미디어/음성 관련 제어 코드
- `Assets/Editor`
  - 머티리얼 텍스처 할당 도구
  - 교통 시스템 플레이어 스트레스 테스트 도구

## 외부 의존성 및 사용 에셋

아래 항목은 프로젝트에서 사용되거나 코드가 참조하지만 **이 저장소에는 포함되지 않습니다**. 사용하려면 각 배포처에서 라이선스를 확인하고 직접 설치해야 합니다.

### 패키지/SDK

- Unity `2022.3.22f1`
- VRChat SDK - Worlds `3.8.1` (UdonSharp 포함)
- Topaz Chat `0.1.6`
- lilToon `1.10.3`
- VRWorldToolkit `3.2.1`
- TextMesh Pro

### 프로젝트에서 확인된 주요 외부 에셋/도구

- AVPro Video
- Bakery GPU Lightmapper
- QvPen
- IwaSync3 / HoshinoLabs
- Media Manager
- Mochie Shaders
- EasyTextures
- VRC Players Only Mirror
- VRC Music Event Calendar
- Year Progress Bar
- imagePad
- Lura's Switch (Udon)
- Prototype Collection
- AllSkyFree
- Noriben Lunch shader assets
- Atelier Rayrell, RIONESTA, Zelkova Tree 및 기타 모델/환경 리소스

각 외부 항목의 저작권과 라이선스는 원 저작자에게 있습니다. Topaz Chat을 포함한 외부 패키지의 코드는 커밋 대상에서 명시적으로 제외했습니다.

## 저장소 정책

`.gitignore`는 허용 목록 방식으로 설정되어 있습니다. 현재는 위 자체 코드의 `.cs` 파일과 저장소 문서만 추적되며 다음 항목은 항상 제외됩니다.

- Unity 씬 및 프리팹
- 이미지, 모델, 음원, 영상
- 머티리얼, 애니메이션, 셰이더, 렌더 텍스처
- Unity `.meta` 및 직렬화된 Udon 프로그램 에셋
- `Packages`, `ProjectSettings`, `Library`, `Temp`, `Logs`, `UserSettings`
- Topaz Chat과 기타 외부/서드파티 코드

새 코드를 다른 폴더에 추가할 때는 해당 파일을 `.gitignore` 허용 목록에 명시적으로 등록해야 합니다.

## 라이선스

이 저장소의 자체 작성 코드에는 현재 별도의 오픈 소스 라이선스가 부여되어 있지 않습니다. 외부 의존성과 에셋은 각 원 저작자의 라이선스를 따릅니다.
