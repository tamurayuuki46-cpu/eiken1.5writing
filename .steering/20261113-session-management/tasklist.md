# tasklist.md — セッション管理(途中終了・再開)

## 状態
- フェーズ: 実装(tdd)
- 最終更新: 2026-11-13
- 次にやること: T1〜T5 を tdd で実装

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [x] ② プランのレビュー(design-reviewer・security-reviewer)— P1 11 を反映、2点はユーザーが決定
- [x] ③ テスト計画のレビュー(edge-case-reviewer)— P0 1・P1 10 を反映

### 実装(tdd スキル)
- [ ] T1: ProgressDocument を Application へ移し Session を追加、セッションの型(PlannedQuestion / SessionQuestion / SessionAnswer / PracticeSession)
- [ ] T2: RetryModeRule
- [ ] T3: SessionManager(Start / Current / Answer / Abandon / PruneAnswerIds / Replay)
- [ ] T4: SessionValidator と ProgressJson の往復
- [ ] T5: ProgressRepository(セッションの検査・捨てる・写しを残す・answerId の整理)

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [ ] P2(②③): 単語データ更新後の再開時の照合(綴りの写し・WPF 側の課題)/ 穴あき位置を再開時に同じにするか / Current に進行表示用の件数 / 時刻の矛盾(StartedAt が未来・回答が開始前)の警告 / answeredAt が前の回答より前の扱い / Abandon を2回呼んだとき
- [ ] decisions.md の決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、出題一覧を受け取る形・再練習のモードは固定の優先順・再練習は正解するまで最大 N 回 と決まった
