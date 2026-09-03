<p align="center">
  <a href="./README.md">한국어</a> · <a href="./README.en.md">English</a> · <strong>日本語</strong>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="https://api.vrchat.cloud/api/1/file/file_c6b519ec-141a-4edb-83f3-2fc3dc39c2e1/5/file" alt="新宿ライブストリートのメインビジュアル" width="800">
  </a>
</p>

<h1 align="center">新宿ライブストリート</h1>

<p align="center">
  <strong>新宿の夜道で、誰もがライブを始められ、通りすがりの誰もが観客になれるVRChatワールド</strong>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>VRChatで訪れる</strong></a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live"><strong>#VRSJKを見る</strong></a>
  ·
  <a href="#コード構成"><strong>コードを見る</strong></a>
</p>

---

## どんなワールド？

新宿ライブストリートは、新宿の夜に感じる光や音、人の密度をVRChatに持ち込んだ、**路上ライブを中心とするソーシャルワールド**です。

決められたステージを眺めるだけの会場ではありません。ユーザー自身が場所を選んでライブを始めると、街を歩いていた人がそのまま観客になります。演奏が終われば、出演者と観客が言葉を交わし、写真を撮り、その夜の出来事をコミュニティへ持ち帰ります。

<table>
  <tr>
    <td width="33%" align="center"><strong>1,693,697回</strong><br><sub>累計訪問数</sub></td>
    <td width="33%" align="center"><strong>64,453人</strong><br><sub>お気に入り</sub></td>
    <td width="33%" align="center"><strong>最大80人</strong><br><sub>定員</sub></td>
  </tr>
  <tr>
    <td align="center"><strong>2025. 04. 04.</strong><br><sub>Public公開</sub></td>
    <td align="center"><strong>2026. 08. 31.</strong><br><sub>最終ワールド更新</sub></td>
    <td align="center"><strong>Version 207</strong><br><sub>Unity · UdonSharp</sub></td>
  </tr>
</table>

<p align="center"><sub><a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat公式ワールド情報</a> · 2026年9月3日確認 · 訪問数とお気に入り数は今後変わる場合があります。</sub></p>

## ここで起きていること

<!-- SCREENSHOT_SLOT: 実際のライブ写真3枚を外部URLで参照し、下のカード上部に配置します。画像ファイルはリポジトリへ追加しません。 -->

<table>
  <tr>
    <td width="33%" valign="top"><strong>街のどこでもステージに</strong><br><sub>ソロボーカルや楽器演奏からバンドライブまで、出演者が選んだ場所からライブが始まります。</sub></td>
    <td width="33%" valign="top"><strong>通りすがりの人が観客に</strong><br><sub>初めて出会った演奏に足を止め、一緒に聴き、踊り、声援を送ります。</sub></td>
    <td width="33%" valign="top"><strong>ライブのあとも続く記録</strong><br><sub>出演者と観客の会話や集合写真が、#VRSJKを通じてワールドの外へ続きます。</sub></td>
  </tr>
</table>

