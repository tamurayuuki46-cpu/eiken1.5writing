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
| Models/AnswerEvent.cs | `AnswerId`(sessionId: Guid・questionIndex・attemptType)、`AttemptType`(Normal / Retry)、`AnswerEvent`(answerId・wordId・mode・isCorrect・input・answeredAt)。questionIndex は**再練習を含むセッション内の通し番号**(同じ単語の2回目の再練習も別の番号) | 新規 |
| Models/ModeRecord.cs | 単語×モードの記録(下の表)。正答率は通常問題だけ、未出題は null | 新規 |
| Models/WordProgress.cs | 単語の記録(下の表) | 新規 |
| Models/LearningProgress.cs | 全体: 単語ID → WordProgress(`ImmutableDictionary`)、反映済み answerId(`ImmutableHashSet`)、`SchemaVersion` | 新規 |
| Domain/MasteryEvaluator.cs | 苦手の単語への通常問題の正答から、連続正答と克服を判定する(v1.2 の MasteryService に当たる) | 新規 |
| Domain/ReviewScheduler.cs | 克服時の初回予定日、再確認に正答したときの次の段階と日付、期限が来た単語の一覧 | 新規 |
| Application/AnswerRecorder.cs | 回答を記録に反映する入口 | 新規 |
| docs/DECISIONS.md | 今回の決定を追記(苦手度は保留 など) | 変更 |

### 記録の項目(コレクションはすべて Immutable 型。日付は日本時間の `DateOnly`、日時は `DateTimeOffset`)
| 型 | 項目 | 再練習で更新するか |
|---|---|---|
| ModeRecord | 通常の正答数・誤答数 | しない |
| ModeRecord | 再練習の正答数・誤答数 | する(再練習のみ) |
| ModeRecord | 最後の誤答入力・最終誤答日時・最終出題日時 | する(通常・再練習の両方) |
| WordProgress | 状態・`MasteryStreak`・`LastEligibleCorrectDate`・`MasteredAt`・`NextReviewDate`・`ReviewStage` | しない |
| WordProgress | 最終通常出題日時(v1.0 9.2 の復習順に使う) | しない |
- モード別の連続正答数(v1.0 11.1「必要な場合」)は持たない(不要)
- 日時は古い値で上書きしない(新しいほうを残す)

### 状態ごとの正しい組み合わせ(不変条件)
| 状態 | MasteryStreak | LastEligibleCorrectDate | ReviewStage | NextReviewDate | MasteredAt |
|---|---|---|---|---|---|
| Normal | 0 | null | 0 | null | 任意(過去の克服) |
| Weak | 0〜必要回数−1(設定で必要回数を下げた直後はそれ以上もありうる。次の対象正答で克服) | streak>0 なら値あり | 0 | null | 任意(過去の克服) |
| Mastered | 克服時点の必要回数(克服時にそろえる。v1.1 10.3。その後に設定を変えると今の必要回数とは一致しないので、検査では比べない) | 値あり | 1〜間隔の数 | 値あり、または null(RepeatLastInterval=false で最後まで終えた) | 値あり |
- この表を確かめる処理(読み込み時の検査)は、progress.json を作る次のタスクで作る。今回は Apply が段階・添字を範囲内に収めて例外を防ぐ

## 3. データ
- 扱うデータ(Entity): LearningProgress / WordProgress / ModeRecord / AnswerEvent
- データの持ち主(Data Owner): ファイルの持ち主は WPF 側の Repository(次のタスク)。Core は値を受け取って新しい値を返すだけ。Core は単語データとの照合をしない(単語データに無い ID の記録も残す)
- Schema 変更: 新規(学習記録の初版)。`LearningProgress.CurrentSchemaVersion = 1`。JSON の形、`wordSetVersion` / `sourceVersion` の保存は次のタスクで決める

## 4. 処理の流れ(AnswerRecorder.Apply)
1. 入力を確かめる。wordId が空、mode・attemptType が定義外、sessionId が Guid.Empty、questionIndex が負なら ArgumentException
2. answerId が反映済みなら、何も変えずに「重複」を返す(R3)
3. 単語の記録が無いとき: 通常問題なら新しく作る。**再練習なら InvalidOperationException**(本来は起きない入力)
4. モード別記録を更新する(2章の表)
5. answerId を反映済みに加える(**再練習もここで登録する**)
6. **追加再練習なら、ここで終わる**(R6)
7. 通常問題: 最終通常出題日時を更新し、次のとおり状態を決める(日付 = `JstClock.ToJstDate(回答日時)`)
   - 誤答
     - 克服済み → 無条件に苦手へ戻す。段階 0・予定日 null。MasteredAt は残す(v1.0 8.1)
     - 苦手 → 苦手のまま(要件 R4。閾値を後から上げても誤答で苦手から外さない。⑥ レビューで修正)
     - 初回・通常 → 通常問題の誤答数の**累計**が `MarkWeakAfterMistakes` 以上なら苦手、未満なら通常
     - どちらも連続正答 0、最後の対象正答日 null
   - 正答で初回 → 通常
   - 正答で通常 → 変化なし(連続正答は苦手の単語だけで数える)
   - 正答で苦手 → `MasteryEvaluator`: 日本語・音声モードで、**日付が最後の対象正答日より後**(または null)なら連続正答 +1・最後の対象正答日を更新。それ以外は変化なし。連続正答が必要回数以上で克服(連続正答を必要回数にそろえる・段階 1・予定日 = 日付 + 間隔[0]・MasteredAt = 回答日時)
     - 同じ日の誤答のあとの正答は、リセット後の1回目として数える(2026-11-13 決定)
   - 正答で克服済み
     - 予定日が null、または日付が予定日**より前** → 記録だけ
     - 日付が予定日**以降**(当日を含む)→ モードを問わず `ReviewScheduler.Advance`: 次の段階 = min(段階 + 1, 間隔の数)。次回日 = 日付 + 間隔[次の段階 − 1]。すでに最後の段階で `RepeatLastInterval=false` なら予定日 null(R7)
   - 添字は 0〜間隔の数−1 に収める(設定で間隔の数を減らしても例外にしない)。予定日の計算が DateOnly の上限を超える場合は上限で止める
