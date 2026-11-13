# design.md — 画面の手前の進行役(Flow)

## 1. 方針
- 新しいプロジェクト `EikenSpelling.Flow`(net10.0)に、画面の状態を持つ進行役 `PracticeFlow` を置く。
  WPF は `PracticeFlow.Screen`(今の画面の状態)を表示し、ボタン操作を `PracticeFlow` のメソッドに渡すだけにする。
- 学習の判断は既存の Core(QuestionSelector・SessionManager・SpellingDiffService)、保存は既存の Persistence(SettingsRepository・ProgressRepository)に任せる。
  進行役は「どの順で呼ぶか」「失敗したらどの画面にするか」だけを持つ。
- 結果画面の集計 `SessionSummary` は PracticeSession だけから求まる純粋な計算なので Core の Application に置く(スマホ版でも使える)。
- 検討して見送った案:
  - プロジェクト名 `EikenSpelling.App`: WPF の `App` クラス(App.xaml)と名前空間が紛らわしいため `Flow` にした【要確認 1】
  - Core に保存の interface を置く案: Core に保存の結果型が入り込む。ユーザー決定で新プロジェクト
  - 画面遷移を WPF に持たせる案: 保存失敗で次へ進まない約束(13.3)をテストで守れない。ユーザー決定で進行役が持つ
  - MVVM の ViewModel をここに作る案: INotifyPropertyChanged などは WPF 側の都合。Flow は不変の画面状態を返し、WPF の ViewModel が写す

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Flow/EikenSpelling.Flow.csproj | net10.0、Core・Persistence を参照。NuGet 追加なし | 新規 |
| src/EikenSpelling.Flow/PracticeFlow.cs | 進行役(状態と操作) | 新規 |
| src/EikenSpelling.Flow/FlowScreens.cs | 画面の状態の型(下の表) | 新規 |
| src/EikenSpelling.Flow/FlowOptions.cs | 起動に渡すもの(保存先・単語データの JSON・使えるモード・時刻・乱数・ID の作り方)と練習の選択 `PracticeChoice` | 新規 |
| src/EikenSpelling.Flow/PracticeChoiceApplier.cs | 練習の選択を LearningSettings に重ねる(settings.json は書かない) | 新規 |
| src/EikenSpelling.Core/Application/SessionSummary.cs | 結果画面の集計 | 新規 |
| tests/EikenSpelling.Flow.Tests/ | xUnit。一時フォルダの本物のファイル、失敗は IFileOperations の差し替え | 新規 |
| tests/EikenSpelling.Core.Tests/SessionSummaryTests.cs | 集計の単体テスト | 新規 |
| EikenSpelling.sln、.github/workflows/ci.yml | プロジェクトの追加(CI がプロジェクト名を列挙していれば追加) | 変更 |
| .claude/rules/architecture.md、CLAUDE.md 4章 | 層と依存の向きに Flow を追加: `Wpf → Flow → Persistence → Core`(`Flow → Core` も可) | 変更 |

1ファイル 300 行を超えそうなら PracticeFlow を「起動」と「練習」の2つの partial ではなく、起動を `FlowStartup`(静的)に分ける。

## 3. データ
- 扱うデータ: LearningSettings、WordSet、ProgressDocument(LearningProgress + PracticeSession?)
- データの持ち主: ファイルは従来どおり ProgressRepository / SettingsRepository。進行役はメモリ上の「今の文書」(最後に保存を試みた ProgressDocument)を1つだけ持つ
- Schema 変更: なし
- 保存時の WordSetVersion・SourceVersion は今の単語データの値を入れる(読み込んだ文書の値は使わない)

## 4. 処理の流れ

