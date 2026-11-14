# design.md — WPF の骨組み(起動・ホーム → 練習 → 判定 → 結果)

<!-- 置き場所: .steering/20261114-wpf-shell/design.md
     役割: requirements.md を「どう作るか」。実装前にここを確認してもらう(技評 SDD) -->

## 1. 方針
- 3層に分ける。
  - **Flow(既存)**: 画面の状態と操作の約束を持つ。今回は振る舞いを変えない(`HomeScreen` に問題数の選択肢を足すだけ。決定 1)。
  - **Presentation(新規、net10.0)**: Flow の画面の状態(不変の record)を、表示用の ViewModel(INotifyPropertyChanged・ICommand)に写す。
    操作は Flow のメソッドを呼んでから `Screen` を読み直すだけ。起動引数の解釈・ログの書き出しもここに置き、Linux でテストする。
  - **Wpf(新規、net10.0-windows)**: XAML の View、二重起動の防止、`%APPDATA%`・exe の場所の解決、ダイアログ、未処理の例外の受け口(合成の起点)。
- 画面の切り替えは1枚のウィンドウの `ContentControl` に今の画面の ViewModel を入れ、型ごとの `DataTemplate` で View を選ぶ。
  どの画面を出すかは Flow の `Screen` の型だけで決まり、Presentation は判断しない。
