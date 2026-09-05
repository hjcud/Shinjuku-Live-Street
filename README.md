<p align="center">
  <strong>한국어</strong> · <a href="./README.ja.md">日本語</a> · <a href="./README.en.md">English</a>
</p>

<div align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="./Docs/images/shinjuku-live-street-hero.png" alt="신주쿠 라이브 스트리트 대표 이미지" width="900">
  </a>
  <h1>Shinjuku Live Street</h1>
</div>

<p align="center"><strong>실행 가능한 Unity 프로젝트는 포함되어 있지 않습니다. 작성한 코드와 주요 개발 내용을 정리해 공개하는 저장소입니다.</strong></p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./Docs/images/world-intro.ko.svg">
    <img src="./Docs/images/world-intro.ko.light.svg" alt="신주쿠의 밤거리에서 누구나 공연을 시작하고, 지나가던 사람이 자연스럽게 관객이 되는 VRChat 소셜 월드" width="900">
  </picture>
</p>

<table align="center">
  <tr>
    <td width="180" align="center"><img src="./Docs/images/metric-visits.svg" alt="" width="28"><br><strong>1,693,697회</strong><br><sub>누적 방문</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-favorites.svg" alt="" width="28"><br><strong>64,453명</strong><br><sub>즐겨찾기</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-capacity.svg" alt="" width="28"><br><strong>최대 80명</strong><br><sub>수용 인원</sub></td>
  </tr>
</table>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>VRChat에서 월드 열기 ↗</strong></a>
</p>

<p align="center"><sub>VRChat 소셜 월드 · Unity / UdonSharp · 2인 제작<br>방문·즐겨찾기 수치 · 2026년 9월 3일 기준</sub></p>

## 거리 공연과 커뮤니티

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./Docs/images/community-highlights.ko.svg">
    <img src="./Docs/images/community-highlights.ko.light.svg" alt="원하는 곳에서 시작하는 공연과 지나가던 사람도 관객이 되는 경험" width="900">
  </picture>
</p>

<p align="center">
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live"><strong>#VRSJK에서 실제 공연과 방문 기록 보기 ↗</strong></a>
</p>

<p align="center">
  <img src="./Docs/images/community-gallery.png" alt="세 장의 거리 공연 사진과 추후 추가할 커뮤니티 사진 영역" width="900">
</p>

https://github.com/user-attachments/assets/9ec884f0-df47-49f0-b39f-327cdfda0ede

<p align="center"><sub>영상 제공: <a href="https://x.com/KixiVRC">@KixiVRC</a> · 사진 제공: <a href="https://x.com/KixiVRC">@KixiVRC</a>, <a href="https://x.com/aoiaoitabi">@aoiaoitabi</a>, <a href="https://x.com/taque_0409">@taque_0409</a>, <a href="https://x.com/ponhayate_vrc">@ponhayate_vrc</a></sub></p>

---

<p align="center">
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues"><img src="./Docs/images/issue-planned-work.ko.svg" alt="예정된 작업 확인하기 — 진행 중인 작업과 개선 계획" width="49%"></a>
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues/new/choose"><img src="./Docs/images/issue-feedback.ko.svg" alt="의견 남기기 — 버그 제보와 기능·개선 제안" width="49%"></a>
</p>

---

## 최근 개발과 개선

<sub>2026년 9월 3일 기준</sub>

공연 장비 동기화와 교통 시뮬레이션을 다시 설계했습니다. 여러 사용자가 동시에 접속한 상황에서도 장비 상태가 안정적으로 동기화되도록 했으며, CPU 프레임 시간과 물리 연산 시간, 프레임당 GC 할당량을 줄였습니다.

### 공연 장비 동기화 — 배치부터 반납까지 상태를 일관되게 관리

스피커를 반납하거나 사용자가 자리를 떠난 뒤 일부 기능의 상태가 남고, 중간 입장자에게 이미 설치된 스피커가 보이지 않는 문제가 있었습니다.

<p align="center">
  <img src="./Docs/images/live-performance-sync.ko.svg" alt="공연 장비의 기존 동기화 문제와 개선 결과" width="900">
</p>

모든 사용자가 같은 장비 상태를 확인할 수 있고, 반납한 장비에는 이전 사용자의 설정이 남지 않도록 했습니다.

### 교통 시뮬레이션 — 차량별 반복 계산을 중앙 처리로 통합

차량마다 매 프레임 목적지 계산, `BoxCast`, Transform 이동과 직렬화를 실행해 차량과 사용자가 늘수록 CPU 프레임 시간과 물리 연산 시간, GC 할당량이 함께 증가했습니다.

<p align="center">
  <img src="./Docs/images/traffic-system-architecture.ko.svg" alt="에디터, 교통 소유자, 네트워크와 원격 사용자로 이어지는 교통 상태 처리 구조" width="900">
</p>

교통 소유자가 차량 10대의 주행 상태를 한 곳에서 계산하고, 차량 상태는 64비트로 압축해 전송합니다. 원격 클라이언트는 같은 차선 데이터에서 차량을 복원하고 매 프레임 보간해 끊김을 줄였습니다.

<details>
<summary><strong>실행 중 디버그 화면 보기</strong></summary>

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="실행 중인 교통 시스템의 차선과 차량 디버그 화면" width="900">
  <br>
  <sub>차선 데이터와 차량별 점유 영역·예상 위치·장애물 센서 범위</sub>
</p>

</details>

### 성능 개선 결과

![교통 시스템 초기 스냅샷과 최신 스냅샷의 Unity Profiler 비교](./Docs/images/traffic-performance-comparison.ko.svg)

