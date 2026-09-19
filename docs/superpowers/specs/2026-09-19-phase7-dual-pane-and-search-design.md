# Phase 7 — 二画面分割 + 検索・ジャンプ系 設計

**日付**: 2026-09-19
**状態**: 設計承認済み(ユーザー確認 2026-09-19)
**計画**: [plan/phase7-dual-pane-and-search.md](../../../plan/phase7-dual-pane-and-search.md)

## Context

zrr(`..\zurari\Program.cs`)は二画面・`/` 検索・Leap・ブックマーク/履歴・操作ログを実装済みで、
ユーザーが数週間実使用して評価が高い。本フェーズはそれを zurari_w の純粋 Transition アーキテクチャへ
移す。挙動は原則 zrr に倣い、GUI・immutable state に合わせて変える箇所だけを本書で明示する。

参照した zrr の実装と仕様:

- 状態: `Pane` / `ScreenState` / `SearchState` / `LeapState`
- 分割: `ToggleSplitAsync` / `SwapPanes` / `SyncPaneToOtherPathAsync` / `StartCopyOrMoveAsync` / `ResolvePaneDirectory`
- 検索: `HandleSearchKeyAsync` / `SearchHelper` / `MigemoProvider`、spec `2026-06-14-quick-search-design.md`
- Leap: `HandleLeapKeyAsync` / `ApplyLeapFilter` / `EnterDirInLeapAsync` / `TryLeapAutoEnterAsync`、spec `2026-06-22-leap-navigation-design.md`
- ジャンプ: `Bookmarks` / `RecordHistoryVisit` / `RequestHistoryJump` / `ShowListSelect` / `ListFilter`
- 二画面: spec `2026-07-06-dual-pane-copy-move-design.md`(「ペイン化は描画ではなく状態の問題」)
- 操作ログ: `OperationLog`(リング 1000 件 + ファイル、10MB でローテート)

## 決定事項(ユーザー確認済み)

