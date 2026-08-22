# Phase 5 — 内製ジョブエンジン + プレビューペイン

**状態**: `[x]` 完了
**実施**: 2026-07-19〜20
**主なコミット**: `7a7257f`, `132ef57`, `053d6ed`, `3fca831`, `16f2283`
**位置づけ**: [roadmap.md](roadmap.md) の Phase 5

> 以下は**着手時に確定させたプランの原文**である(このファイルは記録用に原典を
> そのまま複製したもの。原典: `../docs/superpowers/plans/2026-07-19-phase5-jobs-and-preview.md`)。
> 実装中に判明した事実や設計変更はコミットメッセージとコード内コメントに記録されており、
> **原文と実装に差異がある箇所がある**。現在の仕様を知りたい場合はコードを参照すること。

---

## Phase 5 — 内製ジョブエンジン(コピー/移動/進捗/キャンセル)+ プレビューペイン

前提: Phase 0〜4 完了(main)。参照: `C:\Users\user\source\repos\panzoux\twf\Services\`
(BackgroundOperations・ファイル操作の移植元)、`C:\Users\user\source\repos\panzoux\zurari\Program.cs`
(プレビュー、マジックナンバー種別判定)。

### 設計判断(確定)

- **操作系はOSクリップボード**: Ctrl+C(コピー)/ Ctrl+X(切り取り)/ Ctrl+V(フォーカス列へ貼り付け)。
  CF_HDROP + "Preferred DropEffect" を使い Explorer と相互運用(Explorerでコピー→zurariで貼り付け、逆も可)。
  クリップボード操作は WPF の `Clipboard`/`DataObject`(App層でOK、シェルAPI不要)。
- **貼り付けの実行は内製ジョブエンジン**(Runtime): 進捗Msg・キャンセル対応。
  既存の DnD / 右クリックメニューは IFileOperation のまま(変更しない)。
- **競合(上書き)ポリシー v1**: 貼り付け前に App が確認不要 — Core が Effect 発行前に判断できない
  (存在確認はI/O)ため、**ジョブ実行中に競合を検出したらそのファイルをスキップし、
  完了Msgに skipped 件数を含める**。上書きしたい場合は先に削除してもらう(v2でダイアログ化)。
- **プレビュー**: カーソル移動で自動更新。Runtime が読み込み、Msgで返す。
  画像(BitmapImage化はApp)/ テキスト先頭(~64KB, エンコーディング判定は UTF-8/SJIS 簡易)/
  それ以外はマジックナンバー種別+メタデータ表示。上限(画像~50MB、それ以上はメタのみ)。
  レース対策: リクエストに世代番号(Generation)を持たせ、古い結果は捨てる。

### タスク(1タスク = 1 sonnet dispatch = 1コミット、TDD)

#### 1. Core: ジョブ状態モデル + Runtime: JobEngine
- Core: `Job` record(JobId int, Kind Copy/Move, ImmutableArray<string> Sources, string DestDir,
  Status Queued/Running/Completed/Failed/Cancelled, DoneFiles, TotalFiles, DoneBytes, TotalBytes,
  CurrentFile string?, Error string?, SkippedFiles int)。`AppState.Jobs: ImmutableArray<Job>`。
- Msg: `PasteRequested(int ColumnIndex, ImmutableArray<string> Sources, bool IsMove)`
  (フォーカス列のPathがDestDir。root ""列へは無視)→ Job生成(JobId=State内の連番 `NextJobId`)+
  `Effect.RunFileJob(JobId, Kind, Sources, DestDir)`。
  `JobProgress(JobId, DoneFiles, TotalFiles, DoneBytes, TotalBytes, CurrentFile)`、
  `JobCompleted(JobId, SkippedFiles)` → Job更新 + 影響ディレクトリ再読込(ShellOpCompleted と同じ
  AffectedDirs 方式: DestDir + Move時はソース親)、`JobFailed(JobId, Error)`、
  `JobCancelRequested(JobId)` → `Effect.CancelJob(JobId)`、`JobDismissed(JobId)`(完了/失敗表示の消去)。
  存在しないJobIdはすべて無視(レース安全)。GenMsg必須。
- Runtime: `JobEngine : IDisposable` — ジョブ専用ワーカー(1本で直列実行、キューは Channel)。
  実装は twf Services のコピー/移動を参照しつつ新規: 事前スキャン(総ファイル数/バイト数→最初の
  JobProgress)、ファイル単位コピー(FileStream 1MBバッファ、CopyToAsync+CancellationToken)、
  進捗は100ms間隔スロットル、Move は同一ボリューム File.Move / 跨ボリューム copy+delete、
  ディレクトリ再帰、競合(dest存在)はスキップ計上、ジョブごとの CancellationTokenSource
  (CancelJob で発火 → 部分完了のまま JobCompleted ではなく Cancelled を Msg で返す:
  `JobFailed` とは別に `JobCancelled(JobId)` を追加)。ワーカーは例外で死なない。
- テスト: 実tempディレクトリで copy/move/再帰/競合スキップ/キャンセル(大きめファイルを生成して
  途中キャンセル)/進捗Msgの単調増加。Core遷移は全分岐+property。

#### 2. App: クリップボード配線(Ctrl+C/X/V)
- Ctrl+C / Ctrl+X(Window KeyDown): フォーカス列のマーク集合(なければカーソル)のフルパスで
  `DataObject`(FileDrop + "Preferred DropEffect" = Copy/Move)を `Clipboard.SetDataObject`。
  App層のみ(シェルAPI不要)。マークなし&カーソルなしは無視。
- Ctrl+V: `Clipboard.GetDataObject()` から FileDrop + DropEffect を読み、
  `Msg.PasteRequested(focusedColumn, paths, isMove)` を dispatch(1:1変換)。
- Effect.RunFileJob / CancelJob を JobEngine にルーティング(型スイッチに追加)。
- 検証: Explorer でコピー→zurari で Ctrl+V(実機確認項目)、zurari で Ctrl+C→Explorer 貼り付け。
  自動テストは App.Tests のヘッドレスE2E(JobEngine 実物 + temp ディレクトリで PasteRequested →
  JobCompleted → 列再読込まで)。

#### 3. App: ジョブパネルUI
- MainWindow 下部にジョブストリップ(ItemsControl): ジョブごとに Kind/DestDir 短縮表示、
  ProgressBar(バイトベース、Total不明時は Indeterminate)、現在ファイル名、キャンセルボタン
  (Running時)/ 閉じるボタン(終了時)→ JobCancelRequested / JobDismissed。
  アクティブジョブが0なら折りたたみ(Visibility)。
- StateProjection に `IReadOnlyList<JobVm> ProjectJobs(AppState)`(純関数、App.Tests で単体テスト:
  進捗率計算、状態→表示文字列、バイトの人間可読化は既存ヘルパー再利用)。
- XAMLはロジック禁止を維持(クリック→Msg dispatch のみ)。

#### 4. プレビューペイン
- Core: `PreviewState`(Path, Generation int, Kind Image/Text/Binary/None, Text string?,
  ImageBytes ImmutableArray<byte>? — 画像はバイト列で保持しAppがデコード、Error string?)。
  カーソル移動系Msg(CursorTo/Up/Down/.../EnterDirectory file選択/DirectoryLoaded カーソル確定)の後、
  フォーカス列カーソルがファイルなら `Effect.LoadPreview(Generation, FullPath)` を発行
  (Generation はインクリメント。ディレクトリ/ドライブ/カーソルなしなら PreviewState=None)。
  `PreviewLoaded(Generation, Kind, Text?, ImageBytes?)` / `PreviewFailed(Generation, Error)` —
  Generation が現在値と違えば捨てる。**注意: 全カーソルMsgに絡むため Transition の共通後処理として
  実装**(各分岐にコピペしない)。GenMsg必須。
- Runtime: LoadPreview 実行 — zurari Program.cs の FileTypeDetector(マジックナンバー)を移植
  (Core に置く: 純粋関数 `FileTypeDetector.Detect(ReadOnlySpan<byte> head)` — zurari から移植し
  単体テスト)。画像(PNG/JPEG/GIF/BMP/WebP)→ バイト列(上限50MB、超過はBinary扱い)、
  テキスト判定(BOM/UTF-8妥当性/SJIS推定)→ 先頭64KBをデコード、他 → Binary(種別ラベル)。
- App: プレビュー領域の placeholder Border を PreviewPane(App内のUserControl でよい: 配線のみ)に
  置き換え — Image(Stretch=Uniform)/ TextBox(ReadOnly,等幅)/ メタ表示(種別・サイズ・日時)。
  ImageBytes→BitmapImage 変換(Freeze)は App。StateProjection にプレビューVm投影+単体テスト。
- テスト: Runtime実FS(PNG/テキスト/バイナリをtempに書いて判定・上限・失敗)、Core遷移
  (Generation レース: 古い PreviewLoaded 無視)、FileTypeDetector 単体。

### 検証
- 各タスク `scripts/check.ps1` green。
- フェーズ完了時の手動チェック: Explorer⇄zurari のコピペ相互運用 / 大きなフォルダのコピーで
  進捗表示・キャンセル / 移動後の両列自動更新 / 画像・テキスト・バイナリのプレビュー切替が
  カーソル移動に追従(高速移動でチラつかない=世代破棄が効いている)。
