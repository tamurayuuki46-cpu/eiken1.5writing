# タスクリスト(依頼文の詳細指示で範囲確認済み。CLAUDE.md 6章の「着手前の確認」は依頼文で代えた)

- [x] Core: VocabularySets(整列・分割・単位切替)・WordStudyState・VocabularySetScope・Selector 範囲・Start/Validator(単体テスト → 実装)
- [x] Flow: 閲覧・プレビュー・不足確認・同意開始・保存失敗の再保存・失効・再開(統合テスト → 実装)
- [x] 通しテスト(最優先の5段)と境界テスト(4,924語・0問・満数・最終ページ・50⇄100・語彙更新・旧データ)
- [x] Presentation: VocabularySetViewModel・SetStartPreviewViewModel・ホームの入口・再開の選択の文脈
- [x] WPF: VocabularySetView・SetStartPreviewView・MainWindow の DataTemplate
- [x] dotnet build -warnaserror / dotnet test 全件(1,357 件: Core 669 / Persistence 95 / Flow 466 / Presentation 103 / WordData 24)
- [x] Windows 実機(UI Automation・`--data-dir`・実データ 4,924 語): ホーム → セット閲覧 → 不足確認 → 出題 → 後回し・誤答 → 強制終了 → 再起動 → 再開(22/22)
- [x] レビュー(設計・異常系・セキュリティ・UX の4体)
- [x] ST・AC の照合表(下)

## 結果(2026-11-15)
- Red: 新しいテストは実装前に型が無くコンパイルで Red。通った後、変異で赤になることを確かめた(Selector の範囲の交差を外す → ScopeTests 5件以上 Red。Flow: 候補の再利用を外す・失効判定を外す・同意時に Selector を撃ち直す・範囲を Selector に渡さない → それぞれ該当テストが Red)
- 実機(UI Automation、画面の撮影も確認): 閲覧・単位切替・不足確認(希望60/可能50)・取消・同意・強制終了後の再開まで、書き込みの有無を含めて 22/22。保存失敗の実機再現は Flow の統合テスト(`SwitchableFiles`)で行い、実機では行っていない
- 未確認: ナレーターの読み上げ・200% 表示・キーボードだけの操作(PageUp/PageDown でのスクロールを含む)・最後のセットで「次のセット」を押し続けたときのフォーカス(コードは反対向きのボタンへ移す設計)

## レビュー(2026-11-15、4体)と対応
- 設計: P0・P1 なし(P1 の DECISIONS.md の追記は同時に済み)。P2 のうち StudyStatus と一覧の状態判定の重複は `VocabularySets.StateOf` に一本化。残した P2: 範囲内でも語彙リスト(EnabledSources)の絞り込みが新規の枠に効く(既存の規則のまま。DECISIONS に記載)/ SessionManager.cs が 317 行 / 画面の文言の置き場所(SetPlanChanged などが ScreenText)/ SetStartPreviewScreen.Plan が public(ToString では伏せ、ViewModel は使わない)/ 失効の判定の大半は本番の経路では起きない防御(モードの変化だけが実際に起きうる)
- セキュリティ: P0・P1 なし。P2: 選択の保持を計画を作った後に(直した)/ `"scope": null` の明示は範囲指定なしとして読む(flashShownStep と同じ扱い。テストと DECISIONS に記載)/ JSON 経由の不正値のテストと、改ざんした progress.json のファイル経由のテストを追加
- 異常系: P1 の1(範囲の検査を消しても通るテスト)→ 再練習まで正しいセッションの範囲だけを狭めて、範囲の検査だけが落とすテストを追加。P1 の2(DECISIONS)→ 追記済み。P2: 保存失敗 → 取消 → 再プレビュー、保存失敗 → 前提変更 → 確認、希望数の境目(9/8)、最後の端数ページからの単位切替、同じ単位の再選択、不正な条件で状態が変わらないこと、改ざん時に写しと通知が残ることを追加
- UX: P1 の1(末尾で「このセットで始める」へフォーカスが飛ぶ)→ 反対向きのボタン、無ければ表示単位へ。P1 の2(200% で一覧が潰れる)→ 開始条件を一覧のスクロールの中へ。P1 の3(キーボードで一覧をスクロールできない)→ PageUp/PageDown/Home/End を一覧のスクロールに割り当て。P2: 条件に戻る → 「このセットで始める」へ / 再開できないときは「続けられます」を出さない / 保存失敗の帯に LiveSetting / 条件は開くたびに保存済みの基本設定から。残した P2: 確認画面の初期フォーカスは「N問で開始」(承認者の判断が要る。0.3 秒のゲートで連打は防ぐ)/ 一覧の行の読み上げ順(状態が先)/ ホームへ戻ったときのフォーカスが「おすすめ学習」/ ページ送りボタンが一覧の上だけ

