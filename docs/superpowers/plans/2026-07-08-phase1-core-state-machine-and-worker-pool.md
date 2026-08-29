# Phase 1 — Core状態マシン + Runtime Worker Pool(ヘッドレス)

前提: `docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md` を先に読む。
GUIは一切触らない。全成果物は `dotnet test` だけで検証できること。
各タスクの完了条件: 記載のテストが通る AND `scripts/check.ps1` green。

## 参照

- rwf: `C:\Users\user\source\repos\panzoux\rwf\docs\ARCHITECTURE.md`(Transition/Effectの分割単位)、
  `JOB_HANDLING.md`(ジョブ状態遷移)
- twf: `C:\Users\user\source\repos\panzoux\twf\Services\`(BackgroundOperations、ファイル操作の実装)
- zurari: `C:\Users\user\source\repos\panzoux\zurari\Program.cs`(列ナビゲーションの状態、
  ドライブ一覧、シンボリックリンク循環防止)

## タスク

### 1. Core: ブラウズ状態のモデル化
- [ ] `AppState` に multi-column ブラウズ状態を追加:
  `ImmutableArray<Column> Columns`、`int FocusedColumn`、各 `Column` は
  `Path`(正規化済み文字列)、`ImmutableArray<Entry> Entries`、`int Cursor`、`int ScrollOffset`、
  `LoadState`(Loading/Loaded/Error)
- [ ] `Entry` record: 名前、種別(Dir/File/Drive)、サイズ、更新日時、属性。
  I/O型(FileInfo等)をCoreに持ち込まない(BannedSymbolsが弾く)
- [ ] 不変条件を `AppState.Validate()`(DEBUG用)として明文化:
  Cursor は Entries 範囲内、FocusedColumn は Columns 範囲内、
  Columns[i+1].Path は Columns[i] のカーソル位置ディレクトリ配下
- テスト: 不変条件を property test に組み込む(全Msg列適用後に Validate が通る)

### 2. Core: ナビゲーションMsgとTransition
- [ ] Msg追加: `CursorUp/Down/PageUp/PageDown/Home/End`, `FocusLeft/Right`,
  `EnterDirectory`, `GoToParent`, `Refresh`,
  `DirectoryLoaded(columnIndex, path, entries)`, `DirectoryLoadFailed(columnIndex, path, error)`
- [ ] Effect追加: `ReadDirectory(columnIndex, path)`
- [ ] zurari の挙動を踏襲: Enterで右に列が伸びる、親移動で列が縮む、
  ドライブ一覧がルート列(zurari Program.cs 参照)
- [ ] `GenMsg`(Core.Tests)に全Msgを追加
- テスト: 例示ベース(各Msg) + property(任意Msg列で不変条件維持、
  DirectoryLoaded が古い列indexに届いても壊れない=レース耐性)

### 3. Runtime: Worker Pool と Effect 実行
- [ ] `IEffectRunner`: Effect を受け、結果 Msg を `ChannelWriter<Msg>` に書く
- [ ] `WorkerRuntime`: `Channel<Effect>` で受け、固定数ワーカー(まず2)で実行、
  `CancellationToken` で全体停止。`ReadDirectory` 実装(twf/zurariの列挙コードを移植:
  アクセス拒否・長いパス・シンボリックリンクの扱い含む)
- [ ] UIスレッドへの合流は Runtime では抽象化(`Action<Msg> post`)。WPF Dispatcher は Phase 3 で App が渡す
- テスト: 一時ディレクトリで ReadDirectory → DirectoryLoaded が届く、
  アクセス不能パスで DirectoryLoadFailed、キャンセルで停止

### 4. 配線ハーネス(ヘッドレスloop)
- [ ] `MessageLoop`(Runtime): Msgキュー → Transition.Apply → Effect投入 → 新State通知、を回す小さなクラス
- テスト: 「初期State + Refresh → ReadDirectory実行 → DirectoryLoaded → Entriesが埋まる」を
  実ファイルシステム(一時ディレクトリ)でE2E

## 注意

- LINQは可(zurari本体はLINQ-free方針だったが本プロジェクトでは許可)。ホットパスのみ配慮
- 例外はEffect実行の境界で捕捉し必ず `*Failed` Msg に変換。ワーカーを死なせない
- 公開APIにXMLコメント(既存ファイルの粒度に合わせる)