| 項目 | 決定 |
|---|---|
| 分割順 | **7a 画面分割**(前段 Pane 抽出 → 後段 分割 UI)→ **7b インクリメンタル検索 / Leap**(マッチャ + migemo + `/` 検索 + Leap)→ 7c ジャンプ一覧 → 7d 操作ログ。計画はサブフェーズごとに `plan/phase7a-*.md` 等の別ファイル、`plan/phase7-dual-pane-and-search.md` は要約 |
| 分割方向 | 上下(zrr と同じ) |
| プレビュー | **ペインごと**。各行が `[列 \| スプリッタ \| プレビュー]` を持つ。`PreviewState` は `Pane` へ移る |
| 並べ替え・隠しファイル | **ペインごと**。`ViewOptions` は `Pane` へ移る |
| 永続化 | 分割の開閉・分割比率・位置(上/下)ごとの `ViewOptions`。場所は復元しない(両ペインともドライブ一覧から) |
| アドレッシング | `ColumnRef(PaneId, Column)`。ペインは安定 ID を持つ |
| ペイン間コピー/移動 | `c` / `m`(zrr と同じ) |
| migemo | cmigemo があれば使う → 無ければ内蔵(rustmigemo の C# 移植 + BSD 辞書) |
| ブックマーク | 既存のピン(6d)をブックマークとみなす。別概念は作らない |

---

## 7a 前段 — Pane 抽出(見た目の変化なし)

### 状態

```csharp
public readonly record struct PaneId(int Value);
public readonly record struct ColumnRef(PaneId Pane, int Column);

public sealed record Pane(PaneId Id, ImmutableArray<Column> Columns, int FocusedColumn)
{
    public PreviewState Preview { get; init; } = PreviewState.Initial;  // AppState から移動
    public ViewOptions  View    { get; init; } = ViewOptions.Default;   // AppState から移動
    // 7b で追加: Search, Leap, NeedsRefresh
}

public sealed record AppState
{
    public required ImmutableArray<Pane> Panes { get; init; }   // 1〜2 要素、上が先
    public required int FocusedPane { get; init; }              // Panes へのインデックス
    public int NextPaneId { get; init; }
    // 全ペイン共有(zrr と同じ考え方:「今どこを見ているか」だけがペインに属する):
    // Jobs, NextJobId, Notice, CursorMemory, Rename, InputMode, RevealTarget, CutPending
}
```

- `Columns` / `FocusedColumn` / `Preview` / `View` は `AppState` から消える。
- `CursorMemory` は共有(zrr の `_cursorMemory` と同じ)。どちらのペインで訪れても同じ記憶を使う。
- `InputMode`(並べ替えモード)と `Rename` は共有だが、作用するのはフォーカスペイン。
  `RenameState.ColumnIndex` は `ColumnRef` になる。

### アドレッシング

- Msg / Effect の `int ColumnIndex` を**すべて** `ColumnRef Column` に置き換える
  (`ReadDirectory`、`DirectoryLoaded`/`Failed`、`ShellOpCompleted`/`Failed`、`RenameCompleted`、
  `FolderCreated`、`CursorUp` 等のカーソル系、マーク系、`PasteRequested`、`DropFiles`、`PinFocusedLocation` …)。
  型を変えるのでコンパイラが呼び出し箇所を全部挙げる。
- プレビュー系(`LoadPreview`、`CancelPreview`、`PreviewLoaded`/`Failed`/`CapacityLoaded`、`RetryPreview`)は
  `PaneId` を持つ。世代番号はペインごと。
- **古い結果の判定**: まず `PaneId` がまだ存在するか、次に従来どおり Location / 世代を照合する。
  閉じた・入れ替えたペイン宛ての結果は、安定 ID によって確実に捨てられる(位置インデックスでは
  入れ替え直後の結果が別のペインに着地する)。
- ヘルパー: `state.PaneOf(PaneId)`、`state.ColumnAt(ColumnRef)`、`state.WithColumn(ColumnRef, Column)`、
  `state.Focused`(フォーカス中の `Pane`)。

### Transition の後処理

- `ReconcileChildColumn` と `ReconcilePreview` は**ペインごと**に、そのペインの旧状態と新状態を比べて走る。
- `RememberFocusedCursor` はフォーカスペインについてのみ、共有 `CursorMemory` へ記録する。
- `Rederive` と並べ替えモードはフォーカスペインの `View` に作用する。

### 不変条件(`CheckInvariants`)

- `Panes` は 1〜2 要素、`PaneId` は重複せず、すべて `NextPaneId` 未満。
- `FocusedPane` は範囲内。
- 既存の列ごとの不変条件(カーソル範囲、`Entries ⊆ AllEntries`、エラー時のメッセージ)を各ペインで検査。

### App

- `StateProjection` は `state.Focused` を投影する(後段で2つ目のブラウザを足す)。
- `ColumnBrowser` のイベントは、発生元のブラウザが属するペインの `PaneId` を付けて Msg にする。

### テスト

- `GenMsg` は `ColumnRef` を生成する。**存在しない `PaneId` 宛ても混ぜ**、古い結果の経路を踏ませる。
- 既存テストは機械的に移植する。
- 完了条件: `scripts/check.ps1` green かつ、アプリの挙動が前段以前と同一。

---

## 7a 後段 — 分割 UI

### Msg(すべて `GenMsg` に追加)

| Msg | キー | 挙動 |
|---|---|---|
| `ToggleSplit` | Ctrl+W | 開: 新しい `PaneId` の下ペインを追加。初期位置はフォーカスペインのフォーカス列の Location、`View` はフォーカスペインの複製。フォーカスは元のペインのまま。閉: フォーカスペインだけを残す(他方への未着の結果は古い結果として捨てられる) |
| `FocusPane(PaneId)` | Tab(他方へ)/ ペイン内のクリック | 分割していなければ Tab は何もしない |
| `SwapPanes` | Shift+O | `Panes` の順を入れ替える。`FocusedPane` は**位置**を保つ(上は上のまま)ので、入れ替わるのは「どちらの内容がアクティブか」。ID はペインと一緒に動く |
| `SyncToOtherPane` | `o` | フォーカスペインを他方のペインの Location に合わせる(同じなら何もしない)。列の積み直しは純粋ヘルパー `JumpTo(PaneId, Location)` で行う。7c のジャンプ系もこれを使う |
| `CopyToOtherPane(bool IsMove)` | `c` / `m` | 下記 |

**`CopyToOtherPane`**:
- 対象: マークがあればマーク集合、無ければカーソル下の1件(Ctrl+C と同じ規則)。
- 宛先: 他方ペインの「カーソル下のディレクトリ」— フォーカス列のカーソルがフォルダ上ならそのフォルダ、
  そうでなければその列の Location(zrr `ResolvePaneDirectory`。列の Location をそのまま使うと
  表示と食い違い1階層上を指すバグが zrr で実際にあった)。
- 以降は `PasteRequested` と同じ経路(`RunFileJob`、衝突確認つき)。
- 拒否して `Notice` を出す場合: 宛先がドライブ一覧、宛先が対象そのもの・対象の配下(自分自身への移動)。
- **分割していない場合**: 7a では `Notice`(「分割していません」)。7c で宛先ピッカーに置き換える。

**見送り**: zrr の Shift+Y(列幅を他方ペインへ複製)。列幅は `ColumnBrowser` のビュー局所状態で、
複製には App→ブラウザの新しい経路が要る。zrr の分割系キーで最も使われないもの。

### App のレイアウト

- 中央の `Grid` を2行 + 横向き `GridSplitter`(`Focusable=False` — 矢印キーを奪う既知の罠)にする。
- 各行は再利用可能な UserControl `PaneView`(`ColumnBrowser | スプリッタ | プレビュー`)1つ。
  2行の実装が乖離しないようにするため。分割していないときは下の行を Collapsed にする。
- 非フォーカスペインは**減光**: カーソル行の強調を灰色系にする(zrr の減光パレットに相当)。
- パスバーとステータスバーは1組のまま、**フォーカスペイン**を表示する。
- キーボードフォーカスは `FocusedPane` に従う。`ProjectionCache` はペインごとに持ち、カーソル移動を安く保つ。

### Runtime

- プレビューのスロット(キャンセル可能・最新優先)を **`PaneId` ごと**にする。一方のペインの読み込みが
  他方をキャンセルしない。
- シェルサムネイルの STA スレッドは1本のままだが、「最新優先」をペイン単位で適用する
  (分割を開いた瞬間に両ペインが同時に要求し、片方が飢えるのを防ぐ)。

### 永続化(App 側、`UserSettings`)

- `SplitOpen`、`SplitRatio`(上の行が占める高さの割合)。
- `PaneViews[2]`: 位置(上/下)ごとの並べ替え・隠しファイル設定。既存の単一フィールド
  (`SortMode` 等)を置き換える。**移行**: `PaneViews` の無い設定ファイルは旧フィールドを上ペインへ読む。
- 起動時、`SplitOpen` なら復元系 Msg の後に `ToggleSplit` を投げる。両ペインともドライブ一覧から始まる。

### テスト

- Core: 開閉の往復、閉じた・入れ替えたペイン宛ての古い結果を捨てる、`CopyToOtherPane` の宛先規則と拒否。
- Runtime: 2ペインのプレビューが互いをキャンセルせず独立に読み込まれる。
- ヘッドレス E2E: 分割 → 上から下へファイルをコピー → 下ペインに現れる。
- 手動: 減光、スプリッタのドラッグ、再起動で分割が戻る、ペイン間のドラッグ&ドロップ
  (既存のシェル DnD 経路で動くはず)。

---

## 7b〜7d — 後続サブフェーズが従う決定

各サブフェーズは着手時に短い設計パスを行う。ここに書くのは、それらが守るべき共通の決定。

### 共通マッチャ(7b、Core・純粋)

- `NameMatcher.Compile(query, mode)` → 純粋なデータ仕様。モードは literal / regex / literal+migemo。
- 正規表現は `RegexOptions.NonBacktracking`。利用者が打った病的なパターンで UI スレッドが止まらない。
  不正なパターンは「一致なし」(zrr と同じく `(0)` を赤で)。
- スペース区切りの AND セグメント(`\ ` でエスケープ、順不同)と、順位
  「前方一致 > 部分一致 > migemo(通常の一致が無いときだけ)」は zrr の Leap をそのまま移す。

### migemo(7b、Runtime)

Core は `Effect.ExpandMigemo(PaneId, query)` を出し、Runtime が `Msg.MigemoExpanded(PaneId, query, pattern?)` を返す。
Core は literal の一致を即座に適用し、パターンが届いたら migemo の一致を足す。現在のクエリと違う応答は捨てる。
**Core に時計もデバウンスも要らない**(App が打鍵をまとめてよい)。展開は UI スレッドで走らせない。

提供元は3段:

1. **cmigemo があれば使う**: `migemo.dll` + `migemo-dict` を `<exe>\dict` → `%APPDATA%\zurari_w\dict` →
   zrr の publish フォルダの順に探す。P/Invoke は zrr の `MigemoProvider` と同じ。利用者が自前・大きめの
   SKK 辞書を使い続けられ、かつ我々は GPL の辞書を配布しない。
2. **内蔵フォールバック**: rustmigemo(MIT、oguna、rev `121101d`、`~\.cargo\git\checkouts\rustmigemo-*`)の
   クエリ経路を C# へ移植する — `bit_vector` / `louds_trie` / `compact_dictionary` / `character_converter` /
   `romaji_processor` / `ternary_regex_generator` / `query`(約2000行、辞書ビルダーは不要)。
   出力は .NET の正規表現構文で、`NonBacktracking` でコンパイルできること。
   辞書は wappsw の `assets/migemo-compact-dict.bin`(Mozc + UniDic 由来、BSD、1.4MB)を
   Zurari.Runtime に埋め込みリソースとして同梱し、単体 exe で動く。
   `LICENSE` / `LICENSE_Mozc` / `LICENSE_UniDic` を THIRD-PARTY-NOTICES に載せる(再配布の条件。wappsw ROADMAP 参照)。
3. どちらも無ければ literal のみ(内蔵がある以上、実際には起きない)。

wappsw の実測から引き継ぐこと:
- 1文字のクエリは数十 ms〜0.2 秒かかる。
- **単語単位**の LRU キャッシュ(約100件)。`afx 2` と `afx 3` は `afx` を共有する。
- 起動直後に低優先度で `a`〜`z` を事前展開する。

移植の検証: ローマ字クエリの集合(`nihon`、`kaisha`、`a`、`tt` …)について、Rust の `query` 出力と C# 移植の出力を
比べるゴールデンファイルテスト。ゴールデンは rustmigemo(`examples` / `main.rs`)で一度生成してリポジトリに入れる。

### `/` 検索(7b)

- `Pane.Search { Query, IsRegex, AnchorName, Matches, MatchIndex }`。一致を**強調するだけで隠さない**。
- キーは zrr と同じ: 文字入力 / Backspace、Ctrl+R で regex 切替、↑↓・Ctrl+N/P で一致間を巡回(端で折り返す)、
  Enter で確定(カーソルはその位置)、Esc で取消(`AnchorName` へ戻る)。
- 入力欄はパスバーの位置に出る(2026-09-13 決定)。本物の TextBox なので IME も通る。App はテキストの変化だけを Msg にする。

### Leap(7b)

- フィルタは `Column.Derive` の新しい入力(隠しファイル・折りたたみセクションと同列)。
  `AllEntries`・マーク・部分集合不変条件がそのまま保たれ、zrr の手管理の `VisibleIndices` と
  「非同期読み込み後にフィルタが古いまま」の回復処理(`RecoverStaleLeapFilterAsync`)が不要になる —
  `DirectoryLoaded` が来れば再導出されるだけ。
- `Pane.Leap { Buffer, RootRef, RootCursorName, DirStack }`。バッファ = 辿った道(`mapm/we`)。
- **AND 検索**: ローカルフィルタをスペースで区切り、すべてのセグメントが名前のどこかに含まれるものだけを残す
  (順不同。`map tex` は `tex2map` にも `map2tex` にも一致。`\ ` でリテラルの空白、末尾の空白は無視。zrr の
  `SplitLeapSegments` / `LeapAllSegmentsMatch` と同じ)。前方一致の判定は最初のセグメントで行う。
- 単一のディレクトリ一致は自動で入る。連鎖は**タイマーではなく `DirectoryLoaded` で**進める。
- 一致ゼロは mode B(入力を残し `(no match)` を表示)。
- キーは zrr と同じ: F3 で開始/確定、Esc で取消(開始時の列・カーソルへ)、Enter はその場に着地(入らない)、
  → でディレクトリに入る / ファイルなら確定、← で親へ(ローカルフィルタと `/` を剥がす)、
  Backspace は1文字戻す(`/` を消せば親へ)、Ctrl+U でローカルフィルタ消去、Ctrl+K でルートへ、`/` でカーソル下へ入る。
- LEAP バーはパスバーと同じ位置。
- **プレビュー(ユーザー確認 2026-09-19)**: Leap 中はプレビューを**空にする**(ペイン自体は残し、中身を消す)。
  終了時にカーソル位置のプレビューを読み込む。zrr の実使用で「Leap でカーソルが動くのに、プレビューが
  別のファイルのものを出し続ける」状態が非常に紛らわしかったため、古い内容を残す案は採らない。
  ペインの枠を残すのは、GUI でペインが消えて戻るとレイアウトが揺れるため。

### モード中の再読込の保留(7b)

検索・Leap 中は、`ExternalDirectoryChanged` とジョブ完了による再読込は `Pane.NeedsRefresh` を立てるだけにし、
モード終了時に実行する(zrr の保護規則。再読込が一致インデックスやフィルタを壊すため)。

### ジャンプ一覧(7c)

- **履歴**: Core に置き、セッション限り、上限 100、新しい順。選んだパスが存在しなければ履歴から消す(zrr)。
- **ブックマーク = 既存のピン**。概念は1つで、ドライブペインにも引き続き出る。`b` でピンのピッカー、
  Shift+B は Ctrl+B(カーソル下をピン留め)の別名。
- **ピッカー**: Controls に汎用の絞り込み付き `ListPicker` オーバーレイを1つ作り、共通マッチャ(migemo 含む)で絞る。
  用途は `b`、`h`、分割していないときの `c`/`m` の宛先ピッカー(ピン → 履歴の順)。
- **アドレスバー**: Ctrl+L(Explorer と同じ)でパスバーを編集可能にし、Enter で `JumpTo`。存在しなければ `Notice`。

### 操作ログ(7d)

- JobEngine が項目ごとに1行を Runtime のリング(1000件)と `%APPDATA%\zurari_w\operations.log` に書く
  (zrr の TSV 形式、10MB で `.1` へローテート、書き込み失敗は黙って諦める)。
- Shift+L で Effect 経由で読み込み、`ListPicker` で表示する(確定は閉じるだけ)。

### キー割り当て(Phase 7 で増えるもの)

| キー | 意味 | サブフェーズ |
|---|---|---|
| Ctrl+W | 分割の開閉 | 7a |
| Tab | 他方のペインへフォーカス | 7a |
| Shift+O | ペインの入れ替え | 7a |
| `o` | 他方のペインの場所へ合わせる | 7a |
| `c` / `m` | 他方のペインへコピー / 移動 | 7a(非分割時の宛先ピッカーは 7c) |
| `/` | インクリメンタル検索 | 7b |
| F3 | Leap | 7b |
| `b` / Shift+B | ピン一覧へジャンプ / カーソル下をピン留め | 7c |
| `h` | 履歴へジャンプ | 7c |
| Ctrl+L | アドレスバー | 7c |
| Shift+L | 操作ログ | 7d |

キーを足したら、ステータスバーの文脈ヒント(6e.7)も更新する。一覧としてのヘルプ画面は 6e.9 のまま後回し。

## やらないこと

- 検索結果を仮想ディレクトリの列として開く(roadmap 既定どおり)。
- 分割方向の切り替え(上下固定)。
- 各ペインの最後の場所の復元(7c の履歴と一緒に後で検討)。
- zrr の Shift+Y(列幅の複製)。
