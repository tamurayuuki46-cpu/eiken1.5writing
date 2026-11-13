# tasklist.md — セッション管理(途中終了・再開)

## 状態
- フェーズ: 検証完了(⑦ PR)
- 最終更新: 2026-11-13
- 次にやること: ⑦ PR を作って main へ

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [x] ② プランのレビュー(design-reviewer・security-reviewer)— P1 11 を反映、2点はユーザーが決定
- [x] ③ テスト計画のレビュー(edge-case-reviewer)— P0 1・P1 10 を反映

### 実装(tdd スキル)
- [x] T1: ProgressDocument を Application へ移し Session を追加、セッションの型(PlannedQuestion / SessionQuestion / SessionAnswer / PracticeSession)
- [x] T2: RetryModeRule
- [x] T3: SessionManager(Start / Current / Answer / Abandon / PruneAnswerIds / Replay)
- [x] T4: SessionValidator と ProgressJson の往復
- [x] T5: ProgressRepository(セッションの検査・捨てる・写しを残す・answerId の整理)

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(Core 358 / Persistence 72。Red 69件を確認してから実装)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [x] ⑥ 3体レビュー(1往復目: P1 5 → 修正。2往復目: 通過(P2 3件のうち、全状態の検査テスト・StateBefore のテスト・design の表を反映)。P0・P1 残りなし)

### 片付け
- [ ] P2(⑥): 完了・中止の整理を PruneAnswerIds(null) に揃えて RemoveSession を削除 / 型を1ファイル1つに分ける(design.md の表と合わせる)/ null 確認の重複(ReadSession と SessionValidator)/ CheckQuestion を SessionValidator へ / _discarded を FileRead の旗にする / Completed と SessionToSave is null の重複 / SessionSummary(結果画面用の集計)/ 穴あきの入力が単語全体か欠けた文字か(QuestionSelector・WPF で確認)/ 深い JSON・重複キーで全体が壊れている扱い(限界)/ 綴りの写しと単語データの照合(WPF・QuestionSelector)/ 完了直後に古いセッションで Answer を呼ぶと重複を検出できない(約束で防ぐ限界)/ 既定値に依存したテスト2件 / Save の結果を Assert していないテスト / 復旧時のセッション破棄・Load 2回のテスト / RetryCount 3・4 の Start テスト / InvalidDataException の内側の文言をログに出す扱い
- [ ] P2(②③): 単語データ更新後の再開時の照合(綴りの写し・WPF 側の課題)/ 穴あき位置を再開時に同じにするか / Current に進行表示用の件数 / 時刻の矛盾(StartedAt が未来・回答が開始前)の警告 / answeredAt が前の回答より前の扱い / Abandon を2回呼んだとき
- [x] decisions.md の決定を docs/DECISIONS.md へ
- [x] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、出題一覧を受け取る形・再練習のモードは固定の優先順・再練習は正解するまで最大 N 回 と決まった
- 2026-11-13: 実装中に、セッションが JSON として読めない場合もセッションだけ捨てる(ParseDetailed)と design.md に追記。既存テスト2件の期待値を仕様変更(session が正式な項目・読込時の answerId 整理)に合わせて変更
