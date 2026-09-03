# zurari_w 開発ロードマップ

**作成**: 2026-07-20
**現状**: Phase 0〜5 完了(main = `b561d45`)。Phase 6 は `phase6a-location-foundation` ブランチで進行中 —
33 コミット / 636 テスト green / **main 未マージ**。進捗の詳細は Phase 6 計画の Status 表と Records を見ること
**コンセプト**: zurari(TUI版, 別名 zrr)の multi-column UX を Windows GUI へ。
アーキテクチャは rwf(Rust版)の純粋 Transition パターンの C# 移植。

---

## 凡例

- `[ ]` 未着手
- `[~]` 部分実装
- `[x]` 完了(`scripts/check.ps1` green で確認済み)

---

## フェーズ一覧

| Phase | 内容 | 状態 | 計画 | 主な成果物 |
|---|---|---|---|---|
| 0 | ソリューション骨格 + 品質ゲート | `[x]` | [phase0-foundation.md](phase0-foundation.md) | 4層構成、BannedApiAnalyzers、Arch.Tests、check.ps1 + pre-commit |
| 1 | Core 状態マシン + Runtime Worker Pool | `[x]` | [phase1-core-and-worker-pool.md](phase1-core-and-worker-pool.md) | `Transition.Apply`、`WorkerRuntime`、`MessageLoop` |
| 2 | ColumnBrowser コントロール | `[x]` | [phase2-columnbrowser.md](phase2-columnbrowser.md) | 仮想化列、リサイズ、カーソル/マーク表示、入力イベント化、Gallery |
| 3 | 実ファイルシステム接続 | `[x]` | [phase3-real-filesystem.md](phase3-real-filesystem.md) | `StateProjection`、composition root、ヘッドレス E2E |
| 4 | Windows シェル統合 | `[x]` | [phase4-shell-integration.md](phase4-shell-integration.md) | アイコン、ゴミ箱、IContextMenu、Explorer DnD、複数選択 |
| 5 | 内製ジョブエンジン + プレビュー | `[x]` | [phase5-jobs-and-preview.md](phase5-jobs-and-preview.md) | JobEngine(進捗/キャンセル/競合確認)、Ctrl+C/X/V、プレビュー(メタ/HEX/動画) |
| 6 | 基本操作の穴埋め + プレビュー刷新 | `[~]` | [phase6-basic-operations.md](phase6-basic-operations.md) | 6a `Location` 基盤 / 6b 生リスト+派生ビュー / 6c プレビューのキャンセル+サムネイルキャッシュ / 6d ドライブペイン(セクション・お気に入り・ピン・ゴミ箱) / 6e-bis カーソル移動の高速化。詳細は phase6 計画の Status 表 |
| 7 | 二画面分割 + 検索・ジャンプ系 | `[ ]` | [phase7-dual-pane-and-search.md](phase7-dual-pane-and-search.md) | — |
| 8 | 仮想ロケーション基盤 | `[x]` | [phase8-virtual-locations.md](phase8-virtual-locations.md) | **Phase 6a として実施済み**(`2b74c1a`)。`Location` 型、`Entry.Target`、包含不変条件の削除 |
| 9 | アーカイブ操作 | `[ ]` | [phase9-archives.md](phase9-archives.md) | — |

