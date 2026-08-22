# Phase 3 — 実ファイルシステム接続

**状態**: `[x]` 完了
**実施**: 2026-07-16
**主なコミット**: `97f8613`, `dade497`
**位置づけ**: [roadmap.md](roadmap.md) の Phase 3

> 以下は**着手時に確定させたプランの原文**である(このファイルは記録用に原典を
> そのまま複製したもの。原典: `../docs/superpowers/plans/2026-07-14-phase3-real-filesystem-wiring.md`)。
> 実装中に判明した事実や設計変更はコミットメッセージとコード内コメントに記録されており、
> **原文と実装に差異がある箇所がある**。現在の仕様を知りたい場合はコードを参照すること。

---

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
