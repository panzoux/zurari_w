# Phase 7 — 二画面分割 + 検索・ジャンプ系

**状態**: `[ ]` 未着手(ガワのみ。着手時にこのファイルへタスクを書き下す)
**前提**: Phase 6 完了
**位置づけ**: [roadmap.md](roadmap.md) の Phase 7

---

## 目的

zrr(TUI版)の操作体験の核を GUI に持ち込む。二画面分割と、キーボードでの高速な絞り込み・
ジャンプ。ここまで来ると「zrr の GUI 版」と呼べる状態になる。

## スコープ

### やること

**二画面分割**

- [ ] 上下(または左右)分割のトグルと、ペイン間のフォーカス切替
- [ ] 各ペインが独立した multi-column 列群と独立したカーソル/マークを持つ
- [ ] ペイン間のコピー/移動(既存ジョブエンジンを流用)、ドラッグ&ドロップ
- [ ] 分割状態・分割比率の永続化(`UserSettingsStore`)

**検索・ジャンプ**

- [ ] インクリメンタル検索(列内の絞り込み・ジャンプ)
- [ ] migemo 対応(ローマ字入力で日本語ファイル名を検索)
- [ ] Leap ナビゲーション(絞り込みモード)
- [ ] ブックマーク(登録・一覧・ジャンプ)
- [ ] 履歴ジャンプ(訪問したディレクトリの履歴)
- [ ] パス直接入力(アドレスバー)
- [ ] 操作ログの表示

### やらないこと

- 検索結果を「仮想ディレクトリの列」として開く機能 → Phase 8 の基盤が要るので後回し。
  本フェーズの検索はあくまで **列内の絞り込み・ジャンプ**(zrr と同じ挙動)に留める。

## 設計メモ

**二画面分割が最大の設計判断**。`AppState` は現状 `Columns` + `FocusedColumn` を1組しか持たない。
zrr は `Pane` を導入して `ScreenState.FocusedPane` で切り替えている(`..\zurari\Program.cs` の
`sealed class Pane`)。同じ構造を C# の immutable state に写すなら:

```
AppState { ImmutableArray<Pane> Panes; int FocusedPane; ... }
Pane { ImmutableArray<Column> Columns; int FocusedColumn; }
```

これは `Transition` のほぼ全ハンドラ(現在 `state.Columns[...]` を直接触っている)に波及する。
Phase 8 の `Location` 抽象化と**どちらを先にやるか**は着手時に再検討する価値がある
(両方とも Core 全体に触る大きなリファクタなので、まとめてやる方が安いかもしれない)。

- 検索 UI は新規のオーバーレイ部品(検索バー)。Controls 層に「馬鹿なビュー」として追加し、
  入力は既存同様イベントで外に出す。
- migemo は辞書ファイルが要る。zrr は `MigemoProvider` を持つ(`..\zurari\Program.cs`)。
  辞書の配布方法(同梱するか、あれば使うか)を決める必要がある。
- ブックマーク/履歴は `UserSettingsStore`(Phase 5 で追加済み)を拡張して永続化する。

## 参照

- zrr のペイン構造: `..\zurari\Program.cs` の `Pane` / `ScreenState` / `ToggleSplitAsync`
- zrr の検索: 同 `SearchState` / `SearchHelper` / `MigemoProvider`
- zrr の Leap: 同 `LeapState` / `EnterLeapMode` / `HandleLeapKeyAsync`
- zrr のブックマーク: 同 `Bookmarks` / `BuildBookmarkAndHistoryList`

## 検証

- `scripts/check.ps1` green
- 手動: 二画面での相互コピー/移動、CJK ファイル名の migemo 検索
