# Shinjuku Live Street 교통 시스템 300프레임 비교

차량 제어와 상태 동기화 구조를 개선하기 전후의 Unity Profiler 기록입니다. 두 캡처에서 각각 300프레임을 추출해 비교했습니다.

같은 PC의 Unity Editor에서 차량 10대와 ClientSim 원격 플레이어 80명을 한 지점에 배치한 조건입니다. **실제 이용자 80명이 접속한 서비스의 FPS 측정 결과는 아닙니다.**

## 파일

- [엑셀 비교표](./Shinjuku_300프레임_성능비교.xlsx): 비교 요약, 초기 300프레임, 개선 300프레임, 측정 조건을 수록했습니다. 요약 수치는 원자료를 참조하는 수식으로 계산합니다.
- [초기 프레임별 CSV](./test1_data.data.frames.csv)
- [개선 프레임별 CSV](./test5_data.data.frames.csv)
- [교통 시스템 개선 과정](https://github.com/hjcud/Shinjuku-Live-Street/blob/main/Docs/optimization.md)

CSV는 GitHub에서 표로 확인할 수 있고, 엑셀은 다운로드해 열 수 있습니다. 두 CSV의 모든 행과 열을 엑셀에 수록했습니다. 원본 Profiler `.data` 파일은 이 묶음에 포함하지 않았습니다.

## 결과

| 측정 항목 | 단위 | 초기 | 개선 | 변화율 |
| --- | --- | ---: | ---: | ---: |
| 표본 수 | 프레임 | 300 | 300 | 0.00% |
| CPU 프레임 시간 평균 | ms | 17.649 | 11.923 | -32.44% |
| CPU 프레임 시간 P95 | ms | 24.595 | 17.441 | -29.09% |
| PlayerLoop 평균 | ms | 9.034 | 5.455 | -39.62% |
| EditorLoop 평균 | ms | 8.225 | 6.130 | -25.48% |
| UdonManager.Update 평균 | ms | 0.836 | 1.089 | +30.21% |
| UdonBehaviour.ManagedUpdate 평균 | ms | 0.819 | 1.077 | +31.52% |
| Physics.Simulate 평균 | ms | 0.486 | 0.167 | -65.53% |
| 프레임당 GC 할당 평균 | KiB | 100.972 | 12.040 | -88.08% |
| BoxCast 마커 수 합계 | 회 | 2,967 | 600 | -79.78% |
| CheckBox 마커 수 합계 | 회 | 0 | 744 | 초기값이 0이므로 계산하지 않음 |

변화율은 `(개선 - 초기) / 초기`이며, 반올림 전 수치로 계산했습니다. 음수는 감소, 양수는 증가입니다. 평균 CPU 프레임 시간은 약 32.44% 줄었지만, 모든 세부 항목이 줄어든 것은 아닙니다.

개선 스냅샷에는 중앙 시뮬레이션, 차선 변경, 상태 동기화 개선과 센서 분산 등 여러 변경이 함께 들어 있습니다. 전체 차이를 물리 검사 방식 하나나 AI 제안 하나의 효과로 단정하지 않습니다. PlayerLoop와 EditorLoop 등 개별 마커의 시간도 중첩될 수 있으므로 합산하지 않습니다.

## 측정 조건과 집계 방법

| 항목 | 내용 |
| --- | --- |
| 장비 | Intel Core i5-13400F / GeForce RTX 3080 Ti 12GB / RAM 32GB |
| 소프트웨어 | Unity 2022.3.22f1 / Worlds SDK 3.8.1 / ClientSim |
| 실행 환경 | Unity Editor Play Mode |
| 부하 조건 | 활성 차량 10대, ClientSim 원격 플레이어 80명을 같은 지점에 배치 |
| 초기 자료 | `test1_data.data`, 차량별 계산과 물리 감지를 사용한 로컬 스냅샷 |
| 개선 자료 | `test5_data.data`, 중앙 시뮬레이션과 동기화 개선, 차선 기반 판단과 물리 감지, 센서 분산을 적용한 로컬 스냅샷 |
| 표본 범위 | 파일별 로드 후 인덱스 0~299, Main Thread의 300프레임 전체 |
| 재추출 | 2026-09-05 UTC, Unity의 `ProfilerDriver.LoadProfile`과 `GetRawFrameDataView` 사용 |
| 평균 | 프레임별 값의 산술 평균 |
| P95 | 오름차순 정렬 후 `ceil(0.95 × N)`번째 값. 300개 표본에서는 285번째 값 |
| 전처리 | 이상치 제거, 누락값 보간과 행 삭제 없이 집계 |

인원과 배치 조건은 당시 실험 기록과 기존 최적화 문서에 근거합니다. 캡처의 수치만으로 인원과 위치를 독립 확인한 것은 아닙니다. 파일명 순서가 아니라 당시 기록의 수치와 마커 구성을 대조해 두 파일을 비교 대상으로 정했습니다.

두 캡처의 `frameIndex`는 각각 로드한 뒤 부여된 인덱스입니다. 같은 번호가 같은 순간이나 이벤트를 뜻하지 않습니다. 따라서 초기와 개선 자료를 행별로 짝지어 차이를 계산하지 않았습니다.

당시 Deep Profile 설정, 별도 워밍업 시간과 반복 측정 횟수는 기록이 없습니다. 초기 코드는 공개 저장소 생성 전의 로컬 스냅샷이므로 두 커밋을 비교한 벤치마크도 아닙니다. 과거 기록의 일부를 재분석한 자료이며, 장시간 반복 측정이나 실제 빌드의 성능 검증을 대신하지 않습니다.

저장소의 `TrafficPlayerStressTestEditor.cs`는 이후 추가한 600프레임 반복 측정 도구입니다. 이 문서의 300프레임 자료를 생성한 도구와는 다릅니다.

## CSV 열 설명

| 열 | 의미 |
| --- | --- |
| `frameIndex` | 로드 후 프레임 인덱스. 원래 프로젝트의 프레임 번호가 아님 |
| `startMs` | 캡처에 기록된 프레임 시작 시간(ms). 두 캡처 사이의 공통 시간축이 아님 |
| `frameMs` | Profiler의 `frameTimeMs`. Editor 환경의 프레임 시간이며 GPU 시간이나 게임 코드만의 실행 시간이 아님 |
| `playerLoopMs` | 해당 프레임의 `PlayerLoop` 마커 시간 합계(ms) |
| `editorLoopMs` | 해당 프레임의 `EditorLoop` 마커 시간 합계(ms) |
| `udonManagerMs` | `VRC.Udon.dll!VRC.Udon::UdonManager.Update() [Invoke]` 마커 시간 합계(ms) |
| `udonBehaviourMs` | `UdonBehaviour.ManagedUpdate()` 마커 시간 합계(ms). 모든 Udon 실행 비용의 합계는 아님 |
| `physicsMs` | `Physics.Simulate` 마커 시간 합계(ms). 모든 물리 API의 총비용은 아님 |
| `gcBytes` | `GC.Alloc` 메타데이터의 바이트 합계. 메모리 점유량이 아니며, 요약표에서는 1024로 나눠 KiB로 표시 |
| `boxCastCalls` | 이름에 `BoxCast`가 포함된 마커 표본 수. 두 캡처에서 확인된 이름은 `Physics.BoxCast` |
| `checkBoxCalls` | 이름에 `CheckBox`가 포함된 마커 표본 수. 두 캡처에서 확인된 이름은 `Physics.CheckBox` |
| `phase` | `TrafficStress` 상태 마커. 이번 자료에서는 모두 빈칸 |
| `thread` | 추출 대상 스레드 이름. 이번 자료에서는 모두 `Main Thread` |

CSV의 수치 문자열은 추출 결과를 그대로 유지했습니다. 엑셀에는 숫자로 저장하고 화면에 표시하는 소수 자릿수만 제한했습니다. 원자료가 필요하면 CSV를 사용할 수 있습니다.

## 파일 식별 정보

SHA-256은 동일한 파일인지 확인하기 위한 값입니다.

| 파일 | SHA-256 |
| --- | --- |
| 초기 Profiler 원본 `test1_data.data` | `1B198DD346118C9A2C092C5AB00CA5AF940020CE8112A4F7ECC45E22A6258D15` |
| 개선 Profiler 원본 `test5_data.data` | `2BDE4BB862C9B10A656FD3FA83F1C61FEFF5381F8AF8707890A13183578A2FE6` |
| `test1_data.data.frames.csv` | `64a9794d25ea1f3d4a35be5476f9d3e90d47cca104cb9eba41779719451c2dd2` |
| `test5_data.data.frames.csv` | `189cad05f51925c44d4ac6cf2c2a82c990a36bdff5b62e548c824831a8c96ddd` |

추출에 사용한 Unity API는 [Unity 2022.3 RawFrameDataView](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Modules/ProfilerEditor/Public/RawFrameDataView.bindings.cs)에서 확인할 수 있습니다.
