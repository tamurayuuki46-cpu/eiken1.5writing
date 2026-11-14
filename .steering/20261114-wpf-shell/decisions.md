# decisions.md — WPF の骨組み

<!-- 置き場所: .steering/20261114-wpf-shell/decisions.md
     役割: タスク中に決めたこと。タスク完了時に「移す先」を見て docs/ や rules/ へ移す -->

| 日付 | 決めたこと | 理由 | 検討した他の案 | 移す先(docs / rules / なし) |
|---|---|---|---|---|
| 2026-11-14 | 端末のモードは 日本語・穴あき だけ(音声・フラッシュは次のタスク) | 骨組みを小さくし、実機確認を先に通す | フラッシュも含める / 4モードすべて | なし(次のタスクで変わる) |
| 2026-11-14 | ViewModel は net10.0 の別プロジェクト `EikenSpelling.Presentation` に置く。依存は `Wpf → Presentation → Flow → Persistence → Core` | ViewModel のテストを Linux(クラウド・CI)でも回すため | Wpf の中に置く | docs/DECISIONS.md「層の依存」「UI 技術」、.claude/rules/architecture.md、CLAUDE.md 4章 |
| 2026-11-14 | MVVM の基底は手書き(NuGet なし) | 基底は小さく、依存を増やさない | CommunityToolkit.Mvvm | docs/DECISIONS.md「UI 技術」 |
| 2026-11-14 | 仮の単語データは自作の訳で十数語を exe と同じフォルダ(Data/words.json)に同梱。`sources` は各語が実際に入っているリスト名を確かめて付け、確かめられない語は入れない | 辞書(U2)と切り離して実機確認を進める。出典を不正確にしない | 埋め込みリソース / ユーザーが用意 / すべて NGSL | docs/DECISIONS.md「収録語数とデータ」(仮データであること) |
| 2026-11-14 | 表示する画面はホーム・再開の選択・出題・判定・結果・両方破損・終了のみ(苦手一覧・設定画面とホームからの入口は次のタスク) | 中断・破損の後の起動で必ず出る画面がないと先へ進めない | Flow の全画面 | なし |
| 2026-11-14 | ホームに簡単な練習設定(問題数・モード・苦手のみ)。問題数の選択肢は Flow の `HomeScreen` に `QuestionCountChoices` を足して渡す | 選択肢の正本(LearningSettings)を1つに保つ。Flow の既存の振る舞いは変えない | 自由入力(Flow を変えない)/ Presentation に [5,10,20] を書く(SSoT 違反) | docs/DECISIONS.md「進行役 Flow」 |
| 2026-11-14 | 保存先は既定 `%APPDATA%\EikenSpelling`、起動引数 `--data-dir <絶対パス>` で差し替え。値なし・2回は空文字にして FolderInvalid、知らない引数は無視して warn でログ | 実機の破損・ロック確認を本番の記録と分ける。検査は Flow に任せる | %APPDATA% 固定 | docs/DECISIONS.md「保存・復旧」 |
| 2026-11-14 | ~~空の入力も画面で弾かず `Answer` に渡す(誤答として記録)~~ **取り消し**(下の行) | ― | ― | ― |
| 2026-11-14 | 空欄・空白だけの入力は画面から送らない(回答ボタンを無効、Enter は何もしない)。「空か」は Core に `SpellingJudge.IsBlank` を足して判定する | 承認済みの UX 設計 Q-UX4。空欄で誤答・苦手登録になるのを防ぐ。規則(Normalize)を2か所に書かない | Presentation で `IsNullOrWhiteSpace` / Flow に回答できるかの判断を足す | docs/DECISIONS.md「UI 技術」(記録済み) |
| 2026-11-14 | 差分の文章は Core の `SpellingDiffService.Describe` を使い、Presentation の DiffText は作らない | SSoT(設計レビュー P1-1、H2) | Presentation で文章を作る | docs/DECISIONS.md「UI 技術」(記録済み) |
| 2026-11-14 | 想定外の例外のあとは伏せた文言を出して終了する | Flow の状態が壊れている可能性(H1) | 安全な画面へ戻る | docs/DECISIONS.md「UI 技術」(記録済み) |
| 2026-11-14 | 古い画面の操作は何もしない(IsActive と sender の確認)。どの画面も今の画面になってから0.3秒はコマンドを受け付けない(AcceptGate、コマンドの実行の中で見る、TimeProvider.GetTimestamp で測る)。リピートの Enter は窓の PreviewKeyDown で止める | 連打で Flow の例外 → 終了になるのを防ぐ(P0-2)。溜まったクリック・ボタンが自分で受ける Enter も止める(再レビュー P1)。誤操作防止(Q-UX3) | Flow の例外を捕まえて無視 / 判定画面の Enter だけを止める | なし |
| 2026-11-14 | 判定画面の「n / 合計」と入力は ShellViewModel の LastAnswer が1か所で持つ | 再保存で FeedbackViewModel が作り直されても消えないため(再レビュー P1) | QuestionViewModel から直接引き継ぐ | なし |
| 2026-11-14 | 確認ダイアログは骨組みでは MessageBox(UI 設計 4章の「ダイアログ内だけ Danger 地」は再現しない) | 骨組みを小さくする。見た目の妥協 | 自作のダイアログ | なし(次のタスクで見直す) |
| 2026-11-14 | 見た目は UI 設計(採用案 A+B)の色・文字・部品の状態をトークンにして実装する。窓は既定 1000×680・最小 800×560 を作業領域で頭打ち | 第3工程で承認済み(E1〜E4、H3) | 見た目は次のタスク | docs/DECISIONS.md「UI 技術」(記録済み) |
| 2026-11-14 | ログは手書きのファイルログ `%LOCALAPPDATA%\EikenSpelling\logs\app-yyyyMMdd.log`。例外は型名・HResult・発生したメソッド名まで(スタックトレースとメッセージは書かない)。30日より古いものは起動時に消す | NuGet なし。パス(ユーザー名)を残さない | Debug 出力だけ / Microsoft.Extensions.Logging | .claude/rules/logging.md、docs/DECISIONS.md |
| 2026-11-14 | 文書(DECISIONS・architecture.md・CLAUDE.md・testing.md)の更新はこの PR に含める | 依存の向きが変わるため、コードと文書を同時に揃える | 別 PR | なし |
| 2026-11-14 | 問題数の選択は区切りボタンではなくコンボボックス(UI 設計 4章からの変更。**ユーザー承認済み**) | キーボードで選びやすく、骨組みを小さくする | UI 設計どおり区切りボタン | ui-design.md 4章に追記済み |
| 2026-11-14 | 想定外の例外の終了ダイアログは IUserPrompt を通さず MessageBox を直接出し、その間は窓を無効にする(design 4.7 からの差分) | IUserPrompt の実装は閉じた後にフォーカスを戻すため、終了の途中では動かしたくない | IUserPrompt を通す | なし |
| 2026-11-14 | 保存の再試行の待ちを、本物のファイルを使うテスト(Flow・Presentation)で 0 から 20ms にした | Windows の全体実行で、一瞬のロックとみられる失敗がまれに起きた(Flow・Presentation で各1回)。変更後は全体6回すべて通過 | 0 のまま | なし |
| 2026-11-14 | 判定画面の「苦手に登録されました」を ScreenText が StateBefore/StateAfter から判定している件(Core の SessionSummary の BecameWeak と同じ規則が2か所。PR 前レビュー P1)は**次のタスクへ回す**(ユーザー決定)。今は動きが同じ | 直すには Core の変更(AnswerChange に BecameWeak を足す、または分類を公開)が要り、design の範囲外 | 今回 Core も直す | 次のタスクの requirements |
| 2026-11-14 | Wpf の csproj は Presentation だけを直接参照する(Flow・Core は推移的な参照で使う。design 2章の「Presentation・Flow を参照」からの差分) | 参照を最小にする | Flow も直接参照 | なし |
