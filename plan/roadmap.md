# zurari_w 開発ロードマップ

**作成**: 2026-07-20
**現状**: Phase 0〜5 完了、main マージ済み(HEAD `16f2283`、全471テスト green)
**コンセプト**: zurari(TUI版, 別名 zrr)の multi-column UX を Windows GUI へ。
アーキテクチャは rwf(Rust版)の純粋 Transition パターンの C# 移植。

---

## 凡例

- `[ ]` 未着手
- `[~]` 部分実装
- `[x]` 完了(`scripts/check.ps1` green で確認済み)

---

## フェーズ一覧

| Phase | 内容 | 状態 | 成果物 / 計画 |
|---|---|---|---|
| 0 | ソリューション骨格 + 品質ゲート | `[x]` | 4層構成、BannedApiAnalyzers、Arch.Tests、check.ps1 + pre-commit |
| 1 | Core 状態マシン + Runtime Worker Pool | `[x]` | `Transition.Apply`、`WorkerRuntime`、`MessageLoop` |
| 2 | ColumnBrowser コントロール | `[x]` | 仮想化列、リサイズ、カーソル/マーク表示、入力イベント化、Gallery |
| 3 | 実ファイルシステム接続 | `[x]` | `StateProjection`、composition root、ヘッドレス E2E |
| 4 | Windows シェル統合 | `[x]` | アイコン、ゴミ箱、IContextMenu、Explorer DnD、複数選択 |
| 5 | 内製ジョブエンジン + プレビュー | `[x]` | JobEngine(進捗/キャンセル/競合確認)、Ctrl+C/X/V、プレビュー(メタ/HEX/動画) |
| 6 | 基本操作の穴埋め + プレビュー刷新 | `[ ]` | [phase6-basic-operations.md](phase6-basic-operations.md) |
| 7 | 二画面分割 + 検索・ジャンプ系 | `[ ]` | [phase7-dual-pane-and-search.md](phase7-dual-pane-and-search.md) |
| 8 | 仮想ロケーション基盤 | `[ ]` | [phase8-virtual-locations.md](phase8-virtual-locations.md) |
| 9 | アーカイブ操作 | `[ ]` | [phase9-archives.md](phase9-archives.md) |

