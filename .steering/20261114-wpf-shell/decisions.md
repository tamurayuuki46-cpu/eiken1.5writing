# decisions.md — WPF の骨組み

<!-- 置き場所: .steering/20261114-wpf-shell/decisions.md
     役割: タスク中に決めたこと。タスク完了時に「移す先」を見て docs/ や rules/ へ移す -->

| 日付 | 決めたこと | 理由 | 検討した他の案 | 移す先(docs / rules / なし) |
|---|---|---|---|---|
| 2026-11-14 | 端末のモードは 日本語・穴あき だけ(音声・フラッシュは次のタスク) | 骨組みを小さくし、実機確認を先に通す | フラッシュも含める / 4モードすべて | なし(次のタスクで変わる) |
| 2026-11-14 | ViewModel は net10.0 の別プロジェクト `EikenSpelling.Presentation` に置く。依存は `Wpf → Presentation → Flow → Persistence → Core` | ViewModel のテストを Linux(クラウド・CI)でも回すため | Wpf の中に置く | docs/DECISIONS.md「層の依存」「UI 技術」、.claude/rules/architecture.md、CLAUDE.md 4章 |
| 2026-11-14 | MVVM の基底は手書き(NuGet なし) | 基底は小さく、依存を増やさない | CommunityToolkit.Mvvm | docs/DECISIONS.md「UI 技術」 |
| 2026-11-14 | 仮の単語データは自作の訳で十数語を exe と同じフォルダ(Data/words.json)に同梱。`sources` は各語が実際に入っているリスト名を確かめて付け、確かめられない語は入れない | 辞書(U2)と切り離して実機確認を進める。出典を不正確にしない | 埋め込みリソース / ユーザーが用意 / すべて NGSL | docs/DECISIONS.md「収録語数とデータ」(仮データであること) |
| 2026-11-14 | 表示する画面はホーム・再開の選択・出題・判定・結果・両方破損・終了のみ(苦手一覧・設定画面は次のタスク) | 中断・破損の後の起動で必ず出る画面がないと先へ進めない | Flow の全画面 | なし |
| 2026-11-14 | ホームに簡単な練習設定(問題数・モード・苦手のみ)。問題数の選択肢は Flow の `HomeScreen` に `QuestionCountChoices` を足して渡す | 選択肢の正本(LearningSettings)を1つに保つ。Flow の既存の振る舞いは変えない | 自由入力(Flow を変えない)/ Presentation に [5,10,20] を書く(SSoT 違反) | docs/DECISIONS.md「進行役 Flow」 |
| 2026-11-14 | 保存先は既定 `%APPDATA%\EikenSpelling`、起動引数 `--data-dir <絶対パス>` で差し替え。値なし・2回は空文字にして FolderInvalid、知らない引数は無視して warn でログ | 実機の破損・ロック確認を本番の記録と分ける。検査は Flow に任せる | %APPDATA% 固定 | docs/DECISIONS.md「保存・復旧」 |
| 2026-11-14 | 空の入力も画面で弾かず `Answer` に渡す(誤答として記録) | 判定は Core・Flow の責務(Flow.Tests の Empty_input_is_a_wrong_answer と同じ) | 画面で弾いて「入力してください」 | なし |
| 2026-11-14 | ログは手書きのファイルログ `%LOCALAPPDATA%\EikenSpelling\logs\app-yyyyMMdd.log`。例外は型名・HResult・発生したメソッド名まで(スタックトレースとメッセージは書かない)。30日より古いものは起動時に消す | NuGet なし。パス(ユーザー名)を残さない | Debug 出力だけ / Microsoft.Extensions.Logging | .claude/rules/logging.md、docs/DECISIONS.md |
| 2026-11-14 | 文書(DECISIONS・architecture.md・CLAUDE.md・testing.md)の更新はこの PR に含める | 依存の向きが変わるため、コードと文書を同時に揃える | 別 PR | なし |
