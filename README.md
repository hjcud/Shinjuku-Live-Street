<p align="center">
  <strong>한국어</strong> · <a href="./README.en.md">English</a> · <a href="./README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="https://api.vrchat.cloud/api/1/file/file_c6b519ec-141a-4edb-83f3-2fc3dc39c2e1/5/file" alt="신주쿠 라이브 스트리트 대표 이미지" width="800">
  </a>
</p>

<h1 align="center">신주쿠 라이브 스트리트</h1>

<p align="center">
  <strong>신주쿠의 밤거리에서 누구나 공연을 시작하고, 누구나 관객이 되는 VRChat 월드</strong>
</p>

<p align="center">
  노래와 연주를 들려주고 싶은 사람, 우연히 발걸음을 멈춘 사람, 친구와 밤거리를 걷고 싶은 사람이 한 공간에서 만납니다.
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

신주쿠 라이브 스트리트는 신주쿠의 밤을 채우는 네온과 소리, 끊임없이 오가는 사람들을 VRChat 안에 옮긴 **거리 공연 중심의 소셜 월드**입니다.

정해진 무대만 바라보는 공연장이 아닙니다. 사용자가 직접 자리를 정해 공연을 시작하면, 거리를 걷던 사람이 자연스럽게 관객이 됩니다. 공연이 끝난 뒤에는 서로 이야기를 나누고 사진을 찍으며 그날의 순간을 함께 남깁니다.

<table>
  <tr>
    <td width="33%" align="center"><strong>1,693,697회</strong><br><sub>누적 방문</sub></td>
    <td width="33%" align="center"><strong>64,453명</strong><br><sub>즐겨찾기</sub></td>
    <td width="33%" align="center"><strong>최대 80명</strong><br><sub>수용 인원</sub></td>
  </tr>
  <tr>
    <td align="center"><strong>2025. 04. 04.</strong><br><sub>Public 공개</sub></td>
    <td align="center"><strong>2026. 08. 31.</strong><br><sub>최근 월드 업데이트</sub></td>
    <td align="center"><strong>Version 207</strong><br><sub>Unity · UdonSharp</sub></td>
  </tr>
</table>

<p align="center"><sub><a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat 공식 월드 정보</a> · 2026년 9월 3일 확인 · 방문 및 즐겨찾기 수는 이후 달라질 수 있습니다.</sub></p>

## 이곳에서 벌어지는 일

<!-- SCREENSHOT_SLOT: 실제 공연 장면 3장을 외부 URL로 연결한 뒤 아래 카드 위에 배치합니다. 저장소에는 이미지 파일을 추가하지 않습니다. -->

<table>
  <tr>
    <td width="33%" valign="top"><strong>거리 곳곳이 무대</strong><br><sub>솔로 보컬과 악기 연주부터 밴드 공연까지, 공연자가 고른 자리가 무대가 됩니다.</sub></td>
    <td width="33%" valign="top"><strong>지나가던 사람이 관객으로</strong><br><sub>처음 만난 연주 앞에 멈춰 함께 듣고, 춤추고, 응원합니다.</sub></td>
    <td width="33%" valign="top"><strong>공연 뒤에도 이어지는 기록</strong><br><sub>연주자와 관객이 나눈 대화와 단체사진이 #VRSJK로 이어집니다.</sub></td>
  </tr>
</table>