### 4.1 画面の状態(`FlowScreen`、不変の record。派生型で表す)
| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen` | 通知の一覧(起動時の1回分)、直前の開始が0語だったときの理由 | StartPractice(choice) |
| `ResumePromptScreen` | 未完了セッションの概要(開始日時・回答済み数/全体数) | Resume() / DiscardAndGoHome() |
| `QuestionScreen` | 今の問題(単語ID・綴り・モード・穴あき・日本語訳・再練習か)、通し番号と全体数 | Answer(input) / QuitToHome() |
| `FeedbackScreen` | 正誤・正しい綴り・差分(誤答のみ)・状態の変化・保存できたか・完了か | 保存済みなら Next()、失敗なら RetrySave() |
| `ResultScreen` | SessionSummary | GoHome() |
| `BothCorruptScreen` | 理由 | StartFresh() / (終了は WPF が閉じるだけ) |
| `FatalScreen` | 種類(NewerVersion / Unavailable / WordSetInvalid / FolderInvalid)と理由。Unavailable だけ再読込可 | Reload()(Unavailable のときだけ) |

- 受け付けない操作が来たら `InvalidOperationException`(状態は変えない)。
- 「終了」は WPF がウィンドウを閉じるだけ。進行役は何も書かない(セッションは回答ごとに保存済み)。
- 問題の表示に要る単語の情報(日本語訳・穴あき)は今の WordSet から引く。単語データに無い単語(データ更新で消えた)は綴りだけ出し、日本語訳・穴あきは空【要確認 3】。

### 4.2 起動(`PracticeFlow.Start(FlowOptions)` → 最初の画面)
1. 保存先が絶対パスでなければ FatalScreen(FolderInvalid)。
2. 設定の Load。NewerVersion・Corrupt(初期値で動く)・Unavailable は通知に積み、初期値で続ける(DECISIONS.md)。
3. 単語データ: `WordSetLoader.Parse` → `WordSetValidator.Validate` に Error があれば FatalScreen(WordSetInvalid)。Warning は無視(開発者向け)。
4. 学習記録の Load:
   - Loaded / FirstRun / RecoveredFromBackup → 警告・復旧・SessionDiscarded を通知に積む。Session があれば ResumePromptScreen、無ければ HomeScreen
   - BothCorrupt → BothCorruptScreen
   - NewerVersion → FatalScreen(NewerVersion)
   - Unavailable → FatalScreen(Unavailable)。Reload() で 4. からやり直す
5. 起動時に「再開しない古い answerId」の整理は SessionManager.PruneAnswerIds(keep = 今のセッション ID)で行い、次の保存で反映する。

### 4.3 再開・終了して新しく始める
- Resume(): セッションの Current を QuestionScreen にする。セッションが完了済み(全問回答済みで残っていた)なら SessionValidator が弾くので来ない想定。来たら Abandon 扱いで HomeScreen。
- DiscardAndGoHome(): `SessionManager.Abandon` → Session = null で保存。
  - 成功 → HomeScreen。失敗 → ResumePromptScreen のまま「保存できませんでした」を表示(もう一度押せる)【要確認 2】

### 4.4 練習の開始 `StartPractice(PracticeChoice)`
- `PracticeChoice` = AutoMix・ManualMode・QuestionCount・WeakWordsOnly(練習設定画面の4項目。v1.0 16.1)。
- 今の LearningSettings の Practice に重ねた設定を作り、SettingsValidator で検査(不正なら ArgumentException。画面は選択肢しか出さない前提)。
  重ねた設定は settings.json に書かない。セッションに丸ごと保存されるので再開で使われる(v1.2 7章)。前回の選択の記憶は設定保存のタスクで扱う。
- `QuestionSelector.Select(wordSet, progress, settings, availableModes, now, random)`。
  - Plan が0件 → HomeScreen(EmptyReason つき)。何も保存しない
- `SessionManager.Start(newId(), now, plan, settings)` → 文書に Session を入れて保存。
  - 成功 → QuestionScreen
  - 失敗 → HomeScreen(「練習を始められませんでした(保存できません)」)。メモリ上も開始前の文書に戻す

### 4.5 回答 → 判定 → 保存
1. `Answer(input)`: `SessionManager.Answer(session, progress, input, now)`。
2. 新しい文書 = { Progress = 結果.Progress, Session = 結果.SessionToSave, WordSetVersion/SourceVersion = 今の単語データ }。
3. **メモリ上の文書を新しい文書に置き換えてから** 保存する(保存の成否にかかわらず。Answer を同じ問題で二度呼ばないため)。
4. 保存の成否を FeedbackScreen に入れる。差分は誤答のときだけ `SpellingDiffService.Diff`。
5. `RetrySave()`: 同じ文書をもう一度 Save。成功したら Saved = true の FeedbackScreen。失敗ならそのまま(理由を更新)。
6. `Next()`(保存済みのときだけ): 完了なら ResultScreen(SessionSummary.From(結果.Session))、そうでなければ次の QuestionScreen。

- 保存失敗のまま WPF を閉じた場合: ファイルは前の問題までの内容なので、次回はその時点から再開(この回答は記録されない)。これは仕様 13.3 の「二重記録を防止」と両立する(記録されないが二重にもならない)。

### 4.6 両方破損
- StartFresh(): `ProgressRepository.StartFresh(wordSetVersion, sourceVersion)`。成功 → 空の文書で HomeScreen(通知「新しい記録で始めました。壊れたファイルは残してあります」)。失敗 → BothCorruptScreen のまま理由を表示。

### 4.7 `SessionSummary.From(PracticeSession)`(Core / Application)
- NormalCount、NormalCorrect(通常問題の正答数)
- MistakenWords: 通常問題で誤答した単語(出題順、単語ID・綴り)
- Retries: 単語ごとの再練習の結果(回数・最後に正解したか)。再練習が無い単語は含めない
- StateChanges: 単語ごとに、セッション内の最初の StateBefore と最後の StateAfter が違うもの(新たに苦手 / 克服 / 苦手に戻った)。
  回答ごとの StateBefore/StateAfter は保存されているので、再開をはさんでも同じ結果になる
- 未完了のセッションでも回答済みの分で計算できる(例外にしない)

## 5. テスト方針
- 単体(Core): SessionSummary — 全問正解 / 誤答と再練習 / 再練習で最後まで不正解 / 苦手登録・克服・苦手に戻る / 未完了 / 回答0件
- 結合(Flow、一時フォルダの本物のファイル、FakeTime は既存テストと同じ自作 TimeProvider、乱数は固定シード、ID は連番):
  - 受入条件(requirements.md 5.)を1件1テスト以上
  - 保存失敗: IFileOperations の差し替えで Save の書き込みを失敗させる → FeedbackScreen(Saved=false)、Next は例外、RetrySave 成功で Next 可。progress.json の answerId・回数が1回分
  - 保存失敗のまま作り直し → 前の時点から再開
  - 受け付けない操作(各画面で違う操作)は例外で状態が変わらない
  - 再起動の代わりに PracticeFlow を作り直す(同じフォルダ)
  - 通知の文言にフォルダのパス・誤答入力が入らない
- 手動確認: WPF 作成後に Windows 実機で(このタスクでは対象外)

## 6. リスクと未決事項
- 【要確認 1】プロジェクト名は `EikenSpelling.Flow` でよいか(案: Flow。App は WPF の App クラスと紛らわしい)
- 【要確認 2】「終了して新しく始める」の保存に失敗したとき: 案は「再開の選択のまま、失敗を表示して押し直せる」。
  別案: メモリ上だけ捨ててホームへ進む(次の保存で消える)→ 次の開始の保存も失敗しやすいので案を推す
- 【要確認 3】再開時、単語データに無い単語の問題: 案は「綴りだけで出す(日本語訳・穴あき・音声なし)」。SessionManager は綴りの写しで判定できる。
  モードが日本語・穴あきなのに中身が無い表示になる。別案: その問題を飛ばす仕組みを足す(Core の変更が要る)→ 今回は案で、照合は P2 にある「単語データ更新後の再開」タスクへ
- リスク: Flow を同期で書く。保存が遅い環境(ネットワークドライブ等)で画面が止まる → %APPDATA% 前提で許容。WPF で await が要るなら Task.Run で包む
- リスク: 使えるモード(音声の有無)は起動時に固定。途中で音声が使えなくなった場合は WPF 側で扱う