Phase 0〜5 の元プラン全文は「[完了フェーズの元プラン](#完了フェーズの元プラン全文)」に、
Phase 6 以降の背景・判断は「[今後の候補](#今後の候補)」に置く。

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

# 完了フェーズの元プラン(全文)

以下は各フェーズ着手時に確定させたプランの原文である。実装中に判明した事実や設計変更は
コミットメッセージとコード内コメントに記録されているため、原文と差異がある箇所がある。

---

> 原典: `docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md`
> (Phase 0 = 設計確定 + ソリューション骨格。以下はその承認済みプラン全文)

## Phase 0 — zurari_w 設計 + 立ち上げ計画

### Context

zurari(C#/.NET 8 コンソール版ミラーカラムファイラー)のコンセプトをそのままに、
Windows特化のGUI版を新規プロジェクト `zurari_w` として立ち上げる。

- 参照資産(全て `C:\Users\user\source\repos\panzoux\` 配下):
  - **rwf** (Rust) — アーキテクチャの参照: 純粋Transitionベース状態マシン、Worker Poolによる
    非同期I/O分離、lib/bin分離、property-based testing (800+ tests)
  - **twf** (C#) — ロジックの参照: Services/Controllers/Models構成の二画面ファイラー。
    ファイル操作・ジョブ・検索・アーカイブ等のC#実装をほぼそのまま移植可能
  - **zurari** (C#) — UXの参照: multi-column(Finder風)ナビゲーション、Unicode幅処理、
    マジックナンバーによるファイル種別判定、プレビュー
  - **win_ctxmenu** (C++) — IContextMenuホストの実装例(C#へ移植する際の参照)

### 決定事項(ユーザー確認済み)

| 項目 | 決定 |
|---|---|
| 言語/GUI | **C# / WPF** (.NET 8+、framework-dependent 単一ファイルexe ≈1MB。利用側に .NET Desktop Runtime が必要) |
| サイズ制約 | Runtime依存OK。単体exeであること自体は維持(PublishSingleFile) |
| コア | 新規lib。rwfパターンをC#で設計し、twf/zurariから実装知見を移植 |
| シェル統合 | フル: Explorer連携OLE drag&drop、シェルコンテキストメニュー、ゴミ箱削除、シェルアイコン |
| 品質ゲート | フル装備を最初から(下記) |
| 引き継ぎ | fable でレール(骨格+品質ゲート+難所)を敷き、機能追加は opus/sonnet が担う前提 |

### アーキテクチャ(rwfパターンのC#移植)

一方向データフロー。UIはStateの投影、入力はMsgに変換されるだけ。

```
入力(キー/マウス/DnD) → Msg → Transition.Apply(state, msg) → (新State, Effect[])
                                                                    │
        UI再描画 ← State投影 ←──── Msg(完了/進捗/エラー) ←── Worker Pool が Effect を実行
```

- **Transitionは純粋関数**: `(AppState, Msg) → (AppState, IReadOnlyList<Effect>)`。
  I/Oもスレッドも触らない。AppStateはimmutable record。UIなしで全テスト可能。
- **Effect**はI/O要求の記述(ReadDirectory, CopyFiles, LoadPreview...)。Runtimeが
  Worker Pool (Channel<T>ベース) にディスパッチし、結果はMsgとしてUIスレッドへ戻る。
  ジョブはID付きで進捗Msg・キャンセル(CancellationToken)対応(twfのBackgroundOperations参照)。
- **WPF層は薄いシェル**: MsgをポストしStateを描画するのみ。ビジネスロジック禁止(機械検査)。

### ソリューション構成

```
zurari_w/
  Zurari.sln
  Directory.Build.props        … 全プロジェクト共通の品質設定(下記)
  src/
    Zurari.Core/               … 純粋状態マシン。★依存パッケージ0、BCL純粋部のみ
    Zurari.Runtime/            … Worker Pool、Effect実行、ファイルI/O、ジョブ管理
    Zurari.Shell/              … Win32/COM interop (CsWin32): IContextMenu, IFileOperation(ゴミ箱),
                                  SHGetFileInfo/IShellItemImageFactory(アイコン), OLE DnD補助
    Zurari.App/                … WPF。ColumnBrowserコントロール + 配線のみ
  tests/
    Zurari.Core.Tests/         … 単体 + property-based (CsCheck) でTransition不変条件を検証
    Zurari.Runtime.Tests/      … Worker/ジョブのテスト(一時ディレクトリ実I/O)
    Zurari.Arch.Tests/         … アーキテクチャテスト(NetArchTest.Rules): 層違反を機械的に弾く
  docs/superpowers/{specs,plans}/  … zurari本体と同じ運用
  scripts/check.ps1            … build + test + format の一発ゲート(pre-commit兼CI)
  CLAUDE.md                    … コマンド・規約・引き継ぎ知識
```

依存方向(プロジェクト参照で物理的に強制):
`App → Runtime → Core` / `App → Shell` / `Shell → Core(契約のみ)`。Coreは何も参照しない。

### multi-columnコントロール(最重要部品)

`ColumnBrowser` — 自作WPFコントロール群。ここが精巧なら残りは積み木、の方針に沿い
**Phase 2で偽データ(IColumnSource)だけで完成させる**(ギャラリーアプリで単体開発・目視確認)。

- 横: 列コレクション。各列 `GridSplitter` 相当でリサイズ可、末尾にプレビューペイン。
- 縦: 各列は `VirtualizingStackPanel` ベースの仮想化リスト(数万件でも滑らか)。
- 選択・カーソル・マーク状態はすべてCore側State由来。コントロールは表示+入力転送のみ。
- DnD: WPF標準の `DataObject(DataFormats.FileDrop)` でExplorer相互授受。
  ドラッグ開始/ドロップ受理の判断はMsg経由でCoreが行う。

### 品質ゲート(「正しい書き方を一番楽に、他を機械的に弾く」)

Directory.Build.props で全プロジェクトに一括強制:
- `TreatWarningsAsErrors` / `Nullable=enable` / `AnalysisLevel=latest-all` / `EnforceCodeStyleInBuild`
- **BannedApiAnalyzers**: `System.IO.File/Directory` の直接使用を Runtime 以外で禁止、
  `Dispatcher`/`Task.Run` を App の配線層以外で禁止 等 → 「Effect経由が一番楽」になる
- **プロジェクト参照による物理層分離**(最強のゲート) + Arch.Tests で命名・配置規約を検査
- **CsCheck** による property-based test: 任意Msg列を適用してもStateが不変条件を満たす
  (rwfのproptest相当)
- `scripts/check.ps1` = dotnet format --verify-no-changes + build + 全テスト。
  git hook (pre-commit) とCIの両方で同一スクリプトを実行
- CLAUDE.md に「完了 = check.ps1 green」を明記(opus/sonnet引き継ぎ用)

### フェーズ計画(各フェーズ = 1 plan file、zurari本体の運用と同じ)

- **Phase 0(本セッションで実施)**: git init、ソリューション骨格、品質ゲート一式、
  CLAUDE.md、設計doc(`docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md`)、
  Phase 1以降のplanファイル雛形
- **Phase 1**: Core状態マシン + Runtime Worker Pool(ヘッドレス、GUI無しで全テスト)
- **Phase 2**: ColumnBrowserコントロール(偽データ+ギャラリーで完成させる): 仮想化、
  列リサイズ、キーボードナビ、Unicode幅対応表示
- **Phase 3**: 実ファイルシステム接続 — zurari同等の基本操作(閲覧/移動/更新/ドライブ)
- **Phase 4**: シェル統合 — アイコン、コンテキストメニュー(win_ctxmenu移植)、ゴミ箱、DnD
- **Phase 5**: ジョブUI(コピー/移動/進捗/キャンセル、twfから移植)、プレビューペイン

fableの残り時間は Phase 0–2(レールと難所)に優先投下し、Phase 3以降は opus/sonnet が
planファイルに沿って実行する想定。

### Phase 0 の具体的作業(承認後すぐ実施する範囲)

1. `git init` + .gitignore(VS/dotnet標準)
2. `dotnet new` でソリューション+6プロジェクト作成、プロジェクト参照を上記依存方向で設定
3. Directory.Build.props(品質設定一括)+ .editorconfig + BannedSymbols.txt
4. Arch.Tests に層違反検査の最初のテスト(Core無依存、Runtime非WPF依存)
5. scripts/check.ps1 + pre-commit hook
6. CLAUDE.md(ビルド/テスト/publishコマンド、アーキテクチャ規約、参照資産の場所)
7. 設計docをspecsに保存、Phase 1のplanファイル作成
8. 検証: `scripts/check.ps1` がgreen、`dotnet publish -c Release -p:PublishSingleFile=true
   --no-self-contained` で単一exeが出ること、空のWPFウィンドウが起動すること

### 検証方法

- 常時: `scripts/check.ps1`(format + build + 全テスト)
- Phase 2以降: ギャラリーアプリでColumnBrowserを目視、`dotnet publish` 産物の起動確認
- Core: property-based testで遷移不変条件(選択位置の妥当性、列整合性など)を常時検証

---

> 原典: `docs/superpowers/plans/2026-07-08-phase1-core-state-machine-and-worker-pool.md`

## Phase 1 — Core状態マシン + Runtime Worker Pool(ヘッドレス)

前提: `docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md` を先に読む。
GUIは一切触らない。全成果物は `dotnet test` だけで検証できること。
各タスクの完了条件: 記載のテストが通る AND `scripts/check.ps1` green。

### 参照

- rwf: `C:\Users\user\source\repos\panzoux\rwf\docs\ARCHITECTURE.md`(Transition/Effectの分割単位)、
  `JOB_HANDLING.md`(ジョブ状態遷移)
- twf: `C:\Users\user\source\repos\panzoux\twf\Services\`(BackgroundOperations、ファイル操作の実装)
- zurari: `C:\Users\user\source\repos\panzoux\zurari\Program.cs`(列ナビゲーションの状態、
  ドライブ一覧、シンボリックリンク循環防止)

### タスク

#### 1. Core: ブラウズ状態のモデル化
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

#### 2. Core: ナビゲーションMsgとTransition
- [ ] Msg追加: `CursorUp/Down/PageUp/PageDown/Home/End`, `FocusLeft/Right`,
  `EnterDirectory`, `GoToParent`, `Refresh`,
  `DirectoryLoaded(columnIndex, path, entries)`, `DirectoryLoadFailed(columnIndex, path, error)`
- [ ] Effect追加: `ReadDirectory(columnIndex, path)`
- [ ] zurari の挙動を踏襲: Enterで右に列が伸びる、親移動で列が縮む、
  ドライブ一覧がルート列(zurari Program.cs 参照)
- [ ] `GenMsg`(Core.Tests)に全Msgを追加
- テスト: 例示ベース(各Msg) + property(任意Msg列で不変条件維持、
  DirectoryLoaded が古い列indexに届いても壊れない=レース耐性)

#### 3. Runtime: Worker Pool と Effect 実行
- [ ] `IEffectRunner`: Effect を受け、結果 Msg を `ChannelWriter<Msg>` に書く
- [ ] `WorkerRuntime`: `Channel<Effect>` で受け、固定数ワーカー(まず2)で実行、
  `CancellationToken` で全体停止。`ReadDirectory` 実装(twf/zurariの列挙コードを移植:
  アクセス拒否・長いパス・シンボリックリンクの扱い含む)
- [ ] UIスレッドへの合流は Runtime では抽象化(`Action<Msg> post`)。WPF Dispatcher は Phase 3 で App が渡す
- テスト: 一時ディレクトリで ReadDirectory → DirectoryLoaded が届く、
  アクセス不能パスで DirectoryLoadFailed、キャンセルで停止

#### 4. 配線ハーネス(ヘッドレスloop)
- [ ] `MessageLoop`(Runtime): Msgキュー → Transition.Apply → Effect投入 → 新State通知、を回す小さなクラス
- テスト: 「初期State + Refresh → ReadDirectory実行 → DirectoryLoaded → Entriesが埋まる」を
  実ファイルシステム(一時ディレクトリ)でE2E

### 注意

- LINQは可(zurari本体はLINQ-free方針だったが本プロジェクトでは許可)。ホットパスのみ配慮
- 例外はEffect実行の境界で捕捉し必ず `*Failed` Msg に変換。ワーカーを死なせない
- 公開APIにXMLコメント(既存ファイルの粒度に合わせる)

---

> 原典: `docs/superpowers/plans/2026-07-08-phase2-columnbrowser-control.md`

## Phase 2 — ColumnBrowser コントロール(偽データで完成させる)

前提: `CLAUDE.md` と `docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md` を先に読む。
このフェーズは **Phase 1(Core/Runtime)と独立**。ファイルシステムには一切触らず、
偽データだけで multi-column GUI 部品を完成させる。「ここが精巧にできていれば、あとは積み木」。

このplanは自己完結している(ローカルの参照リポジトリ rwf/twf/zurari は不要)。

### 実現するUX(zurari = Finder風ミラーカラムの要点)

- 左端の列がルート(実運用ではドライブ一覧)。各列は1つのディレクトリの中身。
- フォーカス列でカーソル(1行ハイライト)を↑↓で動かす。ディレクトリ上で Enter または →
  を押すと右に新しい列が開く。← / Backspace で親(左の列)に戻る。
- フォーカス列より左の列は「パスの履歴」で、その列で選択中のエントリがハイライトされたまま残る。
- 列の合計幅がビューポートを超えたら横スクロール。フォーカス列は常に見える位置へ自動スクロール。
- 各列は境界ドラッグで幅を変えられる。右端にプレビューペイン用の領域(Phase 2 では空のプレースホルダ)。
- マーク(複数選択)はエントリの視覚状態として表示する(マーク操作のロジックは持たない)。

### 絶対規約: ColumnBrowser は「馬鹿なビュー」

- **State in, input out。** 表示内容はすべて外から与えられたビューモデルの反映。
  キー/マウス入力は自分で解釈せずイベントとして外に出す(カーソル移動すら自分で決めない)。
  唯一の例外は列幅(ビューローカルな状態。変更イベントで外に通知し、永続化は呼び出し側)。
- Zurari.Core / Runtime / Shell を参照しない(Phase 3 で Core の State→VM アダプタを App 側に書く)。
- `System.IO` 禁止(BannedSymbols.txt を新プロジェクトにも置く)。タイマー・非同期処理も持たない。

### 契約(出発点。実装の都合で調整可、ただし境界は変えない)

```csharp
// Zurari.Controls
public sealed class ColumnBrowser : Control
{
    // state in
    public IReadOnlyList<ColumnVm>? Columns { get; set; }   // DP。スナップショット差し替え or INPC、選んだ方式をdocコメントに明記
    // input out(イベント引数に列index/エントリindexを含める)
    public event ...  CursorMoveRequested;    // Up/Down/PageUp/PageDown/Home/End
    public event ...  ColumnFocusRequested;   // Left/Right/クリックによる列フォーカス
    public event ...  EntryActivated;         // Enter / ダブルクリック
    public event ...  NavigateUpRequested;    // Backspace / ←(ルート列以外)
    public event ...  EntryPointerPressed;    // クリック(修飾キー付き。マーク操作はホストが解釈)
    public event ...  ColumnWidthsChanged;    // 幅の永続化用
}

public sealed record ColumnVm(string Title, IReadOnlyList<EntryVm> Entries,
                              int CursorIndex, bool IsFocused);
public sealed record EntryVm(string Name, EntryKind Kind, bool IsMarked,
                             string? SizeText, string? DateText);
public enum EntryKind { Drive, Directory, File }
```

### 成果物

| 追加 | 内容 |
|---|---|
| `src/Zurari.Controls` | WPF class library (net8.0-windows, `<UseWPF>true</UseWPF>`)。ColumnBrowser本体 |
| `src/Zurari.Gallery` | WPF exe。開発・目視確認用ハーネス(App とは別。App は Phase 3 で配線) |
| `tests/Zurari.Controls.Tests` | xunit + Xunit.StaFact。純粋ヘルパーの単体テスト + STAスモークテスト |
| 変更 | `Zurari.App` が Controls を参照。Arch.Tests に Controls の層テスト追加 |

### タスク(上から順に。各タスク完了 = テスト green + コミット)

#### 1. プロジェクト追加と骨格
- [ ] Zurari.Controls / Zurari.Gallery / Zurari.Controls.Tests を作成し sln に追加。
      Controls に BannedSymbols.txt(Core と同内容の I/O 禁止 + Task/Thread 禁止)を置く
- [ ] Arch.Tests に追加: Controls は Zurari.* の他層に依存しない。
      注意: Arch.Tests が Controls を参照すると net8.0-windows 化が必要(してよい)
- [ ] 空の ColumnBrowser(Control 派生、Generic.xaml のテンプレート)が Gallery に表示される

#### 2. 列レイアウトと仮想化
- [ ] 列の水平配置 + 横スクロール + 列幅ドラッグ(最小幅クランプ)
- [ ] 各列のエントリリストを UI 仮想化(VirtualizingStackPanel recycling モード推奨)。
      **10万件の列でスクロールが引っかからないこと**が受け入れ条件
- [ ] 長い名前は TextTrimming(CharacterEllipsis)。CJK混在名で崩れないこと
- テスト: STAスモーク = 3列×10万件を Measure/Arrange して realized container 数が
  画面分+バッファに収まる(数百以下)ことを assert

#### 3. カーソル・フォーカスの表示と自動スクロール
- [ ] CursorIndex 行のハイライト、IsFocused 列の視覚差、IsMarked の視覚状態
- [ ] Columns 差し替えで CursorIndex 行が見えるようスクロール(ScrollIntoView 相当)
- [ ] フォーカス列が見える位置へ横スクロール
- テスト: STAで Columns を差し替えてカーソル行が viewport 内にあることを検証

#### 4. 入力→イベント変換
- [ ] キー: ↑↓/PageUp/PageDown/Home/End → CursorMoveRequested、←→ → ColumnFocusRequested
      または NavigateUpRequested/EntryActivated(zurariの流儀: →はディレクトリ上なら潜る)、
      Enter → EntryActivated、Backspace → NavigateUpRequested
- [ ] マウス: クリック=EntryPointerPressed(+列フォーカス要求)、ダブルクリック=EntryActivated、
      ホイール=縦スクロール(イベント化せずビューで処理してよい: スクロール位置はビューローカル)
- [ ] コントロール自身は Columns を書き換えない(イベントを出すだけ)ことをコードレビューで保証
- テスト: STAで KeyDown を注入し、期待イベントが期待引数で発火することを検証

#### 5. Gallery を完成させる(目視確認の道具)
- [ ] メモリ内の偽ディレクトリツリー(決定的な擬似乱数で生成)+ 小さなreducer:
      イベントを受けて ColumnVm 群を作り直して差し替える(Phase 3 の App 配線の雛形になる)
- [ ] データセット切替: Small(20件) / Huge(10万件) / CJK+超長名 / Empty / 深い階層(8列)
- [ ] ステータスバーに最後に発火したイベントを表示(イベント配線のデバッグ用)
- [ ] プレビューペイン領域のプレースホルダ(固定幅の空Border)

### 検証

- Windows: `scripts/check.ps1` green + Gallery 起動して全データセットで
  キーボードナビ・リサイズ・スクロールを目視確認
- **Linux(リモート環境)の場合**: WPF は実行不可。保証範囲は
  `dotnet build Zurari.sln`(EnableWindowsTargeting 設定済み)green +
  `dotnet test tests/Zurari.Core.Tests tests/Zurari.Runtime.Tests` green まで。
  STAテストは `[StaFact]` で書き(Windowsでのみ実行される)、最終確認はWindows側で
  `scripts/check.ps1` を実行する。未実行の検証は README または PR 説明に明記すること

---

> 原典: `docs/superpowers/plans/2026-07-14-phase3-real-filesystem-wiring.md`

## Phase 3 — 実ファイルシステム接続(App配線)

前提: Phase 1(Core状態マシン+Runtime Worker Pool)と Phase 2(ColumnBrowser)が完了していること。
このフェーズで初めて Zurari.App が「動くファイラー」になる: 実FSの閲覧/移動/更新/ドライブ一覧。

配線のみ。**Appにロジックを書かない**(判断はすべてCore、I/OはすべてRuntime)。

### 構成(composition root)

```
ZurariApp.OnStartup:
  state = AppState.Initial
  runtime = WorkerRuntime(...)                    // Phase 1 の成果物
  loop = MessageLoop(state, runtime,
           post: msg => Dispatcher.BeginInvoke(() => loop.Dispatch(msg)),
           onStateChanged: s => mainWindow.Render(s))
  loop.Dispatch(new Msg.Refresh())                // ルート列(ドライブ一覧)を読む
```

### タスク

#### 1. StateProjection(AppState → ColumnVm 群)+ App.Tests
- [ ] `src/Zurari.App/StateProjection.cs`: `static IReadOnlyList<ColumnVm> Project(AppState)`。
      純関数。Core の Column/Entry を Controls の ColumnVm/EntryVm に写像
      (SizeText は人間可読形式 KB/MB/GB、DateText は `yyyy-MM-dd HH:mm`、
      Kind: Drive/Directory/File)
- [ ] `tests/Zurari.App.Tests`(新規、net8.0-windows): 写像の単体テスト
      (空、通常、CursorIndex/IsFocused/IsMarked の透過、サイズ・日付の整形)
- Arch.Tests 追加: App は System.IO.File/Directory に依存しない(既存BannedSymbolsの二重化)

#### 2. MainWindow に ColumnBrowser を配置し、State→再描画を配線
- [ ] MainWindow.xaml: ColumnBrowser を全面配置(プレビュー領域プレースホルダは維持)
- [ ] `Render(AppState)`: StateProjection.Project して `browser.Columns` を差し替えるだけ
- [ ] ウィンドウタイトルにフォーカス列のパスを表示

#### 3. 入力イベント → Msg 配線
- [ ] ColumnBrowser の各イベントを Msg に変換して loop.Dispatch:
      CursorMoveRequested→CursorUp/Down/PageUp/PageDown/Home/End、
      EntryActivated→EnterDirectory、NavigateUpRequested→GoToParent、
      ColumnFocusRequested→FocusColumn、EntryPointerPressed→(クリック=カーソル移動要求。
      修飾キーのマーク解釈は Phase 5)
- [ ] F5 で Refresh
- [ ] 変換は 1イベント=1Msg の単純写像に保つ(判断をAppに持ち込まない)

#### 4. E2E確認と公開産物
- [ ] 起動→ドライブ一覧→ディレクトリ潜行→親に戻る→F5更新、を実機で確認
- [ ] アクセス拒否ディレクトリでクラッシュせずエラー表示状態になる
- [ ] `dotnet publish src/Zurari.App -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish` が成功し、exe単体で起動する

### 検証

- `scripts/check.ps1` green(全タスク共通の完了条件)
- タスク4の手動チェックリストの結果を最終報告に記載

---

> 原典: `docs/superpowers/plans/2026-07-16-phase4-shell-integration.md`

## Phase 4 — Windowsシェル統合(アイコン / ゴミ箱削除 / コンテキストメニュー / DnD)

前提: Phase 0〜3 完了(mainマージ済み)。実FSブラウズが動く状態からの積み増し。
参照: `C:\Users\user\source\repos\panzoux\win_ctxmenu\ctxmenu.cpp`(IContextMenuホストのC++実装例)。

### 設計判断(このフェーズで確定)

- **Zurari.Shell は net8.0-windows + UseWPF に変更してよい。** HwndSource/ImageSource を扱うため。
  依存規約は不変: Shell → Core のみ(Runtime/App/Controls 参照禁止、Arch.Tests 既存テストが監視)。
- **シェル系 Effect の実行者は Zurari.Shell 内の `ShellEffectExecutor`。**
  Runtime は Shell を参照できない(Arch規約)ため、App の composition root が
  Effect の型で振り分ける: FS系 → WorkerRuntime.Submit、シェル系 → ShellEffectExecutor.Submit。
  振り分けは型スイッチ1式のみ(判断ロジック禁止は維持)。
- **interop は手書き P/Invoke + ComImport**(CsWin32 は使わない。生成器より可読で移植例に近い)。
- **ファイル転送そのものは Phase 4 では書かない。** DnD 受け入れは SHFileOperation/IFileOperation に
  委譲(シェル標準の進捗ダイアログが出る)。内製ジョブエンジンは Phase 5。

### タスク(1タスク = 1 sonnet dispatch = 1コミット)

#### 1. シェルアイコン
- Controls: `EntryVm` に `ImageSource? Icon`(null なら非表示、既存テスト互換)。
  ItemTemplate 左端に 16x16 Image。
- Shell: `ShellIconCache` — 拡張子(+Directory/Drive種別)→ BitmapSource。
  `SHGetFileInfoW` + `SHGFI_USEFILEATTRIBUTES|SHGFI_ICON|SHGFI_SMALLICON`(ディスク非接触)、
  HICON → `Imaging.CreateBitmapSourceFromHIcon` → `Freeze()` → `DestroyIcon`。
  ConcurrentDictionary でキャッシュ。exe/lnk 等「実体依存アイコン」は v1 では拡張子代表で妥協。
- App: StateProjection に icon resolver(`Func<Entry, ImageSource?>`)を注入。
- テスト: STA — ".txt"/".exe"/Directory で非null・Frozen・同一拡張子はキャッシュ同一参照。

#### 2. ゴミ箱削除
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

#### 3. シェルコンテキストメニュー(最難関、win_ctxmenu 移植)
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

#### 4. Explorer 連携 DnD
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

### 検証

- 各タスク: `scripts/check.ps1` green + 記載のテスト。
- フェーズ完了時の手動チェックリスト: アイコン表示 / Del→ゴミ箱(元に戻せる) /
  右クリックメニュー(開く・プロパティ・送る) / Explorer へドラッグでコピー /
  Explorer からドロップでコピー(シェル進捗ダイアログ)→ 列が自動更新。

---

> 原典: `docs/superpowers/plans/2026-07-19-phase5-jobs-and-preview.md`

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
| **ドライブのプレビュー** | あり | 大 | Phase 6 |
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

別セッション「MP4 サムネイル生成ツール動作確認」で方針が確定し、**一部は実装済み(未コミット)**。

**確定した方針 = zrr 方式**: 依存を **ffmpeg 一本**に絞り、指定時刻のフレームを画像として抽出して
**サムネイルキャッシュへ保存**する構成を基本とする。ffmpegthumbnailer は不採用
(Windows の Unicode パス対応を持つ有力な fork が無く、ffmpeg 本体と重複した対応を抱える
合理性が低いため)。ffmpeg の Windows CLI は `GetCommandLineW` / `CommandLineToArgvW` を使うので
日本語パスをそのまま扱える。

| 項目 | 状態 |
|---|---|
| ffmpeg 一本化(ffmpegthumbnailer 廃止) | `[x]` 実装済み(未コミット) |
| 失敗理由の表示(`ThumbnailOutcome` で理由を返す) | `[x]` 実装済み(未コミット) |
| タイムアウト見直し(4秒 → 20秒、4秒間隔のポーリング) | `[x]` 実装済み(未コミット) |
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