8. 新しい記録と結果(重複 / 前後の状態 / 克服した / 苦手に戻った / 段階が進んだ)を返す。結果に回答の入力文字列は含めない

## 5. テスト方針
- 単体(xUnit。時刻は `DateTimeOffset` の固定値を渡す。期待値の数値は設定から取る):
  - 入力: 空・null の wordId・定義外の mode / attemptType・Guid.Empty の sessionId・負の questionIndex → ArgumentException。記録の無い単語への再練習 → InvalidOperationException
  - 状態の変化: 初回の正答・誤答 / 通常 → 苦手 / 苦手 → 克服 / 克服(期限前・期限後)→ 苦手 / 苦手 → 克服 → 苦手 → 再克服(段階1・回答日 + 間隔[0] からやり直す)
  - 苦手登録: `MarkWeakAfterMistakes=2` で 誤答(→通常)→ 正答 → 誤答(→苦手)/ 再練習の誤答は累計に入らない / 苦手の単語は閾値を上げても誤答で苦手のまま / 克服済みの単語は閾値を上げても誤答で苦手へ(期限前・期限後)
  - 克服: 日本語2日 / 音声2日 / 日本語と音声の混在 / 同じ日の2回目 / 同じ瞬間に別 answerId で2回 / 穴あき・フラッシュの正答は増えも減りもしない / 途中の誤答でリセット / 同じ日の誤答のあとの正答は1回目 / 日付の逆行では増えない / 必要回数 1・2・3 / 必要回数を現在の連続正答以下に下げたら次の正答で克服 / 通常の単語は日本語で正答しても連続正答が増えない / 克服時の連続正答は必要回数のまま
  - 日付の境目: 日本時間 23:59:59 と 0:00:00(= UTC 14:59:59 と 15:00:00)を克服と再確認の両方で / +09:00 以外のオフセットの回答日時
  - 追加再練習: 正答も誤答も状態・連続正答・予定日・最終通常出題日時を変えない / 再練習の数・最後の誤答入力は更新される / 同じ answerId の再練習を2回 → 1回分 / 同じ単語の2回目の再練習(別の questionIndex)は両方記録
  - 再確認: 期限前・当日・期限後の正答 / 期限の3日後の正答は回答日 + 間隔 / 7→14→30→30 / 間隔が1つ([7])で繰り返す・繰り返さない / RepeatLastInterval=false で最後の後は予定日 null、その後の正答は記録だけ / 段階が間隔の数より大きい記録でも例外にならない / 間隔を変えても既存の予定日は変わらず、次に進むときから新しい値
  - 重複: 同じ answerId を2回(通常・再練習)/ sessionId・questionIndex・attemptType のどれか1つだけが違う ID は別の回答
  - 期限一覧: 今日−1日と今日は含む、今日+1日は含まない / 予定日の古い順、同じ日は単語ID順 / 苦手・通常・予定日 null は含まない / 記録が空なら空
  - 正答率: 未出題は null / 再練習だけ答えたモードは null / 再練習は含めない / モードごとに別(フラッシュを合算しない)
  - 元の記録が変わっていない(Apply の前後で元の値を比べる)
- 結合・手動確認: なし(保存と画面は次のタスク以降)
- 性能(約5,000語×4モード)は、5,000語の記録に1回反映して時間を見るだけにする(閾値は置かない)

## 6. リスクと未決事項
- 【確定 2026-11-13】1 「再確認待ち」は保存せず、`状態=克服済み かつ 予定日 ≦ 今日` から求める(v1.0 7.2 の5状態を、保存は3状態+未出題で表す)
- 【確定 2026-11-13】2 再確認の次回日の起点は**実際に回答した日**にする(期限日ではない)
- 【確定 2026-11-13】3 反映済み answerId は増え続ける。消す処理はセッション管理のタスクで作る。条件は「保存が成功して完了したセッションの ID だけ消す」
- 【確定 2026-11-13】4 `MarkWeakAfterMistakes` を 2 以上にした場合は、通常問題の誤答数の**累計**で数える
- 【確定 2026-11-13】5 苦手度は保留。DECISIONS.md に「保留」と書き足し、`WeakWordScoreSettings` は残す
- 【確定 2026-11-13】6 克服済みの単語が期限前に通常問題で出た場合、正答は記録だけ、誤答は苦手に戻す
- 【確定 2026-11-13】7 段階の上限は間隔の数(既定 3 = v1.1 10.2 の「30日ごと」)。克服時の連続正答は必要回数のまま残す(v1.1 10.3)
- 【確定 2026-11-13】8 同じ日の誤答のあとの正答は1回目として数える / 記録の無い単語への再練習は例外 / 苦手に戻っても MasteredAt は残す
- 次のタスクへ: 2章の不変条件の検査(ProgressValidator)、誤答入力の長さの上限、日時のオフセットの統一、同じ answerId で中身が違う回答の扱い
