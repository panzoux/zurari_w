# Phase 2 — ColumnBrowser コントロール

**状態**: `[x]` 完了
**実施**: 2026-07-08〜15
**主なコミット**: `771432c`, `39c762a`, `021767e`, `d54f71a`, `e303142`
**位置づけ**: [roadmap.md](roadmap.md) の Phase 2

> 以下は**着手時に確定させたプランの原文**である(このファイルは記録用に原典を
> そのまま複製したもの。原典: `../docs/superpowers/plans/2026-07-08-phase2-columnbrowser-control.md`)。
> 実装中に判明した事実や設計変更はコミットメッセージとコード内コメントに記録されており、
> **原文と実装に差異がある箇所がある**。現在の仕様を知りたい場合はコードを参照すること。

---

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
