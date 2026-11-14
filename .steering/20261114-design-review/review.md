# 第4工程: 設計レビュー

- 実施: 2026-11-14
- 対象: 承認済みの UX 設計(`.steering/20261114-ux-design/ux-design.md`)と UI 設計(`.steering/20261114-ui-design/ui-design.md`)、AI 暫定案の実装プラン(`.steering/20261114-wpf-shell/`)
- レビュー役: design-reviewer / edge-case-reviewer / security-reviewer(読み取り専用、並列)。主な指摘は根拠のコードで確認した

## 最終判定(2026-11-14、再レビュー後): **GO**(ゲートの範囲の拡大もユーザーが承認。ux-design.md Q-UX3 に反映済み)
- H1〜H5: ユーザーが推奨どおり承認。ui-design.md 5〜7章・ux-design.md・DECISIONS「UI 技術」に反映。UX 専用のレビュー役 `.claude/agents/ux-reviewer.md` を追加
- C1・C2: wpf-shell の requirements・design・decisions を書き直し、P0 2件・P1 12件を反映
- C4: 再レビューを3往復(構成・異常系・安全・UX)。2往復目の P1 計7件(受け付け開始のゲート、直前の回答の値、同じ型の画面の初期フォーカス、ボタンが受ける Enter、ゲートの数え始め、テストの空振り)も反映し、**最終で4体とも P0・P1 0件**
- 残る承認(C3): 受け付け開始のゲートの範囲を、承認済みの Q-UX3(判定画面の Enter を約0.3秒無視)から「どの画面も、今の画面になってから約0.3秒は Enter・クリックを受け付けない」に広げた。承認済みの UX 設計の変更なので、ユーザーの承認を得てから ux-design.md に反映し、実装を始める
- 残りの P2 は wpf-shell design.md 6章の積み残しと、実装中の手動確認表で扱う

---

## 1回目の判定: **GO WITH CONDITIONS**

承認済みの UX・UI 設計は Flow と大筋で整合している(画面の範囲・遷移・保存失敗の約束)。ただし P0 が2件あり、実装プラン(wpf-shell)は承認済みの設計と多くの点で食い違う。**下の条件 C1〜C4 を満たすまで本実装は始めない。**

| 役 | P0 | P1 | P2 |
|---|---|---|---|
| design-reviewer | 1 | 11 | 5 |
| edge-case-reviewer | 2 | 9 | 7 |
| security-reviewer | 0 | 1 | 6 |
(重複を除くと P0 2件・P1 約12件)

### 訂正
第3工程の報告で「差分の文章は Core が作っていない」と説明したのは誤り。Core に `SpellingDiffService.Describe`(src/EikenSpelling.Core/Domain/SpellingDiffService.cs:95-102)があり、文章を作っている。

---

## P0(直すまで実装しない)

| # | 指摘 | 確認した根拠 | 対応案 |
|---|---|---|---|
| P0-1 | 空欄・空白だけの Enter: 承認済み Q-UX4 は「何も起きない」、wpf-shell decisions 決定3 は「誤答として記録」 | Flow は `"   "` も誤答にする(tests/EikenSpelling.Flow.Tests/FlowPracticeTests.cs:66、SpellingJudge.Normalize は Trim)| 承認済みの Q-UX4 を正とし、wpf-shell 決定3を取り消す。「空か」の判定は Core に1つだけ置く(例: `SpellingJudge.IsBlank` = Normalize 後が空)。Presentation はそれで回答ボタンを無効にし、Answer を呼ばない |
| P0-2 | 二重クリック・連打・Enter の押しっぱなしで、古い画面の操作が Flow に届くと例外 → アプリ終了 | `PracticeFlow.Require` は受け付けない操作で例外(PracticeFlow.cs:285-294)。保存は同期で UI が止まり、その間の入力が後から届く | 画面の ViewModel は今の画面でなくなったら全コマンドを無効にし CanExecuteChanged を出す。キーリピート(IsRepeat)は無視。「同じ操作を2回 → 2回目は何もしない」テストを Home・Question・Feedback・Result で |

## P1(このタスク中に直す)— 多くは wpf-shell を承認済みの設計に合わせれば解消

