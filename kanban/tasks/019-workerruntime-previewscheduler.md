---
id: 19
title: WorkerRuntime のプレビュー制御を PreviewScheduler に切り出す
status: backlog
priority: medium
created: 2026-11-19T04:50:40.7689324+09:00
updated: 2026-11-19T04:50:40.7689324+09:00
tags:
    - refactor
class: standard
---

現状: WorkerRuntime.cs が約1700行。プレビューのスロット・最高世代・タスク集合・開始/取消/Dispose が同居(7a.9 レビュー指摘)
次アクション: 内部クラス PreviewScheduler(約150行)に抽出し、ファイル無しで単体テストできるようにする
完了条件: 抽出のみで挙動不変、check.ps1 green