完了フェーズの計画ファイルは**着手時に確定させたプランの原文**であり、実装との差異がある。
現在の仕様はコードを参照すること。Phase 6 以降の背景・判断は「[今後の候補](#今後の候補)」に置く。

### Phase 5 完了時点の実装済み機能(実機確認済み)

**ナビゲーション**: multi-column(Finder風)、ドライブ一覧、キーボード(↑↓/PageUp/PageDown/Home/End/←→/
Enter/Backspace)、マウス(クリックで潜行、Finder流タイミング判定)、F5/Ctrl+R 更新、列幅リサイズ、
プレビュー幅リサイズ(設定永続化)。

**選択**: カーソル、マーク(Ctrl+クリック / Space / 矩形選択 + 自動スクロール + 逐次ハイライト)、
Esc で解除、クリックでマーク集合を畳む。

**ファイル操作**: Explorer 相互運用の Ctrl+C/X/V(切り取り対象は薄表示)、内製ジョブエンジンによる
コピー/移動(進捗バー・キャンセル・同名衝突時の上書き/スキップ/中止選択・中断時の書きかけ削除)、
Delete でゴミ箱 / Shift+Delete で完全削除、Explorer との双方向ドラッグ&ドロップ、
シェルコンテキストメニュー(IContextMenu2/3 対応)。

**表示**: シェルアイコン、プレビューペイン(画像 / テキスト(UTF-8・Shift-JIS 判定) / バイナリ HEX ダンプ /
動画サムネイル(ffmpeg 連携))、メタデータブロック(名前・種類・サイズ・作成/更新日時・解像度・色深度)、
外部変更の自動検知(FileSystemWatcher)。

**診断**: `--debug-input` による入力パイプラインのトレースログ(`%TEMP%\zurari-input.log`)。

---

## 参照資産(このリポジトリの外、`C:\Users\user\source\repos\panzoux\`)

- **rwf/** (Rust) — アーキテクチャの本家
- **twf/** (C#) — ロジック移植元(Services/ 配下: ジョブ、検索、アーカイブ)
- **zurari/** (C#) — TUI版 zrr。multi-column UX・Unicode幅・種別判定の移植元
- **win_ctxmenu/** (C++) — IContextMenu ホストの実装例(Phase 4 で移植済み)

---

# 既知の問題 / 要調査

Phase 6 の作業中に見つかったもののうち、**そのフェーズの中で閉じない**もの。細目と計測値は
[phase6-basic-operations.md](phase6-basic-operations.md) の Records にある。

| # | 内容 | 状態 |
|---|---|---|
| 6f-4 | **ゴミ箱の列挙が、同一プロセスで他のシェル COM が動いている間は短いリストを返す。** テストでは並列実行を切って回避済み(6 回中 5 回失敗 → 0 回)。**アプリ本体も同じことをしている**(UI スレッドのアイコン解決と、STA ワーカーのゴミ箱列挙が同時に走る)ため、表示されるゴミ箱の中身が黙って欠ける可能性がある。専用の調査が必要 | `[ ]` 未着手 |
| 6f-2 | `JobEngineTests` のキャンセル競合(50MB のコピーが先に終わると `JobCancelled` が来ない) | `[ ]` 未着手 |
| 6f-3 | テストの `finally` にある 57 個の無防備な `Directory.Delete(dir, recursive: true)` | `[ ]` 未着手 |
| help | **キーバインドの一覧がどこにも無い。** 「コマンドを足したらキーとヘルプ項目の両方を足す」という規約の後半が未実装で、Ctrl+B / Ctrl+D / Ctrl+E / Space / Apps は**発見不可能**。ステータスバーの文脈ヒント + ヘルプ画面(6e.7)で解消する | `[ ]` 6e.7 |
| R-1 | Phase 6 のコミットが **main に一つもマージされていない** | `[ ]` 未着手 |

---

# 今後の候補

**棚卸し日**: 2026-07-20(Phase 5 完了直後)/ 動画サムネイルの項は 2026-08-22 更新

フェーズ一覧と各計画へのリンクは冒頭の「[フェーズ一覧](#フェーズ一覧)」を参照。
以下はその判断根拠と、着手前に確定させた事項。

## 決定事項(2026-07-20 確認済み)

**アーカイブの実現方式**: 検出方式を採用する。標準では **zip のみ** .NET 標準ライブラリ
(`System.IO.Compression`)で対応し、7z/rar/tar 等はユーザーが 7-Zip を導入していれば使える。
7z.dll は同梱しない。動画サムネイル(ffmpeg)と同じ「あれば使う、無ければ縮退」方式で一貫させる。

**二画面分割は必要**。当初「クリップボード方式(Ctrl+C/V)があるので同時表示は不要では」と考えたが、
これは誤り。multi-column は **1本のパス系統しか表示できない**ため、別ドライブ間(`D:\` と `C:\`)や
無関係な2つのツリーを同時に見ることが原理的にできない。二画面ファイラーの本質はそこにあるので、
Phase 7 で実装する。

**フェーズ順は 6 → 7 → 8 → 9 のまま**。Phase 8(仮想ロケーション基盤)を前倒しする理由は無い:
Phase 7 の検索は zrr 同様「列内の絞り込み・ジャンプ」でありパス表現に触らず、Phase 6 が足す機能
(ソート・隠しファイル等)も Column レベルに閉じるため、後回しによる移行コストの増加は軽微。

## zrr(TUI版)との機能差

zurari_w に未実装のもの。影響度は日常操作への効き方。

| 機能 | zrr | 影響度 | 予定 |
|---|---|---|---|
| ファイルを開く(既定アプリ) | Ctrl+Enter | 大 | Phase 6 |
| 新規フォルダ作成 | Shift+K | 大 | Phase 6 |
| リネーム | あり | 大 | Phase 6 |
| **ドライブのプレビュー** | あり | 大 | Phase 6d.7(未着手) |
| プレビュー on/off トグル | Shift+V | 中 | Phase 6 |
| カーソルメモリ(ディレクトリ毎に選択位置を記憶) | あり | 中 | Phase 6 |
| リンク/ジャンクションの判別・リンク先表示・**循環防止** | あり | 中 | Phase 6 |
| 拡張子ミスマッチ警告 | あり | 小 | Phase 6 |
| 音声/実行ファイル/PDF のメタ表示 | あり | 小 | Phase 6 |
| 二画面分割 | Ctrl+W / Tab | 大 | Phase 7 |
| インクリメンタル検索 + migemo | あり | 大 | Phase 7 |
| Leap ナビゲーション | F3 | 中 | Phase 7 |
| ブックマーク + 履歴ジャンプ | b / Shift+B | 中 | Phase 7 |
| 操作ログ | Shift+L | 小 | Phase 7 |
| アーカイブ内容のプレビュー | あり | 中 | Phase 9 |

**GUI 固有で欲しいもの**(zrr には無いが一般的なファイラーの必須機能):
パス直接入力(アドレスバー)、ソート順変更(名前/サイズ/日時/拡張子・昇降)、隠しファイル表示切替、
ステータスバー拡充(選択サイズ・空き容量)、キーヒント/ヘルプ表示、戻る/進む履歴、
キーバインドのカスタマイズ、ウィンドウ位置の永続化。

## 動画サムネイルの現況(2026-08-22 更新)

別セッション「MP4 サムネイル生成ツール動作確認」で方針が確定し、**一部はコミット済み**(`b561d45`)。

**確定した方針 = zrr 方式**: 依存を **ffmpeg 一本**に絞り、指定時刻のフレームを画像として抽出して
**サムネイルキャッシュへ保存**する構成を基本とする。ffmpegthumbnailer は不採用
(Windows の Unicode パス対応を持つ有力な fork が無く、ffmpeg 本体と重複した対応を抱える
合理性が低いため)。ffmpeg の Windows CLI は `GetCommandLineW` / `CommandLineToArgvW` を使うので
日本語パスをそのまま扱える。

| 項目 | 状態 |
|---|---|
| ffmpeg 一本化(ffmpegthumbnailer 廃止) | `[x]` コミット済み (`b561d45`) |
| 失敗理由の表示(`ThumbnailOutcome` で理由を返す) | `[x]` コミット済み (`b561d45`) |
| タイムアウト見直し(4秒 → 20秒、4秒間隔のポーリング) | `[x]` コミット済み (`b561d45`) |
| **ffmpeg の非同期起動**(現在は同期ブロッキング) | `[ ]` Phase 6 |
| **サムネイルキャッシュ**(現在は毎回再生成) | `[ ]` Phase 6 |
| 動画メタ(再生時間・解像度)の自前パース | `[ ]` Phase 6(zrr は MP4 box を直接読む) |

**動作確認の結果**: `where.exe ffmpeg` による検出 OK、正常な動画で PNG サムネイル生成に成功。
「動作しない」と報告された `.mp4` は `moov atom not found`(MP4 のインデックス欠落 = ファイル自体の
破損)で、ffmpeg 側が正しく拒否していた。失敗理由が UI に出ない問題は上表のとおり対応済み。

## 検証済みの事実(2026-07-20)

**WPF は自己完結ビルドを 68MB より小さくできない**。`PublishTrimmed` は WPF 非対応
(`error NETSDK1168`)、NativeAOT も非対応。framework-dependent なら 419KB。
詳細と実測値は [build.txt](../build.txt) を参照。
