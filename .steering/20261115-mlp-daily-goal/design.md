# design.md — ロードマップ④ 継続学習を支える MLP

## 1. 日別実績をどこで一度だけ確定させるか
- **`AnswerRecorder.Apply`**(Core/Application)。通常枠(`AttemptType.Normal`)の回答を学習記録に反映するのと同じ新しい `LearningProgress` の中で、`Daily` に1を足す
  - 二重反映の防止(answerId)と同じ場所なので、同じ通常枠を2回数えることは構造上ない。再練習(`Retry`)は足さない。スキップは Apply を呼ばない
  - 後回しでの回答・答えを見るも answerId は元の通常枠なので1回だけ
- 日付は `AnswerEvent.AnsweredAt` の**オフセットの壁時計の日付**(`DailyActivity.DayOf`)。Flow は採点の時刻を `TimeProvider.GetLocalNow()`(端末のローカル時刻)で作る
  - 採点の時点で日付が文書に入るので、保存失敗の再保存(Flow は同じ候補の文書を保存し直す)で日付は変わらず、加算もされない
  - 再起動で保存されなかった回答は、もう一度答えた時刻で数える(保存されていない採点は無かったことになる。既存の規則どおり)

## 2. 保存形式(schemaVersion 2 のまま、任意項目の追加)
```json
"progress": {
  "words": { ... }, "appliedAnswerIds": [ ... ],
  "daily": { "recordingSince": "2026-11-15", "days": [ { "date": "2026-11-15", "graded": 3 } ] }
}
```
- `recordingSince` = この版で最初に数えた日(最初の加算と同じ更新で入る。null は書かない)。記録開始日より前の日の採点(時計を戻した)では、記録開始日をその日へ下げる(レビュー P0 で変更。開始日より前の日を残すと検査で重大になり保存できなくなる)
- 記録が空でも `"daily": { "days": [] }` を書く(読むときは「無い」と同じく記録なし。害はない)
- 1日の件数の上限 1,000,000(`DailyActivity.MaxGradedPerDay`)。加算は頭打ち、超える値は検査で重大
- 読込: `daily` が無い = 記録なし(旧データ)。`"daily": null`・`days` の null 要素は他の項目と同じく読めない(InvalidDataException)
- 検査(ProgressValidator・重大): 件数が負、同じ日付が2つ、記録開始日が無いのに日がある、記録開始日より前の日。手で直した場合だけ起きる
- 古いアプリは `daily` を知らない項目として無視する。古いアプリで保存し直すと消え、次に数えた日から記録をやり直す(二重加算はしない。過去の日は記録なしに戻るだけ)
- 起動・ホーム閲覧では書かない(既存どおり)。v1 の主ファイルは最初の保存の前に既存の原本保護が働く

## 3. 当日の表示(Core/Application `DailyProgress`)
- `DailyProgress.From(progress, today, goal)` → `Graded: int?`(null = 記録なし)、`Goal`、`Achieved`
  - recordingSince が無い: 学習記録が空なら 0(何も起きていないことが確か)、記録があれば null(旧データ。推測しない)
  - today < recordingSince(時計を戻した): null
  - それ以外: その日の件数(無ければ 0)
- today は Flow が `TimeProvider.GetLocalNow()` の日付で渡す。ホーム・再開の選択を作るたびに求めるので、日をまたいでもホームに戻れば当日の値
- 目標は `LearningSettings.Goal.DailyQuestionGoal`(初期値 10、選択肢 `[5, 10, 20, 30, 50]`、範囲 1〜500 `SettingsLimits.MaxDailyGoal`)。実績は学習記録だけから求めるので、目標を変えても実績は変わらない

## 4. 設定(S-07)
- Flow: `SettingsScreen` に `DailyGoal`・`DailyGoalChoices`。`SaveSettings(values, dailyGoal)`、`ResetSettings` は目標も初期値へ。保存は既存の `SettingsRepository`(基本設定+目標だけを今の設定に重ねる。詳細設定は残る)
- Presentation: `SettingsViewModel`(問題数・出題方法・苦手のみ・フラッシュ秒数・1日の目標。保存・初期値に戻す・閉じる。保存できない理由・検査の項目・失敗の文言)
- WPF: `SettingsView`(先頭の項目にフォーカス、Enter = 保存、Esc = 閉じる)。ホームに「設定」ボタン
- 練習の今回の条件・保存済み Plan(セッションの設定の写し)には目標は効かない

## 5. 再開の選択(S-08)
- `ResumeSummary` に `Remaining`(列の未採点の数)・`AutoMix`・`ManualMode`・`WeakOnly`・`NormalCount`・`MayAddRetries` を足す。文は Presentation の ScreenText
  - 例: 「自動ミックス・通常10問の練習」「苦手の復習」「語彙セット 3 / 99(50語ずつ)から始めた練習」/「次は後回しの問題です(後回し 2問)」/「残り 4問(誤答すると再練習が加わります)」
- `ResumePromptScreen.Today`(今日の実績)を出す。再開しただけでは増えないことが画面で分かる
- 「続きから」は既存の `Resume`(保存したセッションの列をそのまま使う。書き込みなし)

## 6. ホーム(S-02・S-10)
- `HomeScreen.Today` → HomeViewModel `TodayText`「今日 3 / 10問」・`TodayNote`(達成 / あと7問 / 記録なしの説明)。5カードの上に1行で置く
- 5カード・苦手一覧・語彙セット・おすすめ・自由選択は変えない

## 7. テスト方針
- Core: DailyActivity・DailyProgress・AnswerRecorder の加算(通常・後回し・答えを見る・再練習・重複)・日付の区切り(オフセットの壁時計)・JSON の往復と null・Validator
- Flow(本物のファイル・固定の時計 `FixedTimeProvider` にローカルのタイムゾーンを持たせる): 受入シナリオ①〜⑦をそのままテストにする。設定の目標の保存・初期値・取消
- Presentation: ホームの今日の文、再開の文、設定の ViewModel
- WPF: ビルドと実機(UI Automation、`--data-dir` の一時フォルダ。本人の実データは使わない)
