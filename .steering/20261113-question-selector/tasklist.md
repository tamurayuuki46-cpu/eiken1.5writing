# tasklist.md — 出題する単語の選択(QuestionSelector)

## 状態
- フェーズ: 設計(②③ レビュー中)
- 最終更新: 2026-11-13
- 次にやること: ②③ の結果を報告し、P0・P1 を設計に反映

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [ ] ② プランのレビュー(design-reviewer・security-reviewer)
- [ ] ③ テスト計画のレビュー(edge-case-reviewer)

### 実装(tdd スキル)
- [ ] T1: WeakWordScorer
- [ ] T2: ModeEligibility
- [ ] T3: QuestionSelector(候補・分類・枠・補い・新規の順・モード・混ぜる・0語の理由)
- [ ] T4: SessionManager.Start とのつながり、性能の目安

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [ ] decisions.md の決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、苦手度の式を採用・出題順は混ぜる・苦手のみは足りなければある分だけ と決まった
- 2026-11-13: feature/session-management(PR #6)の上に積んでいる(PlannedQuestion を使うため)