<p align="center"><sub>Unity Editor·ClientSim에서 동일 조건으로 비교한 결과이며, 실제 VRChat 인스턴스 성능과는 다를 수 있습니다.</sub></p>

차량 10대와 ClientSim으로 재현한 원격 플레이어 80명을 같은 지점에 배치하고, Unity Editor에서 초기·최신 상태를 각각 300프레임 측정했습니다. 평균 CPU 프레임 시간은 `17.65 ms → 11.92 ms`, P95 프레임 시간은 `24.60 ms → 17.44 ms`로 감소했습니다. 물리 처리 시간은 65.3%, 프레임당 GC 할당은 88.1% 줄었습니다. 차량 위치와 회전은 매 프레임 보간해 계산 사이에서도 움직임이 끊기지 않도록 했습니다.

<p align="center">
  <a href="./Docs/optimization.md"><strong>측정 조건과 문제별 적용 내용을 자세히 보기 →</strong></a>
</p>

---

## 모델과 렌더링 최적화

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.webp" alt="기본 렌더링과 와이어프레임 비교" width="900">
  <br>
  <sub>왼쪽: 기본 렌더링 · 오른쪽: 동일 카메라에서 촬영한 와이어프레임</sub>
</p>

환경 모델을 구역별로 분리하고, 오클루전 컬링·정적 배칭·베이크 조명을 적용해 실시간 처리 범위를 줄였습니다.

<table align="center">
  <tr>
    <td width="260" align="center"><strong>모델 구성</strong><br><sub>삼각형 246,921개<br>환경 메시 240개<br>메시 콜라이더 2개</sub></td>
    <td width="260" align="center"><strong>렌더링 처리</strong><br><sub>정적 배칭 대상 392개<br>오클루더 설정 330개</sub></td>
    <td width="260" align="center"><strong>베이크 조명</strong><br><sub>적용 메시 약 220개<br>4096×4096 3장<br>512×512 1장</sub></td>
  </tr>
</table>

## 코드 구성

| 영역 | 주요 파일 | 역할 |
| --- | --- | --- |
| 공연 장비 | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | 스피커 배치, 검증, 소유권, 중간 입장자 동기화와 초기화 |
| 무대 음성 | [`VoiceRange.cs`](./Assets/Shinjuku%20Udon/Speaker/VoiceRange.cs) | 공연자의 음성 거리와 음량 공유 |
| 공유 상호작용 | [`ObjectGlobalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectGlobalToggle.cs), [`ObjectLocalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectLocalToggle.cs) | 전역 상태와 개인 상태 분리 |
| 교통 실행 | [`TrafficSimulationManager.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficSimulationManager.cs) | 차량 계산, 상태 압축, 전송과 원격 차량 복원 |
| 차선 데이터 | [`TrafficLaneDatabase.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficLaneDatabase.cs) | 베이크된 차선 정보 조회와 차량 자세 복원 |
| 제작 도구 | [`TrafficLaneBakerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficLaneBakerEditor.cs), [`TrafficSimulationManagerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficSimulationManagerEditor.cs) | 차선 베이킹, 설정 검사와 시각화 |
| 월드 기능 | [`PosterSlide.cs`](./Assets/Shinjuku%20Udon/Posters/PosterSlide.cs), [`PortalToggle.cs`](./Assets/Shinjuku%20Udon/Portal/PortalToggle.cs), [`CollisionTeleport.cs`](./Assets/Shinjuku%20Udon/Teleport/CollisionTeleport.cs) | 포스터 전환, 포털과 이동 처리 |

## 저장소 안내

이 저장소는 월드를 실행할 수 있는 완전한 Unity 프로젝트가 아닙니다. 직접 작성한 C#·UdonSharp 코드, 기술 문서와 README용 자체 제작 이미지만 공개합니다. 월드 구동에 필요한 Unity 씬·Prefab·모델·텍스처·음원·영상·머티리얼·애니메이션·셰이더·`.meta` 파일과 외부 에셋은 포함하지 않습니다.

<details>
<summary><strong>사용한 SDK·패키지·외부 구성요소</strong></summary>

### 개발 환경

- Unity `2022.3.22f1`
- VRChat SDK - Worlds `3.8.1`
- UdonSharp
- TextMesh Pro

### 월드에서 사용한 외부 구성요소

- Topaz Chat `0.1.6`
- lilToon `1.10.3`
- VRWorldToolkit `3.2.1`
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
- Atelier Rayrell, RIONESTA, Zelkova Tree 및 기타 모델·환경 리소스

외부 구성요소는 이 저장소에 포함하지 않습니다. 각 항목의 저작권과 라이선스는 원 저작자 및 배포처의 정책을 따릅니다.

[외부 라이선스 고지 보기 →](./Docs/THIRD_PARTY_NOTICES.md)

</details>

## 저작권 및 이용 조건

> [!IMPORTANT]
> 이 저장소는 오픈 소스가 아닌 **소스 공개형(source-available) 저장소**입니다. GitHub에서의 열람·포크를 넘어 코드를 복제·수정·재배포하거나 상업적으로 이용하려면 사전 서면 허가가 필요합니다.

[저작권 및 이용 조건 자세히 보기](./COPYRIGHT.md) · [이용 허가 요청하기](https://github.com/hjcud/Shinjuku-Live-Street/issues/new?template=7-usage-permission-request.yml)

## 팀

| 구성원 | 담당 |
| --- | --- |
| [Artistoid](https://github.com/Artistoid) · [X @Artistoid_VRC](https://x.com/Artistoid_VRC) | 기획 · 그래픽 · 3D 모델링 |
| [hjcud](https://github.com/hjcud) | Unity·UdonSharp 시스템 개발 및 최적화 |
