<p align="center">
  <a href="./README.md">한국어</a> · <a href="./README.en.md">English</a> · <strong>日本語</strong>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="https://api.vrchat.cloud/api/1/file/file_c6b519ec-141a-4edb-83f3-2fc3dc39c2e1/5/file" alt="新宿ライブストリートのメインビジュアル" width="900">
  </a>
</p>

<h1 align="center">新宿ライブストリート</h1>

<p align="center">
  <strong>新宿の夜道で誰もがライブを始められ、通りすがりの人が自然と観客になるVRChatソーシャルワールド</strong>
</p>

<p align="center">
  歌や演奏が街のあちこちで始まり、人々が自然に集まります。
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>VRChatでワールドを開く ↗</strong></a>
</p>

---

## どんなワールド？

Shinjuku Live Streetは、新宿の街を舞台にした**ストリートライブ中心のVRChatソーシャルワールド**です。

決まったステージはなく、街のあちこちでライブを始められます。通りすがりの人も足を止め、その場で一緒に楽しめます。

<table align="center">
  <tr>
    <td width="180" align="center"><img src="./Docs/images/metric-visits.svg" alt="" width="28"><br><strong>1,693,697回</strong><br><sub>累計訪問数</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-favorites.svg" alt="" width="28"><br><strong>64,453人</strong><br><sub>お気に入り</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-capacity.svg" alt="" width="28"><br><strong>最大80人</strong><br><sub>定員</sub></td>
  </tr>
</table>

<p align="center"><sub>VRChatソーシャルワールド · Unity / UdonSharp · 2人制作</sub></p>
<p align="center"><sub>訪問数・お気に入り数 · 2026年9月3日時点</sub></p>

## ストリートライブとコミュニティ

<table>
  <tr>
    <td width="50%" valign="top"><strong>好きな場所で始めるライブ</strong><br><sub>ボーカルや楽器演奏、バンドライブで街のあちこちをステージとして活用</sub></td>
    <td width="50%" valign="top"><strong>通りすがりの人も観客に</strong><br><sub>偶然出会ったライブに足を止め、一緒に聴いて楽しむ体験</sub></td>
  </tr>
</table>

実際のライブや訪問の記録は、[#VRSJKで見る ↗](https://x.com/search?q=%23VRSJK&src=typed_query&f=live)

<p align="center">
  <img src="./Docs/images/community-gallery-placeholder.svg" alt="ストリートライブとコミュニティの実際の場面を入れる画像領域" width="900">
</p>

---

<p align="center">
  <strong>今後の改善と提案</strong><br><br>
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues"><strong>今後の作業を見る →</strong></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues/new"><strong>意見を送る →</strong></a>
</p>

---

## 最近の開発と改善

ライブ機材の同期と交通シミュレーションを再設計しました。多くのユーザーが同時に接続しても機材の状態がずれないようにし、繰り返し発生していたCPU・物理・GCの負荷を削減しています。

### ライブ機材の同期 — 設置から返却まで状態を一貫して管理

スピーカーの返却後や利用者が離れたあとに一部機能の状態が残り、途中参加したユーザーには設置済みのスピーカーが見えない問題がありました。

<p align="center">
  <img src="./Docs/images/live-performance-sync.ja.svg" alt="ライブ機材で発生していた同期の問題と改善結果" width="900">
</p>

全員が同じ機材の状態を確認でき、返却後の機材に前の利用者の設定が残らないようにしました。

### 交通シミュレーション — 車両ごとの重複処理を中央へ集約

各車両が毎フレーム目的地計算、`BoxCast`、Transform移動、直列化を実行していたため、車両とユーザーが増えるほどCPU・物理・GCの負荷も増えていました。

<p align="center">
  <img src="./Docs/images/traffic-system-architecture.ja.svg" alt="エディタ、オーナー、ネットワーク、リモートユーザーへ続く交通状態の処理構成" width="900">
</p>

オーナーが10台の走行状態を一か所で計算し、車両ごとの状態を64ビットに圧縮して送信します。リモート側は同じレーンデータから車両を復元し、毎フレーム補間して画面上の途切れを抑えました。

<details>
<summary><strong>実行中のデバッグ画面を見る</strong></summary>

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="実行中の交通システムにおけるレーンと車両のデバッグ画面" width="900">
  <br>
  <sub>レーンデータ、車両の占有範囲、予測位置、障害物センサー範囲</sub>
</p>

</details>

### パフォーマンス改善結果

![交通システムの初期スナップショットと最新スナップショットのUnity Profiler比較](./Docs/images/traffic-performance-comparison.ja.svg)

車両10台とClientSimのリモートプレイヤー80人を1地点に集め、初期スナップショットと最新スナップショットの同じ300フレームを比較しました。平均CPUフレーム時間は`17.65 ms → 11.92 ms`、繰り返し発生する遅いフレームを示すP95は`24.60 ms → 17.44 ms`まで減少しました。物理処理時間は65.3%、1フレームあたりのGC割り当ては88.1%削減しています。

計算頻度は`10 Hz`に抑え、位置と回転は毎フレーム補間して画面上では滑らかに動くようにしました。

[測定条件と問題ごとの実装を詳しく見る](./Docs/optimization.ja.md)

## モデルと描画の最適化

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="通常レンダリングとワイヤーフレームの比較" width="900">
  <br>
  <sub>左：通常レンダリング · 右：同じカメラから撮影したワイヤーフレーム</sub>
</p>

環境モデルをエリアごとに分け、カメラに映らないエリアはオクルージョンカリングで描画対象から外しました。固定オブジェクトにはスタティックバッチを適用し、街路と建物の照明はベイクしています。

<table align="center">
  <tr>
    <td width="260" align="center"><strong>モデル構成</strong><br><sub>三角形 246,921個 · 環境メッシュ 240個</sub></td>
    <td width="260" align="center"><strong>描画最適化</strong><br><sub>スタティックバッチ対象 392個 · オクルーダー 330個</sub></td>
    <td width="260" align="center"><strong>当たり判定</strong><br><sub>メッシュコライダー 2個</sub></td>
  </tr>
</table>

ベイク照明は約220メッシュに適用しました。ライトマップは4096×4096を3枚、512×512を1枚使用しています。

## コード構成

| 分野 | 主なファイル | 役割 |
| --- | --- | --- |
| ライブ機材 | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | スピーカー設置、検証、所有権、途中参加者との同期、初期化 |
| ステージ音声 | [`VoiceRange.cs`](./Assets/Shinjuku%20Udon/Speaker/VoiceRange.cs) | 出演者の音声距離と音量の共有 |
| 共有操作 | [`ObjectGlobalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectGlobalToggle.cs), [`ObjectLocalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectLocalToggle.cs) | グローバル状態とローカル状態の分離 |
| 交通処理 | [`TrafficSimulationManager.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficSimulationManager.cs) | 車両計算、状態圧縮、送信、リモート車両の復元 |
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
| [Artistoid](https://github.com/Artistoid) · [X @Artistoid_VRC](https://x.com/Artistoid_VRC) | 企画 · グラフィック · 3Dモデリング |
| [hjcud](https://github.com/hjcud) | Unity・UdonSharpシステムの開発・最適化 |

---

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChatワールド</a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live">#VRSJK</a>
  ·
  <a href="https://github.com/hjcud/Shinjuku-Live-Street">GitHub</a>
</p>