ライブを目当てに訪れる人もいれば、フレンドについて来た先で、知らない誰かの歌に足を止める人もいます。実際のライブや訪問の記録は、[Xの#VRSJK検索結果](https://x.com/search?q=%23VRSJK&src=typed_query&f=live)で確認できます。

## 最近の取り組み

最近は、ライブ機材を複数人で使うときに生じていた状態のずれと、交通システムで繰り返されていた計算・送信処理を解決しました。同じ条件を再現して確認できるよう、制作ツールとテスト手順も同時に整備しています。

### ライブシステムの問題と解決

移動式スピーカーには、位置だけでなく所有者、音声音量、画面、描画ツール、メディアの状態が連動します。返却後や利用者が離れたあとに一部の状態が残り、途中参加したユーザーには設置済みのスピーカーが見えない問題がありました。

~~~mermaid
flowchart LR
    A["設置位置をプレビュー<br/>Desktop · VR"] --> B["設置条件を確認<br/>3 m · 傾斜30°"]
    B --> C["所有権を設定して<br/>位置・向きを同期"]
    C --> D["途中参加者へ<br/>現在の状態を再送信"]
    D --> E["返却・距離を検知<br/>連動する状態を初期化"]
~~~

設置前に距離、傾斜、残り台数を確認し、所有権を設定してから状態を同期します。返却するか5 m以上離れた場合は、連動する機能をまとめて初期化します。全員が共有する画面・描画ツールと、個人別のミラー状態もグローバル用・ローカル用のコンポーネントに分けました。

### 交通システムの問題と解決

交通はライブの奥で街を動かし続ける背景です。初期システムでは、各車両が毎フレーム目的地計算、`BoxCast`、Transform移動、直列化要求を個別に実行していました。車両が増えるほど同じ処理も増え、多人数テストでは一定間隔でフレーム時間が跳ね上がりました。

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="実行中の交通システムにおけるレーンと車両のデバッグ画面" width="900">
  <br>
  <sub>実行中のベイク済みレーン、車両の占有範囲、予測位置、障害物センサー範囲</sub>
</p>

~~~mermaid
flowchart LR
    subgraph BEFORE["以前の構成"]
        direction TB
        B1["各車両が毎フレーム判断"]
        B2["各車両がBoxCastを実行"]
        B3["各車両が移動・直列化"]
        B1 --> B2 --> B3
    end
    subgraph AFTER["現在の構成"]
        direction TB
        A1["エディタでレーンデータを生成"]
        A2["1人の所有者が10台を計算"]
        A3["1フレーム2台のセンサーを更新"]
        A4["1台64ビットに圧縮"]
        A5["0.25秒ごとに送信・リモート補間"]
        A1 --> A2 --> A3 --> A4 --> A5
    end
    BEFORE ==> AFTER
~~~

前方車両と信号はレーン上の進行距離で判断します。リモート側は同じレーンデータ上に車両を復元し、パケットが遅れた場合のみ最大0.15秒先まで予測します。車線変更、緊急回避、後退復帰も1台分の64ビット内に記録します。

### Unity Profilerで確認した改善

![交通システムの初期スナップショットと最新スナップショットのUnity Profiler比較](./Docs/images/traffic-performance-comparison.svg)

車両10台とClientSimのリモートプレイヤー80人を1地点に集め、初期スナップショットと最新スナップショットの同じ300フレームを比較しました。平均CPUフレーム時間は`17.65 ms → 11.92 ms`、繰り返し発生する遅いフレームを示すP95は`24.60 ms → 17.44 ms`まで減少しました。物理処理時間は65.3%、1フレームあたりのGC割り当ては88.1%削減しています。

`10 Hz`は走行判断の周期であり、画面の更新周期ではありません。権限側の車両はシミュレーション状態の間を、リモート車両は受信したスナップショットの間を**レンダーフレームごとに補間**します。モデル、マテリアル、レンダリング設定は下げていません。GPUフレーム時間と画質は今回の比較には含まれていません。

[測定条件と問題ごとの実装を詳しく見る](./Docs/optimization.ja.md)

## モデルと描画の構成

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="通常レンダリングとワイヤーフレームの比較" width="900">
  <br>
  <sub>左：通常レンダリング · 右：同じカメラから撮影したワイヤーフレーム</sub>
</p>

建物と看板を一つの大きなメッシュへまとめると、隠れた路地やオブジェクトまで一緒に描画されやすくなります。反対に細かく分けすぎると、レンダラーとマテリアルの呼び出しが増えます。環境モデルは新宿の街並みを残しながら、区域ごとにカリングできる構成にしました。

<table>
  <tr>
    <td align="center"><strong>246,921</strong><br><sub>三角形</sub></td>
    <td align="center"><strong>240</strong><br><sub>環境メッシュ</sub></td>
    <td align="center"><strong>392</strong><br><sub>スタティックバッチ対象</sub></td>
    <td align="center"><strong>330</strong><br><sub>オクルーダー</sub></td>
    <td align="center"><strong>2</strong><br><sub>Mesh Collider</sub></td>
  </tr>
</table>

約220メッシュにベイク照明を適用し、4096ライトマップ3枚と512ライトマップ1枚を使用しています。

最適化前のモデル設定は残っていないため、上記は改善率ではなく現在の設定値として区別しています。

## コード構成

| 分野 | 主なファイル | 役割 |
| --- | --- | --- |
| ライブ機材 | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | スピーカー設置、検証、所有権、途中参加者との同期、初期化 |
| ステージ音声 | [`VoiceRange.cs`](./Assets/Shinjuku%20Udon/Speaker/VoiceRange.cs) | 出演者の音声距離と音量の共有 |
| 共有操作 | [`ObjectGlobalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectGlobalToggle.cs), [`ObjectLocalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectLocalToggle.cs) | グローバル状態とローカル状態の分離 |
| 交通処理 | [`TrafficSimulationManager.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficSimulationManager.cs) | 車両計算、状態圧縮、送信、リモート再生 |
| レーンデータ | [`TrafficLaneDatabase.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficLaneDatabase.cs) | ベイク済みレーンの参照と車両姿勢の復元 |
| 制作ツール | [`TrafficLaneBakerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficLaneBakerEditor.cs), [`TrafficSimulationManagerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficSimulationManagerEditor.cs) | レーンのベイク、検証、可視化 |
| ワールド機能 | [`PosterSlide.cs`](./Assets/Shinjuku%20Udon/Posters/PosterSlide.cs), [`PortalToggle.cs`](./Assets/Shinjuku%20Udon/Portal/PortalToggle.cs), [`CollisionTeleport.cs`](./Assets/Shinjuku%20Udon/Teleport/CollisionTeleport.cs) | ポスター切り替え、ポータル、テレポート |

## リポジトリについて

このリポジトリだけでワールドを実行することはできません。公開しているのは、自作のC#・UdonSharpコードとドキュメントのみです。UnityのScene、Prefab、モデル、画像、音声、動画、Material、Animation、Shader、`.meta`ファイルは含まれていません。

<details>
<summary><strong>使用したSDK・パッケージ・外部コンポーネント</strong></summary>

### 開発環境

- Unity `2022.3.22f1`
- VRChat SDK - Worlds `3.8.1`
- UdonSharp
- TextMesh Pro

### ワールドで使用した外部コンポーネント

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
- Atelier Rayrell、RIONESTA、Zelkova Tree、その他クリエイターによるモデル・環境素材

外部コンポーネントはこのリポジトリに含まれていません。著作権およびライセンスは、それぞれの作者・配布元の規約に従います。

</details>

## 著作権

このリポジトリの自作コードには、オープンソースライセンスを付与していません。別途許可なく使用、改変、再配布することはできません。外部コンポーネントおよびワールド内アセットの権利は、それぞれの作者に帰属します。

## チーム

| メンバー | 担当 |
| --- | --- |
| [hjcud](https://github.com/hjcud) | Unity・UdonSharpシステムの開発・最適化 |
| [@Artistoid_VRC](https://x.com/Artistoid_VRC) | 新宿の街並み・環境の3Dモデリング |

---

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChatワールド</a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live">#VRSJK</a>
  ·
  <a href="https://github.com/hjcud/Shinjuku-Live-Street">GitHub</a>
</p>
