# Phase 0 — 設計 + ソリューション骨格

**状態**: `[x]` 完了
**実施**: 2026-07-08
**主なコミット**: `fdf466c`
**位置づけ**: [roadmap.md](roadmap.md) の Phase 0

> 以下は**着手時に確定させたプランの原文**である(このファイルは記録用に原典を
> そのまま複製したもの。原典: `../docs/superpowers/specs/2026-07-08-zurari-w-architecture-design.md`)。
> 実装中に判明した事実や設計変更はコミットメッセージとコード内コメントに記録されており、
> **原文と実装に差異がある箇所がある**。現在の仕様を知りたい場合はコードを参照すること。

---

## zurari_w — zurari for Windows (WPF GUI multi-column filer) 設計 + 立ち上げ計画

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
