# タスクリスト(依頼文の詳細指示で範囲確認済み。CLAUDE.md 6章の「着手前の確認」は依頼文と日別計上ルールの本人確定で代えた)

- [x] 日別計上ルールを本人と確定(2026-11-15: 推奨案どおり、目標の初期値 10問)
- [x] Core: DailyActivity・DailyProgress・AnswerRecorder の加算・GoalSettings・検査・JSON(テスト先行)
- [x] Flow: 採点時刻をローカルに・ホーム/再開に Today・再開の要約・設定の目標(受入①〜⑦のテスト先行)
- [x] Presentation: ホームの今日の文・再開の文・SettingsViewModel
- [x] WPF: SettingsView・ホームの今日の行と設定ボタン・再開画面の行
- [x] dotnet build -warnaserror / dotnet test 全件(1,427件: Core 709 / Persistence 95 / Flow 488 / Presentation 111 / WordData 24)
- [x] Windows 実機(UI Automation・`--data-dir` の一時フォルダ、30/30)。レビュー修正の後の再実行は未(下記)
- [x] レビュー(設計・異常系・セキュリティ・UX の4体)と対応
- [x] DECISIONS.md・CLAUDE.md の更新、本人3日確認表(`mlp-3day-check.md`)

## 結果(2026-11-15)
- Red: 新しいテストは実装前に型が無くコンパイルで Red。通った後の変異: 再練習も数える → Core 3件 Red / 採点時刻を UTC に戻す → Flow 3件 Red / 記録開始日の修正を戻す → 新しいテストが Red
- 実機(UI Automation、撮影でも確認): 今日 0/10 → 設定で目標5 → 自由選択で誤答・後回し・誤答(2問)→ やめる → 再開の文脈(手動(日本語)・通常5問、残り3問、後回し1問、今日 2/5)→ 再起動 → 続きから(書き込みなし)→ 答えを見る(5問)→ 再練習5問(5問のまま)→ ホーム 5/5・目標達成 → 再起動 5/5 → 旧記録(daily なし)は「記録なし」で書き込みなし。30/30
- 実機の再実行が未のもの: レビュー後の修正(記録開始日の下げ・件数の上限・再練習段階の注記・読み上げの通知・文言)。本人のアプリが起動中で二重起動防止に当たるため。テストと WPF のビルド(別フォルダ)で確認済み
- 未確認: ナレーターの実際の読み上げ(LiveRegionChanged を足した)・200% 表示・日付の境目の実機(テストで確認)

## レビュー(2026-11-15、4体)と対応
- P0(設計・異常系・セキュリティの3体が同じ指摘): 時計を戻して記録開始日より前の日に採点すると、検査で重大になり保存できなくなる → 記録開始日をその日へ下げる。Core・Flow の通しテストを追加
- P1 セキュリティ: graded が int の上限付近で加算すると負に回る → 上限 1,000,000 で頭打ち・超える値は重大。境界のテストを追加
- P1 異常系: 再練習の段階で RetryCount 2 以上なら「再練習が加わる」の注記が要る/注記が偽になるテストが無い → 直してテストを追加(RetryCount 0・1・2)
- P1 設計: DECISIONS の日付の固定ルール・設定の保存・goal の範囲・daily の形 → 追記
- P1 UX: 保存の成功・失敗がナレーターに伝わらない → 設定・ホームの新しい帯で LiveRegionChanged を発火(実機の読み上げは未確認)
- 本人判断待ち(P1 異常系・P2 セキュリティ): daily の不備(手で壊した場合だけ)で学習記録全体が重大になる。今は既存の方針(重大は読込失敗と同じ)に合わせた。daily だけ捨てて警告にする案もある
- 直した P2: 記録なしの文言(「この版」をやめた)、今日の行の折り返し、Border の効かない名前、再開画面の帯の折り返し
- 残した P2: 設定・苦手一覧から戻ったときのフォーカスが「おすすめ学習」/「初期値に戻す」の後のフォーカスが先頭へ/保存できないときも「Enter で保存」の案内/保存成功の帯に閉じるボタンなし/セット学習で KindText と SetText が両方「…の練習」/スキップの時刻は UTC のまま(計上には使わない)/ホームを開いたまま日をまたぐと開き直すまで前日の値/目標が選択肢に無い値の ViewModel テスト/200% の高さ/既存画面の LiveSetting も同じ問題(FeedbackView など)

## 受入シナリオの照合
| シナリオ | テスト | 結果 |
|---|---|---|
| ① A正解・Bスキップ・C誤答 → 2問 | FlowDailyGoalTests.Scenario1_* / DailyActivityTests.Session_answer_skip_reveal_and_retry_* | 合格 |
| ② 答えを見る → 3問、再練習でも3問 | Scenario2_* / 実機 | 合格 |
| ③ 保存失敗 → 再保存 → 再起動で欠落・二重なし | Scenario3_save_failure_* / Scenario3_restart_after_an_unsaved_answer_* | 合格 |
| ④ 23:59 Skip → 0:01 採点は翌日 / 23:59 採点 → 翌日再保存は前日 | Scenario4_skip_at_2359_* / Scenario4_answer_at_2359_* / DailyActivityTests.Skip_before_midnight_* | 合格 |
| ⑤ 前日のセット学習を後回しで中断 → 翌日に段階・残り → 同じ問題、今日0問 | Scenario5_* / 実機(セットなしの同じ流れ) | 合格 |
| ⑥ 目標10→5 で 3/5、出題数・成績・Plan 不変 | Scenario6_* (2件) / MlpViewModelTests.Settings_* / 実機 | 合格 |
| ⑦ 旧記録: 状態保持・日別は出さない | Scenario7_* / MlpViewModelTests.Legacy_* / 実機 | 合格 |
