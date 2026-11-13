# design.md — 画面の手前の進行役(Flow)

## 1. 方針
- 新しいプロジェクト `EikenSpelling.Flow`(net10.0)に、画面の状態を持つ進行役 `PracticeFlow` を置く。
  WPF は `PracticeFlow.Screen`(今の画面の状態)を表示し、ボタン操作を `PracticeFlow` のメソッドに渡すだけにする。
- 学習の判断は既存の Core(QuestionSelector・SessionManager・SpellingDiffService)、保存は既存の Persistence(SettingsRepository・ProgressRepository)に任せる。
  進行役は「どの順で呼ぶか」「失敗したらどの画面にするか」だけを持つ。
- 結果画面の集計 `SessionSummary` は PracticeSession だけから求まる純粋な計算なので Core の Application に置く(スマホ版でも使える)。
- 仕様 v1.0 15.4 では「ファイル保存・復元」「エラー通知」は WPF の責務。これを Flow(Windows 非依存)に移すのは仕様からの差分として DECISIONS.md に記録する(Persistence を分けたときと同じ理由: Linux と Windows の両方でテストするため)。
- 検討して見送った案:
  - プロジェクト名 `EikenSpelling.App`: WPF の `App` クラス(App.xaml)と紛らわしいため `Flow`(【要確認 1】案で確定)
  - Core に保存の interface を置く案: Core に保存の結果型が入り込む。ユーザー決定で新プロジェクト
  - 画面遷移を WPF に持たせる案: 保存失敗で次へ進まない約束(13.3)をテストで守れない。ユーザー決定で進行役が持つ
  - MVVM の ViewModel をここに作る案: INotifyPropertyChanged などは WPF 側の都合。Flow は不変の画面状態を返し、WPF の ViewModel が写す

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Flow/EikenSpelling.Flow.csproj | net10.0、Core・Persistence を参照。NuGet 追加なし | 新規 |
| src/EikenSpelling.Flow/PracticeFlow.cs | 進行役(練習の状態と操作) | 新規 |
| src/EikenSpelling.Flow/FlowStartup.cs | 起動・再読込(設定 → 単語データ → 学習記録 → 最初の画面) | 新規 |
| src/EikenSpelling.Flow/FlowScreens.cs | 画面の状態の型(4.1 の表) | 新規 |
| src/EikenSpelling.Flow/FlowOptions.cs | 起動に渡すもの(4.2)と練習の選択 `PracticeChoice` | 新規 |
| src/EikenSpelling.Flow/PracticeChoiceApplier.cs | 練習の選択を LearningSettings に重ねる(settings.json は書かない) | 新規 |
| src/EikenSpelling.Flow/ResumeCheck.cs | 未完了セッションを今の単語データ・使えるモードで出題できるかの確認(4.3) | 新規 |
| src/EikenSpelling.Core/Application/SessionSummary.cs | 結果画面の集計 | 新規 |
| tests/EikenSpelling.Flow.Tests/ | xUnit。一時フォルダの本物のファイル、失敗は IFileOperations の差し替え | 新規 |
| tests/EikenSpelling.Core.Tests/SessionSummaryTests.cs | 集計の単体テスト | 新規 |
| EikenSpelling.sln、.github/workflows/ci.yml | プロジェクトの追加(CI がプロジェクト名を列挙していれば追加) | 変更 |
| .claude/rules/architecture.md、CLAUDE.md 4章 | 層と依存の向きに Flow を追加: `Wpf → Flow → Persistence → Core`(`Flow → Core` も可) | 変更 |
| docs/DECISIONS.md | 「仕様からの差分」(保存・復元・エラー通知の順番を Flow に)と「層の依存」 | 変更 |

## 3. データ
- 扱うデータ: LearningSettings、WordSet、ProgressDocument(LearningProgress + PracticeSession?)
- データの持ち主: ファイルは従来どおり ProgressRepository / SettingsRepository。進行役はメモリ上の「今の文書」(最後に保存を試みた ProgressDocument)を1つだけ持つ
- 不変条件: メモリ上の Session が null のときだけ HomeScreen になる。Session があるときに StartPractice は呼べない(SessionManager の「先に Abandon」の約束)
- Schema 変更: なし
- 保存時の WordSetVersion・SourceVersion は今の単語データの値を入れる(読み込んだ文書の値は使わない)。版が違うときの扱いは後続タスク(リスク参照)
- answerId の整理(古いセッションの分を消す)は ProgressRepository の読込がすでに行う。Flow では重ねて行わない

## 4. 処理の流れ

### 4.1 画面の状態(`FlowScreen`、不変の record。派生型で表す)
共通の項目: `Notices`(起動時の通知。起動・再読込の後の最初の画面だけが持ち、次の画面に移ると空)、`Diagnostic`(ログ用の理由。保存・読込に失敗した画面だけが持つ。Persistence の Reason をそのまま入れる。画面には出さない)。

| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen` | 直前の開始が0語だったときの理由、開始の保存失敗の文言 | StartPractice(choice) |
| `ResumePromptScreen` | 未完了セッションの概要(開始日時・回答済み数/全体数)、再開できるか(4.3)、直前の破棄の保存失敗の文言 | Resume()(再開できるときだけ) / DiscardAndGoHome() |
| `QuestionScreen` | 今の問題(単語ID・綴り・モード・穴あき・日本語訳・再練習か)、通し番号と全体数、フラッシュ表示秒数(セッションの設定から) | Answer(input) / Quit() |
| `FeedbackScreen` | 正誤・正しい綴り・差分(誤答のみ)・状態の変化・完了か・保存できたか・保存失敗の理由・セッションID | 保存済みなら Next()、失敗なら RetrySave() |
| `ResultScreen` | SessionSummary | GoHome() |
| `BothCorruptScreen` | 理由、直前の StartFresh の失敗の文言 | StartFresh() / (終了は WPF が閉じるだけ) |
| `FatalScreen` | 種類(NewerVersion / Unavailable / WordSetInvalid / FolderInvalid)と理由。Unavailable だけ再読込可 | Reload()(Unavailable のときだけ) |

- 受け付けない操作が来たら `InvalidOperationException`(状態・ファイルは変えない)。引数の不正(input が null など)は `ArgumentException` 系で、同じく状態を変えない。
- 「終了」は WPF がウィンドウを閉じるだけ。進行役は何も書かない(セッションは回答ごとに保存済み)。
- Quit()(練習をやめる)→ ResumePromptScreen。何も保存しない(回答ごとに保存済み)。保存に失敗した回答が残っている FeedbackScreen では Quit を受け付けない(RetrySave か終了だけ)。
- 理由・文言には保存先のパス・誤答入力・例外の内側(InnerException)を入れない。ログ(WPF が出す)に要る理由と sessionId は FeedbackScreen が持つ。画面の状態を丸ごと(ToString で)ログに出さないことを WPF のタスクの design に引き継ぐ。

### 4.2 起動(`PracticeFlow.Start(FlowOptions)`。読込は `FlowStartup.Load`、最初の画面は PracticeFlow だけが決める)
FlowOptions: 保存先フォルダ、単語データの JSON 文字列、使えるモード、TimeProvider、Random、新しいセッションIDの作り方(`Func<Guid>`)、
テスト用の `IFileOperations?` と `TimeSpan? RetryDelay`(Persistence の再試行の待ち。テストでは Zero)。

1. 保存先: null・空・`Path.IsPathFullyQualified` が偽 → FatalScreen(FolderInvalid)。何も作らない。Repository の生成もこの判定の後で行う。
2. 設定の Load。Loaded は何もしない。RecoveredFromBackup は通知。BothCorrupt・NewerVersion・Unavailable は初期値で動く旨を通知に積み、初期値で続ける(DECISIONS.md)。
3. 単語データ: `WordSetLoader.Parse` の `InvalidDataException` を捕まえる → FatalScreen(WordSetInvalid、固定文)。`WordSetValidator.Validate` に Error があれば同じ。Warning は無視(開発者向け)。
4. 学習記録の Load:
   - Loaded / FirstRun / RecoveredFromBackup → 警告・復旧・SessionDiscarded を通知に積む。Session があれば ResumePromptScreen、無ければ HomeScreen
   - BothCorrupt → BothCorruptScreen
   - NewerVersion → FatalScreen(NewerVersion)
   - Unavailable → FatalScreen(Unavailable)
5. Reload()(Unavailable のときだけ): Repository を作り直して **2.(設定の Load)から** やり直す。通知は作り直す(二重に積まない)。

### 4.3 再開・終了して新しく始める
- 再開できるか(`ResumeCheck`): これから出る問題がすべて、今の単語データに**同じ綴りで**あり、そのモードで表示できる(日本語は訳あり、穴あきは Blanks あり、音声は使えるモードに含まれる)こと。
  これから出る問題 = 列の未回答の分 + 通常問題の途中なら、回答済みの通常問題の誤答の再練習(まだ列に無い。モードは RetryModeRule で求める)(⑥で修正)
  - できない → ResumePromptScreen の「再開できるか」を偽にし、理由を出す。利用者は「終了して新しく始める」だけ選べる(【要確認 3】②③の指摘で案を変更: 答えようのない問題を誤答として記録すると苦手登録・連続正答のリセットが起き、学習記録が汚れるため)
- Resume(): Current を QuestionScreen にする。
- DiscardAndGoHome(): `SessionManager.Abandon` → Session = null で保存。
  - 成功 → HomeScreen。失敗 → ResumePromptScreen のまま失敗の文言を出す(押し直せる。メモリ上の文書は元のまま、Resume も選べる)(【要確認 2】案で確定)

### 4.4 練習の開始 `StartPractice(PracticeChoice)`
- `PracticeChoice` = AutoMix・ManualMode・QuestionCount・WeakWordsOnly・FlashSeconds(練習設定画面の項目。v1.0 16.1、v1.2 2章 A)。
- 今の LearningSettings の Practice に重ねた設定を作る。Flow では別に検査しない(QuestionSelector・SessionManager が検査し、不正なら ArgumentException。状態は変えない。画面は選択肢しか出さない前提)。
  手動モードが使えるモードに無い場合は例外ではなく、QuestionSelector が EmptyReason.ManualModeUnavailable を返す。
  重ねた設定は settings.json に書かない。セッションに丸ごと保存されるので再開で使われる(v1.2 7章)。前回の選択の記憶は設定保存のタスクで扱う。
- `QuestionSelector.Select(wordSet, progress, settings, availableModes, now, random)`。
  - Plan が0件 → HomeScreen(EmptyReason つき)。何も保存しない
- `SessionManager.Start(newId(), now, plan, settings)` → 文書に Session を入れて保存。
  - 成功 → QuestionScreen
  - 失敗 → HomeScreen(「練習を始められませんでした(保存できません)」)。メモリ上は開始前の文書のまま

### 4.5 回答 → 判定 → 保存
1. `Answer(input)`: `SessionManager.Answer(session, progress, input, now)`。
2. 新しい文書 = { Progress = 結果.Progress, Session = 結果.SessionToSave, WordSetVersion/SourceVersion = 今の単語データ }。
3. **メモリ上の文書を新しい文書に置き換えてから** 保存する(保存の成否にかかわらず。Answer を同じ問題で二度呼ばないため)。
4. 保存の成否と失敗の理由を FeedbackScreen に入れる。差分は誤答のときだけ `SpellingDiffService.Diff`。
5. `RetrySave()`: 同じ文書をもう一度 Save。成功したら Saved = true の FeedbackScreen。失敗ならそのまま(理由を更新)。
   再試行しても直らない失敗(セッションの検査で弾かれる・変換できない)も同じ扱いで、文言は「保存できません。アプリを終了してください。最後に保存できた問題から再開できます」とする(Persistence の結果では区別できないため、何度失敗しても同じ画面で終了を案内する)。
6. `Next()`(保存済みのときだけ): 完了なら ResultScreen(SessionSummary.From(結果.Session))、そうでなければ次の QuestionScreen。

- 最後の問題(完了)で保存に失敗した場合も同じ。Session = null の文書を再保存する。失敗のまま閉じると、次回は最後の問題から再開(その回答は記録されない)。
- 保存失敗のまま WPF を閉じた場合: ファイルは前の問題までの内容なので、次回はその時点から再開(この回答は記録されない)。仕様 13.3 の「二重記録を防止」と両立する。

### 4.6 両方破損
- StartFresh(): `ProgressRepository.StartFresh(wordSetVersion, sourceVersion)`。成功 → 空の文書で HomeScreen(通知「新しい記録で始めました。壊れたファイルは残してあります」)。失敗 → BothCorruptScreen のまま失敗の文言(押し直せる)。

### 4.7 `SessionSummary.From(PracticeSession)`(Core / Application)
- NormalCount、NormalAnswered(回答済みの通常問題。未完了の集計用)、NormalCorrect(通常問題の正答数)
- MistakenWords: 通常問題で誤答した単語(出題順、単語ID・綴り)
- Retries: 単語ごとの再練習の結果(回数・最後に正解したか)。再練習が無い単語は含めない
- StateChanges: 単語ごとに、セッション内の最初の StateBefore と最後の StateAfter から、次の3種類だけを出す(v1.0 16.1「苦手状態の変化」)
  - 新たに苦手(Weak 以外 → Weak。ただし Mastered → Weak は下)/ 克服(→ Mastered)/ 苦手に戻った(Mastered → Weak)
  - 記録なし → Normal などその他の変化は出さない
- 回答ごとの StateBefore/StateAfter は保存されているので、再開をはさんでも同じ結果になる
- 未完了のセッションでも回答済みの分で計算できる(例外にしない)

## 5. テスト方針
- 単体(Core): SessionSummary — 全問正解 / 誤答と再練習 / 再練習で最後まで不正解(RetryCount 0・3)/ 苦手登録・克服・苦手に戻る・記録なし→通常は出ない / 未完了 / 回答0件
- 結合(Flow): 一時フォルダの本物のファイル。時刻は既存テストと同じ自作 TimeProvider、乱数は固定シード、ID は連番、RetryDelay は Zero。失敗は IFileOperations の差し替え。「書かれていない」はフォルダの一覧とバイト列で確かめる(更新日時は使わない)。
  - 受入条件(requirements.md 5.)を1件1テスト以上
  - 起動: FolderInvalid(null・空・相対。Windows CI で `C:foo` 相当)/ 単語データの壊れた JSON・新しすぎる schemaVersion・Validate の Error / 設定の BothCorrupt・NewerVersion・Unavailable・RecoveredFromBackup で通知と初期値での開始、settings.json は変わらない
  - Unavailable → Reload の結果ごと(Loaded・セッションあり → ResumePrompt / BothCorrupt / NewerVersion / 再び Unavailable)。通知が二重にならない
  - 起動時の answerId: 古いセッションの answerId を含むファイルで、セッションなし → 次の保存で消える、セッションあり → 再開後の回答が重複例外にならない
  - 再開: 通常問題の途中・再練習の1問目・再練習の2問目以降(列の最後に足された分)で作り直し、Current と残りが同じ、最後の SessionSummary が中断なしと一致。日本時間 23:59:59 → 翌 0:00:00 をまたぐ再開
  - 再開できない: 単語データに無い単語・訳の無い日本語モード・Blanks の無い穴あき・使えない音声 → Resume は例外、Discard だけできる
  - 0語の理由ごと(苦手のみで苦手なし・手動モードが使えない・語彙リストで全部外れる・克服対象モードの無い単語だけ)。何も保存しない
  - 保存失敗: 回答(途中・最後の問題・再練習の最後)/ 失敗0・1・2回の後の成功で answerId と回数が1回分 / 連続失敗で画面とファイルが変わらず理由(Diagnostic)が最後の保存のものになる / 失敗のまま作り直し → 前の時点から再開(最後の問題なら最後の問題から)
  - セッションの検査で弾かれる保存失敗は、Flow からは起こせない(Flow は SessionManager が作ったセッションだけを保存する)。Persistence のテストで確かめている。Flow 側は同じ失敗の扱い(画面は同じ)
  - 開始の保存失敗 → HomeScreen と文言、メモリは開始前、次の開始は成功 / 破棄の保存失敗 → ResumePrompt に残り Resume も選べる、押し直しで成功(記録は残りセッションだけ消える)/ StartFresh の失敗 → 押し直しで成功、WordSetVersion が今の値。「終了」ではフォルダとバイト列が変わらない
  - Quit → ResumePrompt(何も書かない)→ Resume / Discard → StartPractice
  - 日をまたぐ回答: 苦手語の対象正答が日本時間の日付で数えられ、FeedbackScreen の変化と SessionSummary が合う
  - 入力: 空文字・空白だけ → 誤答・差分あり / null → ArgumentNullException で状態不変 / 100・101 文字(サロゲートペアの位置を含む)→ 記録は 100 文字まで
  - 受け付けない操作: 7画面 × 全操作の表を Theory で(Feedback(保存済み)の RetrySave、Next の2回呼び、Result の Answer、Fatal(NewerVersion)の Reload、BothCorrupt の StartPractice、保存失敗中の Quit など)。例外になり、Screen が同じインスタンス、ファイルが変わらない
  - 通知の文言にフォルダのパス・誤答入力が入らない。通知は起動後の最初の画面だけ(GoHome の後は空)
- 手動確認: WPF 作成後に Windows 実機で(このタスクでは対象外)

## 6. リスクと未決事項
- 【要確認 1】プロジェクト名 `EikenSpelling.Flow` — 案で確定(レビューで問題なし)
- 【要確認 2】破棄の保存失敗は再開の選択のまま押し直せる — 案で確定(レビューで問題なし)
- 【要確認 3】単語データに無い・表示できない問題が残るセッション — ②③の指摘で「綴りだけで出す」から「再開させず、終了して新しく始めるだけ選べる」に変更(4.3)
- SessionManager.Answer の重複の例外: 読込時の SessionValidator が answerId の集合の一致を検査し、合わなければセッションを捨てるため、Flow の通常の操作では起こらない(⑥ edge-case の確認)。起きた場合は例外のまま(状態は変わる前)で、WPF の全体の例外処理で受ける
- P2: 単語データの大きさの上限は WPF の読込で設ける(同梱ファイル)。読み込んだ記録と単語データの版が違うときの扱い(保存で版が今の値に書き換わる)は後続タスク
- リスク: Flow は同期で書く。保存が遅い環境で画面が止まる → %APPDATA% 前提で許容。WPF で必要なら Task.Run で包む(1スレッドから呼ぶ前提は守る)
- リスク: 使えるモード(音声の有無)は起動時に固定。途中で音声が使えなくなった場合は WPF 側で扱う
