# tasklist.md — 学習記録・苦手/克服判定・再確認スケジュール

<!-- 置き場所: .steering/20261113-learning-record/tasklist.md
     /clear の後はこのファイルを読ませて再開する -->

## 状態
- フェーズ: 検証(⑥ 3体レビュー待ち)
- 最終更新: 2026-11-13
- 次にやること: ⑥ 実装の3体レビュー

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜6 は案どおり確定)
- [x] ② プランのレビュー(design-reviewer・security-reviewer)— P0 0 / P1 10(重複あり)を反映
- [x] ③ テスト計画のレビュー(edge-case-reviewer)— P0 2 / P1 11 を反映

### 実装(tdd スキル)
- [x] T1: Models(LearningState / AnswerEvent / ModeRecord / WordProgress / LearningProgress)
- [x] T2: ReviewScheduler(初回予定日・次の段階・期限一覧)
- [x] T3: MasteryEvaluator(連続正答・同じ日・克服対象モード)
- [x] T4: AnswerRecorder(重複 → モード別記録 → 再練習の打ち切り → 状態の更新)
- [x] T5: モード別の正答率(未学習は null)

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(220/220、Red 57件を確認してから実装)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [ ] ⑥ 3体レビュー(1往復目: P0 0 / P1 4 → 修正済み、232/232。2往復目の再レビュー中)

### 片付け
- [ ] P2(⑥): DueWordIds を ImmutableArray に / AnswerResult を別ファイルに / ReviewAdvanced を差分ではなく分岐で返す / 性能テストを AnswerRecorderTests へ / 間隔0件の分岐の扱い / 元の記録が変わらないテストを再練習・克服済みの経路にも
- [ ] 次のタスクへ(⑥): ProgressValidator に「辞書のキー == WordId」「回数・連続正答が 0 以上」「Modes に null なし」「列挙が定義内」を追加 / 復元時に StringComparer.Ordinal を付け直す / 未来の日時の扱い / 未完了のまま古いセッションの answerId を消す条件 / 回答の record をそのままログに出さない(logging.md)/ 古い回答が後から届いた場合の扱い / DateTimeOffset.MaxValue の回答日時
- [ ] P2(②③): 日時のオフセット統一 / 誤答入力の長さの上限 / 同じ answerId で中身が違う回答 / DateTimeOffset の範囲 / 部品名と v1.2 の対応 / 性能の計測方法
- [x] decisions.md の決定を docs/DECISIONS.md へ(苦手度の保留を含む)
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で4点を決めた(苦手度は保留 / 再練習は判定に影響しない / 再確認はモードを問わない / 範囲は Core だけ)
- 2026-11-13: 5,000語の記録への1回の反映は約10ms(初回の JIT 込み)。閾値は置かない
