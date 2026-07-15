# Phase 4 — Windowsシェル統合(アイコン / ゴミ箱削除 / コンテキストメニュー / DnD)

前提: Phase 0〜3 完了(mainマージ済み)。実FSブラウズが動く状態からの積み増し。
参照: `C:\Users\user\source\repos\panzoux\win_ctxmenu\ctxmenu.cpp`(IContextMenuホストのC++実装例)。

## 設計判断(このフェーズで確定)

- **Zurari.Shell は net8.0-windows + UseWPF に変更してよい。** HwndSource/ImageSource を扱うため。
  依存規約は不変: Shell → Core のみ(Runtime/App/Controls 参照禁止、Arch.Tests 既存テストが監視)。
- **シェル系 Effect の実行者は Zurari.Shell 内の `ShellEffectExecutor`。**
  Runtime は Shell を参照できない(Arch規約)ため、App の composition root が
  Effect の型で振り分ける: FS系 → WorkerRuntime.Submit、シェル系 → ShellEffectExecutor.Submit。
  振り分けは型スイッチ1式のみ(判断ロジック禁止は維持)。
- **interop は手書き P/Invoke + ComImport**(CsWin32 は使わない。生成器より可読で移植例に近い)。
- **ファイル転送そのものは Phase 4 では書かない。** DnD 受け入れは SHFileOperation/IFileOperation に
  委譲(シェル標準の進捗ダイアログが出る)。内製ジョブエンジンは Phase 5。

## タスク(1タスク = 1 sonnet dispatch = 1コミット)

### 1. シェルアイコン
- Controls: `EntryVm` に `ImageSource? Icon`(null なら非表示、既存テスト互換)。
  ItemTemplate 左端に 16x16 Image。
- Shell: `ShellIconCache` — 拡張子(+Directory/Drive種別)→ BitmapSource。
  `SHGetFileInfoW` + `SHGFI_USEFILEATTRIBUTES|SHGFI_ICON|SHGFI_SMALLICON`(ディスク非接触)、
  HICON → `Imaging.CreateBitmapSourceFromHIcon` → `Freeze()` → `DestroyIcon`。
  ConcurrentDictionary でキャッシュ。exe/lnk 等「実体依存アイコン」は v1 では拡張子代表で妥協。
- App: StateProjection に icon resolver(`Func<Entry, ImageSource?>`)を注入。
- テスト: STA — ".txt"/".exe"/Directory で非null・Frozen・同一拡張子はキャッシュ同一参照。

### 2. ゴミ箱削除
- Core: `Msg.DeleteEntry(ColumnIndex, EntryIndex)` → 対象列を Loading に、
  `Effect.DeleteToRecycleBin(ColumnIndex, Path, EntryFullPath)` を発行。
  `Msg.DeleteCompleted(ColumnIndex, Path)` → その列に `Effect.ReadDirectory` 再発行。
  `Msg.DeleteFailed(ColumnIndex, Path, Error)` → Error状態(stale check は既存流儀)。
  GenMsg 追加必須。
- Shell: `ShellEffectExecutor` 新設(post: Action<Msg>、Submit(Effect)。専用1ワーカー、
  Channel、Dispose — WorkerRuntime と同型)。`IFileOperation`(FOF_ALLOWUNDO|FOFX_RECYCLEONDELETE 相当、
  確認UIなし: FOF_NOCONFIRMATION)で削除実行。
- App: Delete キー → MessageBox 確認(はい/いいえ)→ `Msg.DeleteEntry`。
  composition root に効果振り分け(型スイッチ)を実装。
- テスト: 一時ファイルを DeleteToRecycleBin → ファイルが消え DeleteCompleted が届く(実FS)。
  Core transition はヘッドレスで全分岐。

### 3. シェルコンテキストメニュー(最難関、win_ctxmenu 移植)
- Shell: `ShellContextMenu.Show(IntPtr ownerHwnd, string[] paths, int screenX, int screenY)` —
  IShellFolder.GetUIObjectOf(IContextMenu) → QueryContextMenu → TrackPopupMenuEx →
  InvokeCommand。IContextMenu2/3 の HandleMenuMsg(オーナードローの「送る」等)対応は
  メッセージフック(HwndSource.AddHook)で。ctxmenu.cpp の手順を C# に忠実移植。
  UIスレッドでブロッキング実行(TrackPopupMenu の作法)。
- Controls: 右クリック時に画面座標が要る — `EntryPointerPressedEventArgs` に
  `ScreenPosition (Point)` を追加(既存呼び出し互換を保つならオーバーロード/新プロパティ)。
- App: 右クリック → カーソル移動(CursorTo)+ ShellContextMenu.Show(フルパス解決は
  StateProjection 同様 Column.Path + Entry.Name)。実行後 `Msg.Refresh` を dispatch。
- テスト: interop 自動テストは困難 — COMインターフェイス定義のGUID/レイアウト検証と
  「存在しないパスで例外を投げず false を返す」程度のスモーク。動作確認は手動チェックリスト
  (右クリック→開く/コピー/プロパティ/送る サブメニュー描画)を最終報告に記載。

### 4. Explorer 連携 DnD
- ドラッグ開始(OUT): Controls — 行の左ボタン押下後、`SystemParameters.MinimumHorizontalDragDistance`
  超えの移動で `EntryDragRequested(ColumnIndex, EntryIndex)` を発火(判断はホスト)。
  App — マーク済みエントリ群(なければカーソル行)のフルパスで
  `DataObject(DataFormats.FileDrop)` を作り `DragDrop.DoDragDrop(Copy|Move)`。
- ドロップ受け入れ(IN): Controls — `AllowDrop`、列単位で DragOver/Drop をハンドルし
  `FileDropRequested(ColumnIndex, string[] Paths, bool IsMove)`(Shift=Move、既定Copy)を発火。
  ドロップ先ハイライト(列ボーダー強調)。
- App: FileDropRequested → `Msg.DropFiles(ColumnIndex, Paths, IsMove)`(Core追加、GenMsg必須)
  → `Effect.ShellCopyOrMove(ColumnIndex, DestPath, Paths, IsMove)` → ShellEffectExecutor が
  IFileOperation で実行(シェルの進捗UI任せ)→ 完了で `Msg.Refresh` 相当。
- テスト: Core transition ヘッドレス全分岐。ShellCopyOrMove は一時ディレクトリ実コピーで検証。
  ドラッグジェスチャ自体は STA で MouseMove 合成が不安定なら手動確認に回してよい
  (その場合は最終報告に明記)。

## 検証

- 各タスク: `scripts/check.ps1` green + 記載のテスト。
- フェーズ完了時の手動チェックリスト: アイコン表示 / Del→ゴミ箱(元に戻せる) /
  右クリックメニュー(開く・プロパティ・送る) / Explorer へドラッグでコピー /
  Explorer からドロップでコピー(シェル進捗ダイアログ)→ 列が自動更新。