## ST・AC の照合(P0-ST の本文はリポジトリ外で参照できず、ST-T01〜22 の個別定義は未確認。P0-AC 4.2 の分類に対応するテスト名で照合)
| ID | テスト(クラス.名前) | 結果 |
|---|---|---|
| ST-01 4,924語で99/50セット | VocabularySetTests.ST01_ST03_full_data_has_99_fifty_sets_and_50_hundred_sets_with_24_in_the_last / ST01_every_word_id_appears_exactly_once_across_the_sets(50・100)/ BundledWordsTests.ST01_ST03_bundled_words_make_99_fifty_sets_and_50_hundred_sets_with_every_word_exactly_once(実データ) | 合格 |
| ST-02 固定順 | VocabularySetTests.ST02_order_is_tier_then_ranked_first_then_rank_then_ordinal_id / ST02_order_does_not_depend_on_input_order / FlowVocabularySetBrowseTests.ST02_pages_move_in_order_and_the_last_page_shows_the_remainder / ST02_previous_on_the_first_page_is_rejected_without_changing_the_screen | 合格 |
| ST-03 50⇄100・隣接ペア・共通成績 | VocabularySetTests.ST03_hundred_set_is_two_adjacent_fifty_sets_in_the_same_order / ST03_unit_switch_keeps_the_page_containing_the_original_first_word / FlowVocabularySetBrowseTests.ST03_hundred_view_joins_two_adjacent_fifty_sets_and_keeps_the_remainder / ST03_unit_switch_shows_the_page_containing_the_original_first_word / ST09_each_row_shows_the_learning_state_from_the_word_id_record(50・100で同じ状態) | 合格 |
| ST-04 全カテゴリを範囲内に | QuestionSelectorScopeTests.ST04_due_review_and_high_priority_weak_outside_the_scope_never_enter_the_plan / ST04_inside_review_weak_unseen_and_seen_are_all_selected_within_the_scope / ST04_weak_only_inside_the_scope_ignores_weak_words_outside | 合格 |
| ST-05 scope=null は回帰なし | QuestionSelectorScopeTests.ST05_no_scope_matches_the_unscoped_overload_for_the_same_clock_and_random / ST05_a_scope_holding_every_word_gives_the_same_plan_as_no_scope | 合格 |
| ST-06 不足は明示確認・0件は開始不可・再抽選禁止 | FlowVocabularySetStartTests.AC_X03_*(確認前は書かない・同意で同じPlan)/ AC_X04_zero_candidates_*/ ST10_weak_only_*/ QuestionSelectorScopeTests.ST06_fewer_eligible_words_than_requested_give_a_short_plan_without_error / FlowVocabularySetEdgeTests.Desired_one_above_available_*(9/8 と 8/8) | 合格 |
| ST-07/08 語彙更新・再開は元の計画 | FlowVocabularySetAcceptanceTests.AC_X12_resume_keeps_the_saved_plan_after_a_word_update_*/ ST08_a_word_update_notice_is_shown_once_*/ ST07_resume_is_refused_with_a_reason_when_a_saved_word_is_gone_*/ SessionScopeTests.Resume_does_not_reselect_a_changed_set_* / VocabularySetViewModelTests.Resume_prompt_*(2件) | 合格 |
| ST-09 閲覧は無書込・端数 | FlowVocabularySetBrowseTests.ST09_home_opens_the_first_fifty_set_and_writes_nothing / ST09_browsing_next_previous_unit_switch_and_back_never_write / ST02_pages_*(最後の20語)/ VocabularySetViewModelTests.Back_returns_to_home_and_nothing_is_written_by_browsing | 合格 |
| ST-10 音声不可・モード不適格・空Set | QuestionSelectorScopeTests.ST10_an_empty_scope_is_not_the_same_as_no_scope / ST10_mode_ineligible_* / ST10_audio_unavailable_* / VocabularySetViewModelTests.An_unusable_mode_cannot_be_started_from_a_set | 合格 |
| ST-T01〜22 | 個別定義が参照できず未照合(上の ST-01〜10 で同じ分類を網羅) | 未確認 |
| AC-X02 選択外に高優先語 | FlowVocabularySetStartTests.AC_X02_selection_never_includes_words_outside_the_set_* / 通しテスト Five_stage_* | 合格 |
| AC-X03 20→8・取消・同意・同じPlan・Selector再実行0 | FlowVocabularySetStartTests.AC_X03_short_candidates_*/ AC_X03_cancel_returns_*/ AC_X03_confirm_starts_the_same_plan_*(乱数の呼び出し数が不変)/ AC_X03_double_confirm_* | 合格 |
| AC-X04 開始保存失敗・0件・満数 | FlowVocabularySetStartTests.AC_X04_start_save_failure_*/ AC_X04_zero_candidates_*/ AC_X04_full_count_*(2件)/ FlowVocabularySetEdgeTests.Save_failure_then_*(2件) | 合格 |
| AC-X07 確認中の前提変更で失効 | FlowVocabularySetStartTests.AC_X07_changed_mode_availability_expires_the_old_confirmation_and_asks_again / FlowVocabularySetEdgeTests.Save_failure_then_a_changed_premise_* | 合格 |
| AC-X12 語彙更新後も再抽選なし | FlowVocabularySetAcceptanceTests.AC_X12_resume_keeps_the_saved_plan_after_a_word_update_* | 合格 |
| AC-X14 不正なScope外を拒否・無指定は既存と一致 | SessionScopeTests.AC_X14_validator_rejects_*/ AC_X14_a_legit_retry_session_is_rejected_by_the_scope_check_alone_*/ AC_X14_every_question_through_normal_deferred_and_retry_stays_in_the_scope / FlowVocabularySetAcceptanceTests.AC_X14_a_saved_session_with_a_question_outside_its_scope_is_rejected_on_load_*/ Five_stage_*(5段目: 固定乱数・固定時刻で従来の選出と一致) | 合格 |
