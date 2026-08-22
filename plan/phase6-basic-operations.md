# Phase 6 — 基本操作の穴埋め

**状態**: `[ ]` 未着手(ガワのみ。着手時にこのファイルへタスクを書き下す)
**前提**: Phase 5 完了(main、HEAD `16f2283`)
**位置づけ**: [roadmap.md](roadmap.md) の Phase 6

---

## 目的

日常操作の穴を最短で塞ぐ。既存アーキテクチャ(Msg → Transition → Effect)に素直に載る小粒な
機能の集まりで、基盤変更を伴わない。

## スコープ

### やること

**ファイル操作**

- [ ] ファイルを開く(既定アプリ) — Enter / ダブルクリック。現状は右クリックメニュー経由のみ
- [ ] 新規フォルダ作成
- [ ] リネーム(F2)

**表示・ナビゲーション**

- [ ] ソート順変更(名前 / サイズ / 日時 / 拡張子、昇順・降順)
- [ ] 隠しファイル・システムファイルの表示切替
- [ ] カーソルメモリ(ディレクトリごとに選択位置を記憶し、戻ったとき復帰)
- [ ] ステータスバー拡充(選択件数・選択サイズ・ドライブ空き容量)
- [ ] キーヒント / ヘルプ表示(現状キー一覧を見る手段が皆無)

**リンク / ジャンクション**

- [ ] リパースポイントの判別と視覚表示(リンク・ジャンクション・シンボリックリンク)
- [ ] リンク先パスの表示(プレビューのメタデータ欄)
- [ ] **循環防止** — 深いシンボリックリンクで無限にたどらないようにする(zrr にはある)

**プレビュー**

- [ ] **ドライブのプレビュー**(zrr の LoadDrive 相当。ユーザー要望度が高い)
      表示項目: ボリュームラベル / ファイルシステム / 総容量 / 使用量(%) / 空き容量 / 使用率バー
- [ ] プレビュー on/off トグル(zrr は Shift+V)
- [ ] 拡張子ミスマッチ警告(例: `.txt` だが中身は PNG)
- [ ] 音声 / 実行ファイル / PDF のメタデータ表示

**動画サムネイルの zrr 方式化**(方針は別セッションで確定済み。詳細は
[roadmap.md の「動画サムネイルの現況」](roadmap.md#動画サムネイルの現況2026-08-22-更新))

- [x] ffmpeg 一本化 / 失敗理由の表示 / タイムアウト見直し — 実装済み(未コミット)
- [ ] **ffmpeg の非同期起動** — 現在は `RunProcess` が同期ブロッキングで、Worker を1本占有する。
      zrr は `ExtractVideoFrameAsync` として `CancellationToken` 付きの非同期起動にしている。
      カーソルが離れたら即座に破棄できるようにする(現状は世代番号で結果を捨てているだけで、
      プロセス自体は走り続ける)
- [ ] **サムネイルキャッシュ** — 抽出したフレームをキャッシュに保存し、2回目以降は再生成しない。
      zrr は `GetVideoCachePath(filePath)` でパスを決め、存在すれば ffmpeg を起動しない。
      要決定: キャッシュ場所(`%LOCALAPPDATA%\zurari\thumbs` 等)、キー(パス+更新日時+サイズ)、
      上限サイズと破棄方針
- [ ] 動画メタ(再生時間・解像度)の表示 — zrr は MP4 の box を自前パースして ffmpeg 無しで取得
      (`ParseMp4Info` / `WalkMp4Boxes`)。ffmpeg 非依存でメタだけ出せる利点がある

### やらないこと

- 二画面分割、検索、ブックマーク → Phase 7
- 仮想ディレクトリを要する機能(アーカイブ閲覧等) → Phase 8 以降

## 設計メモ

- 新規フォルダ / リネーム / 開く はいずれも I/O なので `Effect` を足して Runtime で実行する。
  リネームは競合(同名が既に存在)の扱いを決める。ジョブエンジンに載せるほどの規模ではない。
- ソート・隠しファイルは `Column` に表示条件を持たせ、`Transition` で並べ替える純粋処理にできるか、
  それとも `ReadDirectory` の再発行が要るかを最初に決める(前者が望ましい)。
- リンク判定は `FileSystemInfo.LinkTarget` / `FileAttributes.ReparsePoint` で取れる。
  循環防止は Runtime 側で「たどったリンク先の実パス集合」を持つ方式が素直。
- ドライブプレビューは `DriveInfo`(Runtime)で取得し、`PreviewKind` に `Drive` を追加する。
  カーソルがドライブ行にあるとき `ReconcilePreview` が拾う必要がある(現状はファイルのみ対象)。

## 参照

- zrr のドライブプレビュー: `..\zurari\Program.cs` の `PreviewLoader.LoadDrive`
- zrr の拡張子ミスマッチ: 同 `PreviewLoader.CheckExtensionMismatch`
- zrr のカーソルメモリ: 同 `_cursorMemory` / `ApplyCursorMemory` / `SaveCursorMemory`
- zrr の動画プレビュー(非同期 + キャッシュ): 同 `PreviewLoader.LoadVideoPreviewAsync` /
  `GetVideoCachePath` / `ExtractVideoFrameAsync`、MP4 メタ自前パースは `ParseMp4Info` / `WalkMp4Boxes`
- 現行実装: `src\Zurari.Runtime\VideoThumbnailer.cs`(ffmpeg 一本化・失敗理由付き)

## 検証

- `scripts/check.ps1` green
- 手動: 各操作の実機確認。特にリンクの循環防止(自己参照ジャンクションを作って確認)
