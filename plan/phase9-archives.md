# Phase 9 — アーカイブ操作

**状態**: `[ ]` 未着手(ガワのみ。着手時にこのファイルへタスクを書き下す)
**前提**: Phase 8 完了(`Location` 抽象化が済んでいること)
**位置づけ**: [roadmap.md](roadmap.md) の Phase 9

---

## 目的

書庫ファイルをフォルダと同じように閲覧・展開・作成できるようにする。

## 方式(決定済み)

**標準は zip のみ**、`System.IO.Compression`(.NET 標準ライブラリ)で対応する。
7z / rar / tar / lzh 等は **ユーザーが 7-Zip を導入していれば使える**という検出方式にする。
7z.dll は**同梱しない**。

理由: 現在の配布方針(framework-dependent 単一 exe 419KB — [build.txt](../build.txt))を崩さない。
動画サムネイル(ffmpeg)と同じ「あれば使う、無ければ縮退」で一貫させる。

## スコープ

### やること

- [ ] `IArchiveProvider` 相当のインターフェイス(twf からの移植)
      操作: 一覧 / 展開(全体) / 展開(選択エントリ) / 圧縮 / アーカイブ内削除
- [ ] `ZipArchiveProvider` — `System.IO.Compression` 実装(標準・常に利用可)
- [ ] `SevenZipProvider` — 7-Zip 検出時のみ有効
- [ ] アーカイブ内ブラウズ — `Location.Archive(archivePath, innerPath)` を列として開く
- [ ] 展開・圧縮を既存 JobEngine に載せる(進捗・キャンセル・競合確認を共通化)
- [ ] アーカイブ内容のプレビュー(カーソルが書庫ファイル上にあるとき、中身の一覧を表示)
- [ ] 7-Zip 未導入時の縮退表示(zip 以外は「7-Zip が必要」と明示する)

### やらないこと

- パスワード付き書庫の作成 UI(閲覧時のパスワード入力は要検討)
- 分割書庫

## 設計メモ

**7-Zip の検出方法を決める必要がある**。候補:

1. `7z.exe` を PATH / レジストリ(`HKLM\SOFTWARE\7-Zip`)から探し、**プロセス起動**で使う
   → ffmpeg と同じ方式。実装が単純、ライセンス面も明快(LGPL の動的リンク問題を回避)。
   進捗取得は標準出力のパースが必要。
2. `7z.dll` を見つけて **P/Invoke** する
   → 進捗・キャンセルの制御が細かくできるが、COM インターフェイス実装が必要で重い。
   twf は SevenZipSharp(NuGet)経由でこの方式を採っている(`..\twf\Services\SevenZipArchiveProvider.cs`)。

**推奨は 1**(プロセス起動)。ffmpeg と実装パターンが揃い、`VideoThumbnailer` の
検出・起動・タイムアウト処理をほぼそのまま流用できる。

- アーカイブ内エントリに対する操作(コピー等)は、一旦一時ディレクトリへ展開してから実ファイル
  操作に落とすのが素直。ジョブの「仮想 → 実」変換として Phase 8 の設計に含めておく。

## 参照

- twf のインターフェイス: `..\twf\Services\IArchiveProvider.cs`
- twf の 7-Zip 実装: `..\twf\Services\SevenZipArchiveProvider.cs`(SevenZipSharp 使用)
- twf の zip 実装: `..\twf\Services\ZipArchiveProvider.cs`
- 既存の外部ツール検出パターン: `src\Zurari.Runtime\VideoThumbnailer.cs`

## 検証

- `scripts/check.ps1` green
- 手動: zip の閲覧・展開・作成、7-Zip 導入時の 7z/rar 閲覧、未導入時の縮退表示