| # | 指摘 | 正とするもの |
|---|---|---|
| P1-1 | 差分の文章が3通り(Core の Describe / ui-design 5章 / wpf-shell の DiffText)| **Core の Describe を正本にする**(H2)。wpf-shell の DiffText は作らない |
| P1-2 | Extra(余分)の位置は Trim・小文字化の後の位置。入力をそのまま表示して取り消し線を引くと、先頭に空白があるとずれる | 入力側の表示は Normalize 後の文字列で行う。テスト `" Recieve"`・`"ACCOMODATE"` |
| P1-3 | 想定外の例外の後: ui-design は「安全な画面へ」、wpf-shell は「終了」| **終了する**(Flow の状態が壊れている可能性があるため。H1)。終了時の文言は「保存できていない回答がある」場合も正しい文に |
| P1-4 | 保存失敗中の「次へ」: wpf-shell は隠す、承認済みは無効にして理由 | 承認済み(無効にして理由)|
| P1-5 | 保存失敗中の Enter: wpf-shell は何もしない、承認済みは再保存 | 承認済み(再保存。初期フォーカスも「もう一度保存」)|
| P1-6 | 判定画面の Enter を約0.3秒無視(Q-UX3)が wpf-shell に無い | 追加。時間は TimeProvider で測りテスト(0.29 / 0.30 / 0.31 秒)|
| P1-7 | 「やめる」の前の確認・Esc が wpf-shell に無い(Q-UX2)| 追加。確認で「いいえ」なら出題のまま |
| P1-8 | 起動時の通知: wpf-shell は画面が変わると消す、承認済みは閉じるまで | 承認済み。StartFresh 後のホームの Notices も拾う |
| P1-9 | 窓の大きさ・最大幅・縦スクロールが wpf-shell に無い。また 1920×1080・200% では最小の高さ 560 が作業領域に入らない | 追加。最小サイズは作業領域で頭打ち、足りなければ縦スクロール(H3)|
| P1-10 | 判定画面の「n / 合計」「あなたの入力」が FeedbackScreen に無い | Presentation が直前の出題の値と入力を覚える。入力はログ・ToString に出さない(H4)|
| P1-11 | 再練習で合計が増える説明(UX-8 Must)、キー操作・IME(UX-2・UX-9 Must)が wpf-shell では Should / 未記載。初期フォーカス・フォーカス移動・スクリーンリーダー用の名前も無い | wpf-shell の要件を Must にそろえ、画面ごとの初期フォーカスを書く。スクリーンリーダー用の名前は固定の文言(入力値を結び付けない)|
| P1-12 | 画面の文言の持ち主(Flow / Presentation)の線引きが architecture.md に無い | 「判断を伴う文言は Flow・Core、値の言い換えだけ Presentation」を1行追加 |

## P2(任意。tasklist に残す)
- 貼り付け(Ctrl+V)で日本語・改行が入る → MaxLength と改行の除去
- 誤りが多いときの「3か所まで」の数え方(入れ替わりは2件)、1文字だけの入力のテスト
- 単語に空白・ハイフン・大文字がある場合の差分の読みやすさ(同梱データにあるかは不明)
- `--data-dir` に UNC パスを渡せる → 拒否するか前提を DECISIONS に書く
- 名前付き Mutex の範囲(ユーザーの切り替え)を限界として記録
- logging.md の対象に Presentation を追加
- AppDomain.UnhandledException はログを先に同期で書き、ダイアログは出せるときだけ
- 手動モードの選択肢を HomeScreen に持たせる(SSoT)
- 最後の通常問題を誤答したときのボタン名(再練習が足されて Completed は偽)のテスト

## まとめ

| 区分 | 内容 |
|---|---|
| Ready | 画面の範囲と遷移は Flow と一致 / Presentation は net10.0 で Windows 専用アセンブリを参照しない / 結果の3種類・問題数の選択肢は Flow から / 両方破損の「新しい記録」はデータを消さない(壊れたファイルは退避)/ 外部通信なし |
| Unresolved | H1〜H5(下)|
| Risk | 連打での終了(P0-2)/ 保存が同期で UI が止まる / 200% 表示で窓が収まらない / 入力の写しが Presentation にでき、ログへ漏れる経路が増える |
| Overengineering | タスクを使わないのに UnobservedTaskException を登録 / 骨組みの段階でログの30日削除と安全策一式(残すかは実装プランの見直しで判断)|
| Missing | 0.3秒の Enter 無視・やめる前の確認・窓の大きさ・合計が増える説明・フォーカスの確認項目(いずれも wpf-shell 側)|

## 条件(本実装の前に満たす)
- C1: P0-1・P0-2 の対応を設計に入れる
- C2: wpf-shell の requirements・design・decisions を承認済みの UX・UI 設計に合わせて書き直す(P1 の大半)
- C3: H1〜H5 を人間が決め、承認済み文書(ui-design 5・7章、DECISIONS)に反映する
- C4: 書き直した wpf-shell をレビュー役で再確認し、P0・P1 が0件になる

## 人間に決めてほしいこと
| # | 質問 | AI の推奨 |
|---|---|---|
| H1 | 想定外の例外の後は「終了」にする(承認済み ui-design 7章を変更)| 終了 |
| H2 | 差分の文章は Core の Describe を使う(置換は「4文字目はeではなくaです」。ui-design 5章の言い回しを変更)| Describe |
| H3 | 最小の窓の大きさを作業領域で頭打ちにし、足りなければ縦スクロール | そうする |
| H4 | 判定画面に「あなたの入力」を出すのは可、ログ・ToString には出さない(DECISIONS「進行役 Flow」に明記。苦手一覧の最後の誤答と同じ扱い)| そうする |
| H5 | UX 専用のレビュー役(ux-reviewer)を `.claude/agents/` に追加し、第5・6工程で使う | 追加する |
