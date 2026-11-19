---
id: 22
title: '不変条件: 各列は自分のペインの View で導出されている'
status: backlog
priority: medium
created: 2026-11-19T10:38:20.0496545+09:00
updated: 2026-11-19T10:38:20.0496545+09:00
tags:
    - phase7
    - test
class: standard
---

現状: CheckColumns は Entries を pane.View と CollapsedGroups で導出した結果と照合していない。他ペインの View で導出する誤り(7a.3 の主リスク)を検出できない(7a 最終レビュー)
次アクション: 不変条件を追加。可能ならリネームの At が生きた列を指すことも
