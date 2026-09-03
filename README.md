<p align="center">
  <strong>한국어</strong> · <a href="./README.en.md">English</a> · <a href="./README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="https://api.vrchat.cloud/api/1/file/file_c6b519ec-141a-4edb-83f3-2fc3dc39c2e1/5/file" alt="신주쿠 라이브 스트리트 대표 이미지" width="900">
  </a>
</p>

<h1 align="center">Shinjuku Live Street</h1>

<p align="center">
  <strong>신주쿠의 밤거리에서 누구나 공연을 시작하고, 지나가던 사람이 자연스럽게 관객이 되는 VRChat 소셜 월드</strong>
</p>

<p align="center">
  노래와 연주, 대화와 단체사진이 거리 곳곳에서 이어집니다.
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>VRChat에서 방문하기</strong></a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live"><strong>#VRSJK 둘러보기</strong></a>
  ·
  <a href="#코드-구성"><strong>코드 살펴보기</strong></a>
</p>

---

## 어떤 월드인가요?

신주쿠 라이브 스트리트는 네온이 켜진 도심 한가운데서 공연자와 관객이 자연스럽게 만나는 **거리 공연 중심의 VRChat 소셜 월드**입니다.

정해진 무대 대신 거리 곳곳에서 공연이 시작됩니다. 지나가던 사람은 그 자리에 머물러 함께 공연을 즐기고, 공연이 끝난 뒤에는 대화와 단체사진으로 그날의 순간을 남깁니다.

<table>
  <tr>
    <td width="33%" align="center"><strong>1,693,697회</strong><br><sub>누적 방문</sub></td>
    <td width="33%" align="center"><strong>64,453명</strong><br><sub>즐겨찾기</sub></td>
    <td width="33%" align="center"><strong>최대 80명</strong><br><sub>수용 인원</sub></td>
  </tr>
</table>

<p align="center"><sub>VRChat 소셜 월드 · Unity / UdonSharp · 2인 제작 · 2025. 04. 04. 공개 · 최근 업데이트 2026. 08. 31.</sub></p>
<p align="center"><sub><a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat 공식 월드 정보</a> · 2026년 9월 3일 확인</sub></p>

## 거리 공연과 커뮤니티

<table>
  <tr>
    <td width="33%" valign="top"><strong>거리 곳곳이 무대</strong><br><sub>솔로 보컬과 악기 연주부터 밴드 공연까지, 공연자가 고른 자리에서 시작되는 공연</sub></td>
    <td width="33%" valign="top"><strong>지나가던 사람이 관객으로</strong><br><sub>처음 만난 연주 앞에 멈춰 함께 듣고 춤추고 응원하는 사람들</sub></td>
    <td width="33%" valign="top"><strong>공연 뒤에도 이어지는 기록</strong><br><sub>연주자와 관객이 나눈 대화와 단체사진으로 이어지는 #VRSJK 기록</sub></td>
  </tr>
</table>

사용자들이 남긴 실제 공연과 방문 기록은 [X의 #VRSJK 검색 결과](https://x.com/search?q=%23VRSJK&src=typed_query&f=live)에서 볼 수 있습니다.

## 최근 개발과 개선

공연 장비 동기화와 교통 시뮬레이션을 다시 설계했습니다. 여러 사용자가 동시에 접속한 상황에서 장비 상태를 안정적으로 동기화하고, 반복되던 CPU·물리·GC 부하를 줄였습니다.

### 공연 장비 동기화 — 배치부터 반납까지 상태를 일관되게 관리

스피커를 반납하거나 사용자가 자리를 떠난 뒤 일부 기능의 상태가 남고, 중간 입장자에게 이미 설치된 스피커가 보이지 않는 문제가 있었습니다.

<p align="center">
  <img src="./Docs/images/live-performance-sync.ko.svg" alt="공연 장비의 기존 동기화 문제와 개선 결과" width="900">
</p>

모든 사용자가 같은 장비 상태를 확인할 수 있고, 반납한 장비에는 이전 사용자의 설정이 남지 않도록 했습니다.

### 교통 시뮬레이션 — 차량별 반복 계산을 중앙 처리로 통합

차량마다 매 프레임 목적지 계산, `BoxCast`, Transform 이동과 직렬화를 실행해 차량과 사용자가 늘수록 CPU·물리·GC 부하가 함께 증가했습니다.

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

차량 10대와 ClientSim 원격 플레이어 80명이 한 지점에 모인 상태에서 초기 스냅샷과 최신 스냅샷의 동일한 300프레임을 비교했습니다. 평균 CPU 프레임 시간은 `17.65 ms → 11.92 ms`, 반복적으로 느린 프레임을 나타내는 P95는 `24.60 ms → 17.44 ms`로 줄었습니다. 물리 처리 시간은 65.3%, 프레임당 GC 할당은 88.1% 감소했습니다.

계산 주기는 `10 Hz`로 낮췄지만 차량 위치와 회전은 매 프레임 보간해 화면에서 부드럽게 움직이도록 했습니다.

[측정 조건과 문제별 적용 내용을 자세히 보기](./Docs/optimization.md)

## 모델과 렌더링 최적화

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="기본 렌더링과 와이어프레임 비교" width="900">
  <br>
  <sub>왼쪽: 기본 렌더링 · 오른쪽: 동일 카메라에서 촬영한 와이어프레임</sub>
</p>

환경 모델을 구역별로 나눠 카메라에 보이지 않는 영역은 오클루전 컬링으로 그리지 않게 했습니다. 정적 배칭을 적용하고 네온과 건물 조명은 베이크 조명으로 처리했습니다.

<table>
  <tr>
    <td align="center"><strong>246,921</strong><br><sub>삼각형</sub></td>
    <td align="center"><strong>240</strong><br><sub>환경 메시</sub></td>
    <td align="center"><strong>392</strong><br><sub>정적 배칭 오브젝트</sub></td>
    <td align="center"><strong>330</strong><br><sub>오클루더</sub></td>
    <td align="center"><strong>2</strong><br><sub>메시 콜라이더</sub></td>
  </tr>
</table>

약 220개 메시에 베이크 조명을 적용했고, 4096 라이트맵 3장과 512 라이트맵 1장을 사용합니다.

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

이 저장소만으로 월드를 실행할 수는 없습니다. 직접 작성한 C#·UdonSharp 코드와 문서만 공개하며, Unity 씬과 프리팹, 모델, 이미지, 음원, 영상, 머티리얼, 애니메이션, 셰이더, `.meta` 파일은 포함하지 않습니다.

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

</details>

## 저작권

이 저장소의 자체 작성 코드에는 별도의 오픈 소스 라이선스를 부여하지 않았습니다. 별도 허가 없이 코드를 사용·수정·재배포할 수 없습니다. 외부 구성요소와 월드에 사용된 에셋의 권리는 각 원 저작자에게 있습니다.

## 팀

| 구성원 | 담당 |
| --- | --- |
| [hjcud](https://github.com/hjcud) | Unity·UdonSharp 시스템 개발 및 최적화 |
| [Artistoid](https://github.com/Artistoid) · [X @Artistoid_VRC](https://x.com/Artistoid_VRC) | 기획 · 그래픽 · 3D 모델링 |

---

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat 월드</a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live">#VRSJK</a>
  ·
  <a href="https://github.com/hjcud/Shinjuku-Live-Street">GitHub</a>
</p>
