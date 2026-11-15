# タスクリスト(依頼文で範囲確認済み。CLAUDE.md 6章の「着手前の確認」は依頼文の詳細指示で代えた)

- [x] Core: Revealed(AnswerEvent・ModeRecord.LastWrongInputAt・SessionAnswer・SessionManager.Reveal・Validator)単体テスト → 実装
- [x] Core: WeakWordRow(実誤入力・モード別)
- [x] Flow: Reveal・保存失敗/再起動・結果/ホームからの苦手一覧と戻り(統合テスト)
- [x] 受入シナリオ(A・B・C)を Flow の統合テストに(`tests/EikenSpelling.Flow.Tests/FlowRevealTests.cs` の最初のテスト)
- [x] Presentation: WeakListViewModel・結果/ホームの入口・答えを見る・判定画面
- [x] WPF: WeakListView・各画面の入口・答えを見る
- [x] dotnet build -warnaserror / dotnet test 全件(1,234 件)
- [x] WPF 実機の確認(UI Automation・テスト用データ `--data-dir`)
- [x] レビュー(設計・異常系・セキュリティ・UX の4体。2026-11-15 20:20 以降に再開して完了)

## レビュー(2026-11-15、4体)
- 設計: P0・P1 なし
- セキュリティ P1: 苦手一覧の「直近に入力した誤綴り」がスクリーンリーダーの名前に出る → 固定の名前に(実機の UI Automation で、入力が名前に出ないことを確認)。P2: AnswerEvent の ToString で入力を伏せた
- UX P1: ①0件でも一括復習を押せた → Flow の HasReviewTargets で押せなくし、理由を常に出す ②並べ替えでフォーカスが「まとめて復習」へ戻り、開いた行が閉じた → 押したボタンにフォーカスを戻し、開いた行を引き継ぐ。初期フォーカスは並べ替え
- 異常系 P1(テストの穴): フラッシュの後回しで見せる前・表示中・表示開始の保存待ちに答えを見られないこと/答えを見るの時刻が逆行したときの記録/未来の lastWrongInputAt の警告 → テストを追加(フラッシュの条件を外す変異で Red を確認)
- P2 で直したもの: 「詳細/閉じる」の切り替えと ItemStatus、並べ方の「(選択中)」、一覧向けの0件の文言、文言と日時の書式を ScreenText に一本化、答えを見るの文言を XAML から ScreenText へ、結果へ戻ったら「苦手一覧」にフォーカス、入力の部分だけ等幅の字体、WeakRowViewModel を別ファイルに
- P2 で残したもの: SessionManager.cs が 308 行 / lastWrongInputAt と LastWrongAt の食い違いの検査 / 答えを見るに確認ダイアログなし(ボタンの説明で「不正解として記録されます」)/ 答えを見た判定のナレーターでの読み上げ(実機未確認)/ FeedbackViewModel の「正解/不正解」も ScreenText 外(既存)

## 結果(2026-11-15)
- Red: 新しいテストは実装前に API が無くコンパイルで Red。後から通ったものは、AnswerRecorder で答えを見るを実入力扱いに戻す変異で受入テストを含む2件が Red になることを確かめた
- Windows 実機(UI Automation、保存失敗は progress.json を別プロセスで排他に開いて起こす): ①〜⑥すべて確認(27/28 PASS。残り1件はスクリプトの期待の誤りで、C の行を展開していなかった)
- 未確認: スクリーンショットでの見た目・ナレーター・200%・キーボードだけの操作
