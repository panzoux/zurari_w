# zurari_w — zurari for Windows (WPF multi-column filer)

GUI版zurari。Windows特化、multi-column(Finder風)ファイラー。
アーキテクチャは rwf(Rust版)の純粋Transitionパターンの C# 移植。

## コマンド

```powershell
scripts/check.ps1          # format検証 + build + 全テスト + plan status検証。完了の定義 = これがgreen
scripts/status.ps1         # plan/*.md のテスト数と `(this commit)` を実際の値に書き換える
dotnet format Zurari.sln   # フォーマット修正
dotnet build Zurari.sln
dotnet test Zurari.sln
# 配布用単一exe(要 .NET Desktop Runtime):
dotnet publish src/Zurari.App -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish
```

pre-commit フックが check.ps1 を実行する(`git config core.hooksPath scripts/hooks` 設定済み)。
**フックを --no-verify で回避しない。エラーは原因を直す。**

## アーキテクチャ(絶対規約)

一方向データフロー。UIはStateの投影。

```
入力 → Msg → Transition.Apply(state, msg) → (新State, Effect[])
                                                  │
   UI再描画 ← State投影 ← Msg(結果/進捗) ← Worker Pool が Effect を実行
```

| プロジェクト | 役割 | 依存 |
|---|---|---|
| Zurari.Core | 純粋状態マシン(AppState/Msg/Effect/Transition)。I/O・スレッド・時計・Task禁止 | なし(BCL純粋部のみ) |
| Zurari.Runtime | Worker Pool。Effectを実行しMsgを返す。ファイルI/Oはここだけ | Core |
| Zurari.Shell | Win32/COM interop(コンテキストメニュー、ゴミ箱、アイコン、DnD補助) | Core |
| Zurari.App | WPF。ColumnBrowserコントロール+配線のみ。ロジック禁止 | Runtime, Shell |

規約は機械的に強制される(直そうとせず従う。違反はビルドエラー):
- **BannedApiAnalyzers**: 各プロジェクトの `BannedSymbols.txt`。Core/App/Shell で `File`/`Directory` 等は使えない。
  I/Oが必要になったら Effect を定義して Runtime に実装する — それが唯一かつ一番楽な道。
- **tests/Zurari.Arch.Tests**: 層違反(RuntimeがWPFに触る等)をテストで検出。
- `Directory.Build.props`: TreatWarningsAsErrors + AnalysisLevel latest-all。
  アナライザーを無効化・suppressする場合は .editorconfig に理由コメント付きで(乱用しない)。

新しい Msg を足したら `tests/Zurari.Core.Tests/TransitionTests.cs` の `GenMsg` に必ず追加する
(property-based test が自動でカバーするため)。

## 参照資産(このリポジトリの外、`C:\Users\user\source\repos\panzoux\`)

- **rwf/** (Rust) — アーキテクチャの本家。docs/ARCHITECTURE.md, JOB_HANDLING.md 参照
- **twf/** (C#) — ロジック移植元: ファイル操作・ジョブ・検索・アーカイブ(Services/ 配下)
- **zurari/** (C#) — multi-column UX・Unicode幅処理・マジックナンバー種別判定(Program.cs 単一ファイル)
- **win_ctxmenu/** (C++) — IContextMenu ホストの実装例(Phase 4 で C# に移植)

## 進め方

- 設計: docs/superpowers/specs/ / **フェーズ計画: plan/(1フェーズ=1ファイル)+ plan/roadmap.md**
  docs/superpowers/plans/ は着手時の原文(日付付きファイル名)で、更新されない。現行の計画は plan/ を見ること
- タスク完了の条件: planの項目を満たす AND `scripts/check.ps1` green。証跡(テスト出力)を残す
- **plan/roadmap.md と plan/phase*.md の「N テスト green」は手で書かない。**`scripts/status.ps1` が
  書き、check.ps1 が古ければ落とす(format と同じ「検証はcheck、修正は別コマンド」の形)。
  Status 表の commit 欄は、その行を書く時点ではまだ存在しないので `(this commit)` と書いておき、
  コミット後に `scripts/status.ps1` が HEAD の短縮ハッシュに置き換える
- コミットは小さく。コミット前に check.ps1(フックが強制)
