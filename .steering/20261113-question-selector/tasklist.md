# tasklist.md — 出題する単語の選択(QuestionSelector)

## 状態
- フェーズ: 検証(⑥ 3体レビュー待ち)
- 最終更新: 2026-11-13
- 次にやること: ⑥ 実装の3体レビュー

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [x] ② プランのレビュー(design-reviewer・security-reviewer)— P1 9 を反映、3点はユーザーが決定
- [x] ③ テスト計画のレビュー(edge-case-reviewer)— P0 1・P1 13 を反映

### 実装(tdd スキル)
- [x] T1: WeakWordScorer
- [x] T2: ModeEligibility
- [x] T3: QuestionSelector(候補・分類・枠・補い・新規の順・モード・混ぜる・0語の理由)
- [x] T4: SessionManager.Start とのつながり、性能の目安

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(Core 427 / Persistence 75。Red 59件を確認してから実装。5,000語の選択は約54ms)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [x] LastWrongAt に再練習を含める理由を DECISIONS.md に
- [ ] P2(②③): Tier 別の優先順位(v1.2 2章 B)は将来
- [x] decisions.md の決定を docs/DECISIONS.md へ
- [x] 学びを CLAUDE.md の「学びメモ」に1行追記(ImmutableArray の比較を testing.md に昇格)

## メモ
- 2026-11-13: ① の質問で、苦手度の式を採用・出題順は混ぜる・苦手のみは足りなければある分だけ と決まった
- 2026-11-13: feature/session-management(PR #6)の上に積んでいる(PlannedQuestion を使うため)
