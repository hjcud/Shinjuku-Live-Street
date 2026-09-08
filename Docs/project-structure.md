# 프로젝트 폴더와 씬 구성

자체 콘텐츠, 외부 패키지, 생성 데이터를 분리한다. 폴더 이름 변경 자체를 성능 최적화로 간주하지 않는다.

## Assets

| 경로 | 용도 |
|---|---|
| `Assets/_Shinjuku/Scripts` | 기능별 자체 런타임 코드. 연결된 Udon 프로그램 에셋은 해당 코드 옆에 유지 |
| `Assets/_Shinjuku/Editor` | 자체 Editor 도구. 기존 Editor 전용 컴파일 범위 유지 |
| `Assets/_Shinjuku/Scenes/TEST.unity` | 현재 작업 씬 |
| `Assets/_Shinjuku/Scenes/TEST` | 작업 씬의 기존 오클루전·반사 프로브 데이터 |
| `Assets/_Shinjuku/Prefabs` | 포스터·스피커·상호작용 프리팹 |
| `Assets/_Shinjuku/Art` | Models, Materials, Textures, Animations, RenderTextures, Effects |
| `Assets/_Shinjuku/Audio` | 프로젝트 음원 |

`Scripts`와 콘텐츠 하위 폴더는 Traffic, Speaker, Posters, Car 등의 기능명을 사용한다. 스피커의 `v2.7` 폴더 표기는 제거했지만 클래스명과 동작은 변경하지 않았다.

외부 패키지, `Assets/Script`의 출처 미확인 코드, 원본 모델이 있는 `Assets/model`, Bakery 전용 데이터와 `Assets/TEST` 등의 생성 경로는 유지한다. `Assets/model/Save.unity`도 이번 정리 대상에서 제외했다. 이름이 TEST/Save라는 이유만으로 파일을 삭제하지 않는다.

## 현재 씬의 상위 그룹

| 그룹 | 내용 |
|---|---|
| `00_World` | 원본 건물·배경·나무·고정 소품 |
| `10_Lighting` | 조명·프로브·후처리·오클루전 |
| `20_Systems` | 교통·신호·스피커·영상 재생 시스템 |
| `30_Interactables` | 거울·펜·등신대·악기·텔레포트 |
| `40_Displays` | 포스터·안내·캘린더·월드 추천 |
| `50_Audio` | 환경음과 관련 입력·트리거 |
| `90_Cameras` | 기존 카메라 |

`VRCWorld`, `EventSystem`, `!ftraceLightmaps`는 SDK/생성 도구 관리 오브젝트이므로 루트에 유지한다.

상위 그룹은 위치 0, 회전 0, 스케일 1이며 런타임 컴포넌트를 추가하지 않는다. 기존 프리팹은 Unpack하지 않고 내부 구조와 이름을 유지한다. 그룹의 Transform이나 활성 상태를 일괄 변경하지 않는다.

## 이동과 검증 원칙

- Unity Project 창 또는 `AssetDatabase.MoveAsset`으로 `.meta`와 GUID를 보존하며 이동한다.
- `Editor`, `Resources`, `StreamingAssets`, 외부 패키지 및 베이크 출력 경로는 일반 폴더처럼 취급하지 않는다.
- 이름 검색과 상대 경로에 쓰이는 `등신대`, `TrafficSystem_V2`, `Runtime` 등의 이름을 유지한다.
- 재배치 후 월드 Transform, 직렬화된 참조, 프리팹 연결, 애니메이션 바인딩 대상, 라이트맵 인덱스와 텍스처를 확인한다.
- Udon 컴파일 후 VRChat에서 주요 기능을 확인한다. 정적 검증은 실제 멀티플레이 테스트를 대체하지 않는다.

이 저장소는 자체 C#와 공개 문서만 추적한다. Unity 에셋과 `.meta`는 디스크에 보존되지만 Git에는 포함하지 않으므로 **Git만으로 전체 프로젝트나 씬을 복구할 수 없다.** 전체 프로젝트 백업은 별도로 유지한다.

2026-09-08 정리의 로컬 이동 목록과 변경 전 씬은 `output/project-organization`에 보관되어 있다. 이 폴더는 Git과 월드 빌드에 포함되지 않는다.
