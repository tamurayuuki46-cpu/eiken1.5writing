# tasklist.md — 学習記録・苦手/克服判定・再確認スケジュール

<!-- 置き場所: .steering/20261113-learning-record/tasklist.md
     /clear の後はこのファイルを読ませて再開する -->

## 状態
- フェーズ: 設計(② プランのレビュー・③ テスト計画のレビュー中)
- 最終更新: 2026-11-13
- 次にやること: ②③ のレビュー結果を報告し、P0 を反映してから実装へ

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜6 は案どおり確定)
- [ ] ② プランのレビュー(design-reviewer・security-reviewer)
- [ ] ③ テスト計画のレビュー(edge-case-reviewer)

### 実装(tdd スキル)
- [ ] T1: Models(LearningState / AnswerEvent / ModeRecord / WordProgress / LearningProgress)
- [ ] T2: ReviewScheduler(初回予定日・次の段階・期限一覧)
- [ ] T3: MasteryEvaluator(連続正答・同じ日・克服対象モード)
- [ ] T4: AnswerRecorder(重複 → モード別記録 → 再練習の打ち切り → 状態の更新)
- [ ] T5: モード別の正答率(未学習は null)

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [ ] decisions.md の決定を docs/DECISIONS.md へ(苦手度の保留を含む)
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で4点を決めた(苦手度は保留 / 再練習は判定に影響しない / 再確認はモードを問わない / 範囲は Core だけ)
