# design.md — 学習記録・苦手/克服判定・再確認スケジュール

<!-- 置き場所: .steering/20261113-learning-record/design.md
     役割: requirements.md を「どう作るか」。実装前にここを確認してもらう(技評 SDD) -->

## 1. 方針
- 学習記録は immutable な record(`System.Collections.Immutable` を使う。.NET 標準なので NuGet の追加は不要)。
  `AnswerRecorder.Apply(記録, 回答, 設定)` が新しい記録と結果を返し、元の記録は変えない
- 判定は小さな Domain 部品に分ける。克服の連続正答は `MasteryEvaluator`、再確認の段階と日付は `ReviewScheduler`。
  回答の種類で振り分けて組み合わせるのは Application の `AnswerRecorder`
- 回答日時は `DateTimeOffset` で受け取り、`JstClock.ToJstDate` で日本時間の日付にする。Core は現在時刻を読まない
- 検討して見送った案:
  - 可変クラスで記録を直接書き換える案 → 保存に失敗したとき元に戻せない(v1.0 13.3「現在の回答を保持」)
  - 「再確認待ち」を状態として保存する案 → 日付が変わるだけで状態が変わり、保存し直しが必要になる。期限から毎回求める(6章 1)

## 2. 変更する場所
| ファイル | 内容 | 新規 / 変更 |
|---|---|---|
| Models/LearningState.cs | 状態: `Normal`(通常)/ `Weak`(苦手)/ `Mastered`(克服済み)。未出題は「記録が無い」で表す | 新規 |
| Models/AnswerEvent.cs | `AnswerId`(sessionId・questionIndex・attemptType)、`AttemptType`(Normal / Retry)、`AnswerEvent`(answerId・wordId・mode・isCorrect・input・answeredAt) | 新規 |
| Models/ModeRecord.cs | 単語×モードの記録。通常問題の正答数・誤答数と、追加再練習の正答数・誤答数を分けて持つ。最後の誤答入力・最終誤答日時・最終出題日時。正答率(通常問題のみ、未出題は null) | 新規 |
| Models/WordProgress.cs | 単語の記録: 状態・モード別記録・最終通常出題日時・`MasteryStreak`・`LastEligibleCorrectDate`・`MasteredAt`・`NextReviewDate`・`ReviewStage`(0=未克服、1〜) | 新規 |
| Models/LearningProgress.cs | 全体: 単語ID → WordProgress、反映済み answerId の集合、`schemaVersion` | 新規 |
| Domain/MasteryEvaluator.cs | 苦手の単語への通常問題の正誤から、連続正答と克服を判定する | 新規 |
| Domain/ReviewScheduler.cs | 克服時の初回予定日、再確認に正答したときの次の段階と日付、期限が来た単語の一覧 | 新規 |
| Application/AnswerRecorder.cs | 回答を記録に反映する入口(重複の確認 → モード別記録 → 状態の更新) | 新規 |
| docs/DECISIONS.md | 今回の決定(苦手度は保留、再練習は判定に影響しない、再確認はモードを問わない など)を追記 | 変更 |

## 3. データ
- 扱うデータ(Entity): LearningProgress / WordProgress / ModeRecord / AnswerEvent
- データの持ち主(Data Owner): ファイルの持ち主は WPF 側の Repository(次のタスク)。Core は値を受け取って新しい値を返すだけ
- Schema 変更: 新規(学習記録の初版)。`LearningProgress.CurrentSchemaVersion = 1` を持たせる。JSON の形は次のタスクで決める

## 4. 処理の流れ(AnswerRecorder.Apply)
1. answerId が反映済みなら、何も変えずに「重複」を返す(R3)
2. 単語の記録が無ければ新しく作る(未出題 → 初回)
3. モード別記録を更新する。通常問題と追加再練習で別の数を増やす。誤答なら最後の誤答入力と日時を残す
4. **追加再練習なら、ここで終わる**(R6)
5. 通常問題の場合、最終出題日時を更新し、状態ごとに次のように処理する
   - 誤答: 苦手にする(通常問題の誤答数の合計が `MarkWeakAfterMistakes` 以上)。連続正答を 0、最後の対象正答日を null にする。克服済みだった場合は段階を 0、予定日を null に戻す(R4・R7)
   - 正答で、記録が初回: 通常にする
   - 正答で、苦手のとき: `MasteryEvaluator` を使う。日本語・音声モードで、最後の対象正答日と違う日なら連続正答 +1。必要回数に届いたら克服し、`ReviewScheduler` で段階 1・予定日 = 回答日 + 間隔[0](R5)
   - 正答で、克服済みのとき: 予定日以前なら記録だけ残す。予定日以降ならモードを問わず `ReviewScheduler` で次の段階へ進める。次回日は回答日 + 間隔[次の段階 − 1]。最後の段階は `RepeatLastInterval` なら同じ間隔を繰り返す(R7)
6. answerId を反映済みに加え、新しい記録と結果(重複 / 状態の変化 / 克服した / 苦手に戻った)を返す

## 5. テスト方針
- 単体(xUnit。時刻は `DateTimeOffset` の固定値を渡す):
  - 状態の変化: 初回の正答・誤答 / 苦手 → 克服 / 克服 → 苦手
  - 克服: 同じ日の2回目の正答 / 穴あき・フラッシュの正答 / 途中の誤答でのリセット / 必要回数 n-1・n・n+1(設定を 1・2・3 に変える)
  - 日付の境目: 日本時間 23:59:59 と 0:00:00(= UTC 14:59:59 と 15:00:00)
  - 追加再練習: 正答も誤答も状態・連続正答・予定日を変えない。記録の数は増える
  - 再確認: 期限前・当日・期限後の正答 / 7→14→30→30 / RepeatLastInterval=false のとき最後で予定日が無くなる / 期限後の誤答で苦手へ / 間隔を変えた設定
  - 重複: 同じ answerId を2回 / sessionId または attemptType だけが違う ID は別の回答として扱う
  - 期限一覧: 予定日の古い順、同じ日は単語ID順、期限前は含めない
  - 正答率: 未出題は null、追加再練習は含めない
  - 元の記録が変わっていない(immutable)
- 結合・手動確認: なし(保存と画面は次のタスク以降)

## 6. リスクと未決事項
- 【確定 2026-11-13】1 「再確認待ち」は保存せず、`状態=克服済み かつ 予定日 ≦ 今日` から求める(v1.0 7.2 の5状態を、保存は3状態+未出題で表す)
- 【確定 2026-11-13】2 再確認の次回日の起点は**実際に回答した日**にする(期限日ではない)。例: 期限の3日後に正答したら、そこから14日後
- 【確定 2026-11-13】3 反映済み answerId は増え続ける。完了したセッションの ID を消す処理は、セッション管理のタスクで作る(今回は集合を持つだけ)
- 【確定 2026-11-13】4 `MarkWeakAfterMistakes` を 2 以上にした場合は、通常問題の誤答数の**累計**で数える(連続ではない)
- 【確定 2026-11-13】5 苦手度は保留にしたが、DECISIONS.md の「苦手度」の節と `WeakWordScoreSettings` が既にある。DECISIONS.md に「保留」と書き足し、設定はそのまま残す(案)
- 【確定 2026-11-13】6 克服済みの単語が期限前に通常問題で出た場合(本来は Q21 で補充に使わない)、正答は記録だけ、誤答は苦手に戻す
- リスク: 正答率・最終出題日時の定義を「通常問題だけ」にしたので、苦手一覧の画面で追加再練習の成績を見せたいときは別の値を使う必要がある