공연을 보기 위해 찾아오는 사람도 있지만, 친구를 따라 들어왔다가 모르는 사람의 노래 앞에 머무는 사람도 있습니다. 사용자들이 남긴 실제 공연과 방문 기록은 [X의 #VRSJK 검색 결과](https://x.com/search?q=%23VRSJK&src=typed_query&f=live)에서 볼 수 있습니다.

## 최근 활동

최근에는 공연 장비를 여러 사람이 사용할 때 생기던 상태 차이와 교통 시스템에서 반복되던 계산·전송 문제를 해결했습니다. 같은 조건에서 다시 확인할 수 있도록 제작 도구와 테스트 과정도 함께 만들었습니다.

### 공연 시스템의 문제와 해결

이동식 스피커에는 위치뿐 아니라 소유자, 음성 크기, 화면, 그림 도구와 미디어 상태가 함께 연결됩니다. 스피커를 반납하거나 사용자가 자리를 떠난 뒤 일부 상태가 남았고, 나중에 들어온 사용자에게 이미 설치된 스피커가 보이지 않는 문제도 있었습니다.

~~~mermaid
flowchart LR
    A["설치 위치 미리보기<br/>Desktop · VR"] --> B["설치 조건 확인<br/>3 m · 경사 30°"]
    B --> C["소유권 설정 후<br/>위치·방향 동기화"]
    C --> D["중간 참가자에게<br/>현재 상태 재전송"]
    D --> E["반납·이탈 감지<br/>연결 상태 전체 초기화"]
~~~

설치 전에 거리, 경사와 남은 수량을 확인하고 소유권을 정한 뒤 상태를 전송합니다. 반납하거나 5 m 이상 멀어지면 연결된 기능을 함께 초기화합니다. 모두가 공유하는 화면·그림 도구와 개인별 거울 상태도 전역·로컬 컴포넌트로 나누었습니다.

### 교통 시스템의 문제와 해결

교통은 공연 뒤에서 도시를 계속 움직이게 만드는 배경입니다. 초기 시스템은 차량마다 매 프레임 목적지 계산, `BoxCast`, Transform 이동과 직렬화를 따로 실행했습니다. 차량 수가 늘면 같은 계산과 전송 요청도 함께 늘었고, 사용자 수가 많은 테스트에서는 일정한 간격으로 프레임 시간이 튀었습니다.

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="실행 중인 교통 시스템의 차선과 차량 디버그 화면" width="900">
  <br>
  <sub>실행 중인 차선 데이터와 차량별 점유 영역·예상 위치·장애물 센서 범위</sub>
</p>

~~~mermaid
flowchart LR
    subgraph BEFORE["이전 구조"]
        direction TB
        B1["차량마다 매 프레임 판단"]
        B2["차량마다 BoxCast"]
        B3["차량마다 이동·직렬화"]
        B1 --> B2 --> B3
    end
    subgraph AFTER["현재 구조"]
        direction TB
        A1["에디터에서 차선 데이터 생성"]
        A2["소유자 한 명이 차량 10대 계산"]
        A3["프레임당 차량 2대 센서 갱신"]
        A4["차량당 64비트 압축"]
        A5["0.25초마다 전송·원격 보간"]
        A1 --> A2 --> A3 --> A4 --> A5
    end
    BEFORE ==> AFTER
~~~

앞차와 신호는 차선 진행 거리로 판단합니다. 원격 사용자는 같은 차선 데이터 위에 차량을 복원하고, 패킷이 늦을 때만 최대 0.15초 앞의 위치를 예측합니다. 차선 변경, 긴급 회피와 후진 복구 상태도 차량당 64비트 안에 함께 기록합니다.

### Unity Profiler로 확인한 개선

![교통 시스템 초기 스냅샷과 최신 스냅샷의 Unity Profiler 비교](./Docs/images/traffic-performance-comparison.svg)

차량 10대와 ClientSim 원격 플레이어 80명이 한 지점에 모인 상태에서 초기 스냅샷과 최신 스냅샷의 동일한 300프레임을 비교했습니다. 평균 CPU 프레임 시간은 `17.65 ms → 11.92 ms`, 반복적으로 느린 프레임을 나타내는 P95는 `24.60 ms → 17.44 ms`로 줄었습니다. 물리 처리 시간은 65.3%, 프레임당 GC 할당은 88.1% 감소했습니다.

`10 Hz`는 주행 판단 주기일 뿐 화면 갱신 주기가 아닙니다. 권한자의 차량은 이전 상태와 현재 상태 사이를, 원격 차량은 수신한 스냅샷 사이를 **매 렌더 프레임 보간**합니다. 모델, 머티리얼과 렌더링 설정을 낮추지 않고 CPU 부하를 줄였으며, GPU 프레임 시간과 화질은 이번 비교에 포함하지 않았습니다.

[측정 조건과 문제별 적용 내용을 자세히 보기](./Docs/optimization.md)

## 모델과 렌더링 구성

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="기본 렌더링과 와이어프레임 비교" width="900">
  <br>
  <sub>왼쪽: 기본 렌더링 · 오른쪽: 동일 카메라에서 촬영한 와이어프레임</sub>
</p>

건물과 간판을 하나의 큰 메시로 합치면 가려진 골목과 오브젝트까지 함께 그려질 수 있습니다. 반대로 너무 작게 나누면 렌더러와 머티리얼 호출이 늘어납니다. 환경 모델은 신주쿠 거리의 형태를 살리면서 구역별 가림 처리가 가능하도록 나누었습니다.

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

최적화 전 모델 설정은 남아 있지 않아, 위 숫자는 개선율이 아닌 현재 적용값으로 구분했습니다.

## 코드 구성

| 영역 | 주요 파일 | 역할 |
| --- | --- | --- |
| 공연 장비 | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | 스피커 배치, 검증, 소유권, 참가자 동기화와 초기화 |
| 무대 음성 | [`VoiceRange.cs`](./Assets/Shinjuku%20Udon/Speaker/VoiceRange.cs) | 공연자의 음성 거리와 음량 공유 |
| 공유 상호작용 | [`ObjectGlobalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectGlobalToggle.cs), [`ObjectLocalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectLocalToggle.cs) | 전역 상태와 개인 상태 분리 |
| 교통 실행 | [`TrafficSimulationManager.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficSimulationManager.cs) | 차량 계산, 상태 압축, 전송과 원격 재생 |
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
| [@Artistoid_VRC](https://x.com/Artistoid_VRC) | 신주쿠 거리·환경 3D 모델링 |

---

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat 월드</a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live">#VRSJK</a>
  ·
  <a href="https://github.com/hjcud/Shinjuku-Live-Street">GitHub</a>
</p>