- MVVM の基底(`ObservableObject`・`RelayCommand`)は手書き(ユーザー決定。NuGet なし)。
- 端末で使えるモードは 日本語・穴あき だけを Flow に渡す(ユーザー決定)。音声・フラッシュの問題は Flow の選択で出なくなる。
- 検討して見送った案:
  - ViewModel を Wpf の中に置く案: ViewModel のテストが Windows でしか動かない(ユーザー決定で別プロジェクト)
  - CommunityToolkit.Mvvm: NuGet の追加になる。基底は小さいので手書き(ユーザー決定)
  - Microsoft.Extensions.Logging: NuGet の追加になり、標準ではファイルに出せない(ユーザー決定で手書き)
  - 画面ごとに Window を開く案: 二重起動時に前に出すウィンドウが定まらず、フォーカスの管理も増える
  - 判定画面の差分の文言を Flow・Core に足す案: 文言は表示の都合。Presentation に置けば Linux でテストでき、Flow を変えずに済む

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Presentation/EikenSpelling.Presentation.csproj | net10.0。Flow・Core を参照。NuGet 追加なし | 新規 |
| src/EikenSpelling.Presentation/Mvvm/ObservableObject.cs, RelayCommand.cs | 手書きの MVVM 基底 | 新規 |
| src/EikenSpelling.Presentation/ShellViewModel.cs | Flow を持ち、今の画面の ViewModel・通知・終了要求を公開する(4.3) | 新規 |
| src/EikenSpelling.Presentation/ScreenViewModelFactory.cs | `FlowScreen` の型 → 画面の ViewModel(4.3 の表) | 新規 |
| src/EikenSpelling.Presentation/Screens/*.cs | Home・ResumePrompt・Question・Feedback・Result・BothCorrupt・Fatal の各 ViewModel | 新規 |
| src/EikenSpelling.Presentation/Text/DiffText.cs, ScreenText.cs | 差分・状態の変化・モード名・空の理由の表示文言(4.5) | 新規 |
| src/EikenSpelling.Presentation/StartupArguments.cs | `--data-dir` の解釈(4.1) | 新規 |
| src/EikenSpelling.Presentation/Logging/AppLog.cs, FileLog.cs | ログの口と、キーと値の行をファイルに追記する実装(4.6) | 新規 |
| src/EikenSpelling.Presentation/IUserPrompt.cs | 確認ダイアログの口(「新しい記録で開始」・保存前に閉じる)。実装は Wpf | 新規 |
| src/EikenSpelling.Wpf/EikenSpelling.Wpf.csproj | net10.0-windows、UseWPF、`EnableWindowsTargeting`(Linux の CI でビルドするため)。Presentation・Flow を参照 | 新規 |
| src/EikenSpelling.Wpf/App.xaml(.cs) | 合成の起点(4.1)。二重起動の防止・未処理の例外 | 新規 |
| src/EikenSpelling.Wpf/SingleInstance.cs | 名前付き Mutex と、既存の画面を前に出す合図(4.2) | 新規 |
| src/EikenSpelling.Wpf/MainWindow.xaml(.cs) | `ContentControl` と DataTemplate、閉じる前の確認(R18) | 新規 |
| src/EikenSpelling.Wpf/Views/*.xaml | 各画面の View(コードビハインドはフォーカス・IME だけ) | 新規 |
| src/EikenSpelling.Wpf/MessageBoxPrompt.cs | `IUserPrompt` の実装 | 新規 |
| src/EikenSpelling.Wpf/Data/words.json | 仮の単語データ(4.4)。出力フォルダへコピー | 新規 |
| tests/EikenSpelling.Presentation.Tests/ | xUnit。一時フォルダの本物のファイルで Flow を動かし、ViewModel を確かめる | 新規 |
| EikenSpelling.sln | 3プロジェクトを追加 | 変更 |
| .github/workflows/ci.yml | 変更なしの見込み(ソリューション全体をビルド・テストしている)。Linux で WPF のビルドが通らなければ報告する | 変更なし |
| docs/DECISIONS.md、.claude/rules/architecture.md、CLAUDE.md 4章、.claude/rules/testing.md | 依存の向きに Presentation を追加、Wpf の構成・ログの場所・起動引数を記録 | 変更 |
| src/EikenSpelling.Flow/FlowScreens.cs, PracticeFlow.cs と Flow.Tests | `HomeScreen` に `QuestionCountChoices`(設定の `QuestionCountChoices` から作る)を足す。既存の振る舞いは変えない(決定 1) | 変更 |
| .claude/rules/logging.md | ログの場所・例外の書き方(決定 5)を追記 | 変更 |

## 3. データ
- 扱うデータ: 画面の状態(`FlowScreen`、既存)、練習の選択(`PracticeChoice`、既存)、words.json(既存の形)、ログの行(新規、ファイルだけ)
- データの持ち主: progress.json・settings.json は Persistence の ProgressRepository / SettingsRepository(変更なし。画面側は読み書きしない)。
  words.json は読むだけ。ログファイルは `FileLog`
- Schema 変更: なし。ログは学習記録ではないので互換性の対象外

## 4. 処理の流れ

### 4.1 起動(App.OnStartup)
1. `SingleInstance.TryAcquire()`。取れなければ既存のアプリに合図を送り、何もせずに `Shutdown`(4.2)
2. ログの用意: `%LOCALAPPDATA%\EikenSpelling\logs`(取れなければログなしで続ける。ログの失敗でアプリを止めない)
3. 未処理の例外の受け口を登録(`DispatcherUnhandledException`・`AppDomain.UnhandledException`・`TaskScheduler.UnobservedTaskException`)
4. 保存先: `StartupArguments.Parse(args)`
   - `--data-dir <値>` があればその値をそのまま使う(検査は Flow。相対パスなら FatalScreen(FolderInvalid))
   - 無ければ `Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `EikenSpelling`(取れなければ空文字 → FolderInvalid)
   - `--data-dir` の値が無い・2回ある: 空文字にして Flow の FolderInvalid にする。知らない引数は無視して warn でログ(引数の中身は書かない)(決定 2)
5. 単語データ: `AppContext.BaseDirectory\Data\words.json` を UTF-8 で読む。無い・読めない(IOException・UnauthorizedAccessException)なら null を渡す
   → Flow が FatalScreen(WordSetInvalid)。読めなかった種類だけを warn でログに出す(パスは書かない)
6. `PracticeFlow.Start(new FlowOptions { DataDirectory, WordSetJson, AvailableModes = {Japanese, Blank} })`
7. `ShellViewModel` を作り、MainWindow を表示する。最初の画面が `Diagnostic` を持てば error でログに出す(4.6)
- 起動だけでは progress.json・settings.json を書かない(Flow の約束。バックアップからの復旧だけは Flow が書く)

### 4.2 二重起動の防止(SingleInstance)
- 名前付き Mutex `Local\EikenSpelling.SingleInstance`(同じユーザーのセッション内で1つ)。保存先を変えて起動しても1つだけ
- 既存の画面を前に出す: 名前付き `EventWaitHandle`(`Local\EikenSpelling.Activate`)を1つ目が待ち、2つ目が Set する。
  1つ目は合図を受けたら UI スレッドで最小化を戻して `Activate()`(前に出ないときのために `Topmost` を一瞬だけ立てる)
- 見送った案: 2つ目がプロセスを探して `SetForegroundWindow`(P/Invoke が要り、前面化の制限にかかりやすい)
- Mutex は終了まで持ち続ける(GC で解放されないよう App のフィールドに持つ)。前のプロセスが異常終了した後の `AbandonedMutexException` は取得できたとみなす

### 4.3 画面の表示と操作(ShellViewModel)
- `ShellViewModel` は `PracticeFlow` を1つ持ち、公開するもの:
  - `Current`: 今の画面の ViewModel(変わったら PropertyChanged)
  - `Notices`: 起動時の通知(画面の上に帯で出し、「閉じる」で消す。次の操作で画面が変わったら消す)
  - `ExitRequested`(イベント): 「終了」が押された。Wpf が `Shutdown` する
  - `HasUnsavedAnswer`: 今の画面が `FeedbackScreen { Saved: false }`(R18 の確認に使う)
- 操作の形はすべて同じ: `Invoke(flow => flow.Xxx(...))` → `Refresh()`(`flow.Screen` を読み、`ScreenViewModelFactory` で写し、
  `Diagnostic` があればログに出す)。Presentation はどの画面へ行くかを判断しない
- ボタンは今の画面で Flow が受け付ける操作だけを出す(下の表)。それでも Flow が `InvalidOperationException` を投げたら
  プログラムの誤りなので、捕まえずに未処理の例外の受け口へ任せる(R17)

| Flow の画面 | ViewModel | 表示 | 操作 → Flow |
|---|---|---|---|
| HomeScreen | HomeViewModel | 学習状況5項目、練習設定(問題数・モード・苦手のみ)、`Message` | 始める → `StartPractice(choice)` |
| ResumePromptScreen | ResumePromptViewModel | 開始日時(日本時間)、回答済み / 全体、`CannotResumeReason`、`Message` | 続きから → `Resume()`(CanResume のときだけ)/ 終了して新しく始める → `DiscardAndGoHome()` |
| QuestionScreen | QuestionViewModel | 「n / 全体」、日本語モードは `Japanese`、穴あきは `Blanks`、再練習の印、入力欄 | 回答 → `Answer(input)` / やめる → `Quit()` |
| FeedbackScreen | FeedbackViewModel | 正誤、正しい綴り、差分の文言、苦手状態の変化、`SaveFailure` | 次へ・結果へ → `Next()`(Saved のときだけ)/ 再保存 → `RetrySave()`(Saved が偽のときだけ) |
| ResultScreen | ResultViewModel | 正答数 / 問題数、誤答した単語、再練習の結果、苦手状態の変化 | ホームへ → `GoHome()` |
| BothCorruptScreen | BothCorruptViewModel | 説明、`Message` | 新しい記録で開始 → 確認(`IUserPrompt`)→ `StartFresh()` / 終了 → `ExitRequested` |
| FatalScreen | FatalViewModel | `Reason` | 終了 → `ExitRequested` / もう一度読む → `Reload()`(CanReload のときだけ) |
| WeakListScreen・SettingsScreen | ― | 今回は開く操作が無いので出ない。来たら未対応として例外(プログラムの誤り) | ― |

- ホームの練習設定(R6・R7)
  - 初期値は `HomeScreen.Defaults`。画面の値はそのまま `PracticeChoice` にして渡す(範囲の検査・0語の判断は Flow と Core)
  - モードの選択肢: 「自動ミックス」+ 端末で使えるモード(`FlowOptions.AvailableModes` と同じ集合を ShellViewModel に渡す)。
    保存済みの手動モードが音声・フラッシュならその値も選択肢に出す(選んで始めると Flow が ManualModeUnavailable の理由を返す)
  - 問題数の選択肢: `HomeScreen.QuestionCountChoices`(決定 1)。`Defaults.QuestionCount` が選択肢に無ければその値も出す
  - フラッシュ秒数は画面に出さず `Defaults.FlashSeconds` をそのまま渡す
- 出題の入力(R8・R19・R20): 入力文字列はそのまま `Answer` に渡す(前後の空白・大文字小文字の扱いは Core の判定)。
  空の入力も渡す(Flow が誤答として記録する。Presentation で弾かない)(決定 3)
- 判定画面の Enter: Saved なら「次へ」、Saved が偽なら何もしない(再保存はボタンだけ。押し続けによる連打を避ける)

### 4.4 仮の単語データ(Data/words.json)
- 準1級ライティングで綴りを間違えやすい語を十数語(例: accommodate, necessary, environment, government, recommend, definitely,
  separate, occurrence, embarrass, privilege, conscientious, maintenance)。訳は自作の短い訳、穴あきは手で指定(`blankSource: manual`)
- `wordSetVersion` は `sample-0.1` のように仮であることが分かる値にする。`WordSetValidator` でエラー 0 をテストで確かめる
- `sources`: 各語が実際に入っているリスト名(NGSL・NAWL・BSL)を一覧で確かめて付ける。確かめられない語は入れない(決定 4。
  既定の `EnabledSources` に入っていないと新規の問題として出ないため、出典は必ず付ける)
- 出力フォルダへのコピー: csproj の `<Content Include="Data\words.json" CopyToOutputDirectory="PreserveNewest" />`

### 4.5 表示の文言(Presentation/Text)
- 差分(`SpellingDiff` → 文言): DECISIONS「差分表示」の例に合わせる。例「6文字目の m が不足しています」「3文字目は i ではなく e です」「4文字目の x は余分です」。
  位置は 1 始まりで表示する(`CorrectPosition`・`InputPosition` の基準は実装時に Core のテストで確かめる)
- 苦手状態の変化: `AnswerChange` → 「苦手に登録しました」「克服しました」「苦手に戻りました」「再確認が進みました」(複数あれば並べる)
- 結果画面の変化: `StateChangeKind` の3種類 → 「新たに苦手」「克服」「苦手に戻った」
- 空の理由(`SelectionEmptyReason`): Flow が `Message` を持つのでそれを出す。Presentation は文言を作らない
- 日時: `ResumeSummary.StartedAt` を日本時間(`JstClock`)の「yyyy/MM/dd HH:mm」で表示
- ここに置くのは表示の言い回しだけ。正誤・状態の判断はしない

### 4.6 ログ(FileLog)
- 場所: `%LOCALAPPDATA%\EikenSpelling\logs\app-yyyyMMdd.log`(日本時間の日付)。学習記録と分けるため保存先(`--data-dir`)とは別
- 形式: 1行1件、`time=… level=… op=… sessionId=… reason=…`(logging.md の最低限の項目)
- 書くもの: 画面の状態の `Diagnostic`(error、`FeedbackScreen` なら `SessionId` も)、単語データを読めなかった種類(warn)、
  知らない起動引数(warn)、未処理の例外(error。型名・HResult・発生したメソッド名まで。`Message` とスタックトレースはパスを含みうるので書かない)(決定 5)
- 書かないもの: パス・誤答入力・画面の状態の丸ごと・単語データの中身
- 世代: 起動時に 30 日より古い `app-*.log` を消す(決定 5)
- 失敗: ログの書き込みに失敗しても例外を外へ出さない(アプリを止めない)。1回失敗したら以後そのファイルには書かない

### 4.7 想定外の例外(R17)
- 受け口で捕まえたら、ログに出し、`IUserPrompt` で「問題が起きたため終了します。学習記録は回答ごとに保存されています。」を出して終了
  (`DispatcherUnhandledException` は `Handled = true` にして既定のダイアログを出さない)
- 保存できていない回答(Saved が偽)があっても、ここでは保存を試みない(Flow の状態が壊れている可能性があるため)。次の起動で再開の選択から続く

### 4.8 閉じる操作(R18)
- MainWindow の `Closing` で `ShellViewModel.HasUnsavedAnswer` が真なら「この回答はまだ保存できていません。閉じると、この1問の回答は記録されません。閉じますか?」を確認
- それ以外は確認しない(回答ごとに保存済み。練習の途中で閉じても次の起動で再開を選べる)

## 5. テスト方針
- 単体(Presentation.Tests、Linux・Windows):
  - Flow は本物を使う(一時フォルダ + テスト用の小さな words.json)。保存の失敗は既存の `IFileOperations` の差し替えで起こす(Flow.Tests と同じ形)。
    モックは `IUserPrompt`(決まった答えを返す)と `AppLog`(記録するだけ)だけ
  - 画面の型ごとに正しい ViewModel になる / 出す操作が Flow の受け付ける操作と一致する(Saved の真偽、CanResume、CanReload)
  - 通しの流れ: ホーム → 始める → 回答(正答・誤答)→ 判定の差分の文言 → 次へ → 再練習 → 結果 → ホーム
  - 保存失敗 → 再保存のボタンだけ → ログに Diagnostic と sessionId が出て、入力・パスが出ない → 再保存の成功で次へが出る
  - 起動して画面を出しただけではファイルが書かれない
  - 両方破損: 確認で「いいえ」なら何も書かない、「はい」で `StartFresh` / 終了で `ExitRequested`
  - `StartupArguments`: 引数なし / `--data-dir 絶対パス` / 相対パス / 値なし / 2回 / 知らない引数
  - `DiffText`: 不足・余分・置換・入れ替わり(recieve)・同じ文字が続く不足(accomodate)の文言
  - `FileLog`: 1行の形、禁止項目を書かない、書けないフォルダで例外を外へ出さない、古い世代の削除の境界(29 / 30 / 31 日)
  - 同梱の words.json が `WordSetLoader.Parse` と `WordSetValidator` でエラー 0
- Wpf: 自動テストは作らない(View とコードビハインドは薄く、合成だけにする)。Linux の CI ではビルドだけ確かめる
- 手動確認(Windows 実機。結果は tasklist.md に残す): requirements.md 5章の手動の項目と handoff.md 5.7
  (初回起動 → 練習 → 閉じる → 再起動 → 続きから → 結果、ロックでの保存失敗、破損、二重起動、IME、Enter・フォーカス、高 DPI)

## 6. リスクと未決事項
- 決定済み(2026-11-14、ユーザーが案どおり承認。詳細は decisions.md):
  1. ホームの問題数の選択肢は Flow の `HomeScreen` に `QuestionCountChoices` を足して渡す(Flow.Tests を追加。既存の振る舞いは変えない)
  2. 起動引数: `--data-dir` の値なし・2回は空文字にして FolderInvalid、知らない引数は無視して warn でログ
  3. 空の入力は画面で弾かず `Answer` に渡す(誤答として記録)
  4. 仮の単語データの `sources` は実際に入っているリスト名を確かめて付け、確かめられない語は入れない
  5. 例外のログは型名・HResult・発生したメソッド名まで(スタックトレース・メッセージは書かない)。30 日より古いログは起動時に消す
  6. DECISIONS・architecture.md・CLAUDE.md・testing.md・logging.md の更新はこの PR に含める
- 【要確認】なし(② のレビューで出たものはここに足す)
- 応答の固まり: Flow は同期で保存し、保存の再試行で待つことがある(UI スレッドが止まる)。骨組みでは同期のまま(Flow は1スレッドから使う前提)。
  実機で気になれば次のタスクで考える
- Linux の CI での WPF のビルド: `EnableWindowsTargeting` でビルドできる見込み。通らなければ止めて報告する(CI の変更は確認を取る)
- 前面化の制限: Windows はほかのプロセスの前面化を制限する。`Topmost` を一瞬立てても前に出ないことがあれば、タスクバーの点滅で知らせる(実機で確かめる)
