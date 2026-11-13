# tasklist.md — セッション管理(途中終了・再開)

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
- [ ] T1: Models(PlannedQuestion / SessionQuestion / PracticeSession)と ProgressDocument.Session
- [ ] T2: RetryModeRule
- [ ] T3: SessionManager(Start / Current / Answer / Abandon / PruneAnswerIds / HasUnfinished)
- [ ] T4: ProgressValidator にセッションの検査、ProgressJson の往復

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [ ] decisions.md の決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、出題一覧を受け取る形・再練習のモードは固定の優先順・再練習は正解するまで最大 N 回 と決まった
