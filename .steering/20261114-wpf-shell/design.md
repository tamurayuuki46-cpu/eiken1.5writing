# design.md — WPF の骨組み(起動・ホーム → 練習 → 判定 → 結果)

<!-- 置き場所: .steering/20261114-wpf-shell/design.md
     役割: requirements.md を「どう作るか」。実装前にここを確認してもらう(技評 SDD)
     改訂(2026-11-14): 承認済みの UX 設計・UI 設計と第4工程の設計レビュー(P0・P1、H1〜H5)に合わせて書き直した -->

## 1. 方針
- 3層に分ける。
  - **Core・Flow(既存)**: 画面の状態と操作の約束、判定と差分の文章を持つ。今回の変更は2点だけ(決定 1・決定 3):
    Flow の `HomeScreen` に問題数の選択肢を足す / Core の `SpellingJudge` に「空か」の判定 `IsBlank` を足す。
  - **Presentation(新規、net10.0)**: Flow の画面の状態(不変の record)を、表示用の ViewModel(INotifyPropertyChanged・ICommand)に写す。
    操作は Flow のメソッドを呼んでから `Screen` を読み直すだけ。起動引数の解釈・ログ・Enter を無視する時間の判定もここに置き、Linux でテストする。
  - **Wpf(新規、net10.0-windows)**: XAML の View とテーマ、二重起動の防止、`%APPDATA%`・exe の場所の解決、ダイアログ、未処理の例外の受け口(合成の起点)。
- 画面の切り替えは1枚のウィンドウの `ContentControl` に今の画面の ViewModel を入れ、型ごとの `DataTemplate` で View を選ぶ。
  どの画面を出すかは Flow の `Screen` の型だけで決まり、Presentation は判断しない。
- 判断を伴う文言(保存失敗・開始失敗・通知・再開できない理由)は Flow のもの、差分の文章は Core の `SpellingDiffService.Describe` を使う。
  Presentation の `ScreenText` が持つのは値の言い換え(0語の理由・記録の変化・モード名・ボタン名)だけ。
- MVVM の基底(`ObservableObject`・`RelayCommand`)は手書き(ユーザー決定。NuGet なし)。
- 端末で使えるモードは 日本語・穴あき だけを Flow に渡す(ユーザー決定)。
- 検討して見送った案:
  - ViewModel を Wpf の中に置く案: ViewModel のテストが Windows でしか動かない(ユーザー決定で別プロジェクト)
  - CommunityToolkit.Mvvm / Microsoft.Extensions.Logging: NuGet の追加になる(ユーザー決定で手書き)
  - 画面ごとに Window を開く案: 二重起動時に前に出すウィンドウが定まらず、フォーカスの管理も増える
  - 差分の文章を Presentation で作る案: Core の Describe と2か所になる(SSoT 違反。レビュー P1-1、H2)
  - 空欄の判定を Presentation に書く案(`IsNullOrWhiteSpace`): Core の `Normalize` と同じ規則が2か所になる(レビュー P0-1)
  - 想定外の例外のあと安全な画面へ戻る案: Flow の状態が壊れている可能性があり、壊れた記録を保存しかねない(H1)

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Core/Domain/SpellingJudge.cs と Core.Tests | `IsBlank(string? input)` = `Normalize(input).Length == 0` を足す(決定 3)。既存の振る舞いは変えない | 変更 |
| src/EikenSpelling.Flow/FlowScreens.cs, PracticeFlow.cs と Flow.Tests | `HomeScreen` に `QuestionCountChoices`(設定の `QuestionCountChoices` から作る)を足す(決定 1)| 変更 |
| src/EikenSpelling.Presentation/EikenSpelling.Presentation.csproj | net10.0。Flow・Core を参照。NuGet 追加なし | 新規 |
| src/EikenSpelling.Presentation/Mvvm/ObservableObject.cs, RelayCommand.cs | 手書きの MVVM 基底。`RelayCommand` は `ICommand` を実装し、`RaiseCanExecuteChanged()` を自分で出す(WPF の CommandManager に頼らない)| 新規 |
| src/EikenSpelling.Presentation/ShellViewModel.cs | Flow を持ち、今の画面の ViewModel・通知・確認・終了要求を公開する(4.3)| 新規 |
| src/EikenSpelling.Presentation/ScreenViewModelFactory.cs | `FlowScreen` の型 → 画面の ViewModel(4.3 の表)| 新規 |
| src/EikenSpelling.Presentation/Screens/*.cs | Home・ResumePrompt・Question・Feedback・Result・BothCorrupt・Fatal の各 ViewModel と共通の基底 `ScreenViewModel`(IsActive)| 新規 |
| src/EikenSpelling.Presentation/Screens/DiffDisplay.cs | 判定の表示用データ: 正しいスペルの文字ごとの強調、正規化した入力と取り消し線の位置、Describe の文章(最初の3件+「ほか n か所」)(4.5)| 新規 |
| src/EikenSpelling.Presentation/Text/ScreenText.cs | 0語の理由・記録の変化・結果の変化・モード名の言い換え(4.5)| 新規 |
| src/EikenSpelling.Presentation/Input/AcceptGate.cs | 画面が今の画面になってから 0.3 秒未満のコマンドを無視する判定。`TimeProvider.GetTimestamp` で測る(4.3)| 新規 |
| src/EikenSpelling.Presentation/StartupArguments.cs | `--data-dir` の解釈(4.1)| 新規 |
| src/EikenSpelling.Presentation/Logging/AppLog.cs, FileLog.cs | ログの口と、キーと値の行をファイルに追記する実装(4.6)| 新規 |
| src/EikenSpelling.Presentation/IUserPrompt.cs | 確認・お知らせのダイアログの口(新しい記録で始める・やめる・保存前に閉じる・想定外の例外)。実装は Wpf | 新規 |
| src/EikenSpelling.Wpf/EikenSpelling.Wpf.csproj | net10.0-windows、UseWPF、`EnableWindowsTargeting`(Linux の CI でビルドするため)。Presentation・Flow を参照 | 新規 |
| src/EikenSpelling.Wpf/App.xaml(.cs) | 合成の起点(4.1)。二重起動の防止・未処理の例外 | 新規 |
| src/EikenSpelling.Wpf/Theme/Tokens.xaml, Controls.xaml | UI 設計 2〜4章の色・文字・部品の状態(4.9)| 新規 |
| src/EikenSpelling.Wpf/SingleInstance.cs | 名前付き Mutex と、既存の画面を前に出す合図(4.2)| 新規 |
| src/EikenSpelling.Wpf/MainWindow.xaml(.cs) | 通知の帯、`ContentControl` と DataTemplate、窓の大きさ(4.9)、閉じる前の確認(R25)| 新規 |
| src/EikenSpelling.Wpf/Views/*.xaml | 各画面の View。コードビハインドは初期フォーカス・IME・キー(Enter・Esc・リピート)の受け渡しだけ | 新規 |
| src/EikenSpelling.Wpf/MessageBoxPrompt.cs | `IUserPrompt` の実装(確認の初期の選択はキャンセル / いいえ)| 新規 |
| src/EikenSpelling.Wpf/Data/words.json | 仮の単語データ(4.4)。出力フォルダへコピー | 新規 |
| tests/EikenSpelling.Presentation.Tests/ | xUnit。一時フォルダの本物のファイルで Flow を動かし、ViewModel を確かめる | 新規 |
| EikenSpelling.sln | 3プロジェクトを追加 | 変更 |
| .github/workflows/ci.yml | 変更なしの見込み。Linux で WPF のビルドが通らなければ止めて報告する | 変更なし |
| docs/DECISIONS.md、.claude/rules/architecture.md、CLAUDE.md 4章、.claude/rules/testing.md、.claude/rules/logging.md | 依存の向きに Presentation を追加 / 「Presentation は Windows 専用のアセンブリを参照しない(`ICommand` は可)」/ 文言の持ち主の線引き(判断は Flow・Core、言い換えだけ Presentation)/ logging.md の対象に Presentation を足し、書かないもの(パス・誤答入力・画面と ViewModel の状態)を明記 | 変更 |

## 3. データ
- 扱うデータ: 画面の状態(`FlowScreen`、既存)、練習の選択(`PracticeChoice`、既存)、words.json(既存の形)、ログの行(新規、ファイルだけ)
- データの持ち主: progress.json・settings.json は Persistence の ProgressRepository / SettingsRepository(変更なし。画面側は読み書きしない)。
  words.json は読むだけ。ログファイルは `FileLog`
- 画面側が一時的に覚えるもの(ファイルには書かない): 直前の出題の「n / 合計」と、回答した入力(判定画面の表示用。4.3)
- Schema 変更: なし

## 4. 処理の流れ

### 4.1 起動(App.OnStartup)
1. `SingleInstance.TryAcquire()`。取れなければ既存のアプリに合図を送り、何もせずに `Shutdown`(4.2)
2. ログの用意: `%LOCALAPPDATA%\EikenSpelling\logs`(取れなければログなしで続ける。ログの失敗でアプリを止めない)
3. 未処理の例外の受け口を登録(`DispatcherUnhandledException`・`AppDomain.UnhandledException`)(4.7)
4. 保存先: `StartupArguments.Parse(args)`
   - `--data-dir <値>` があればその値をそのまま使う(検査は Flow。相対パスなら FatalScreen(FolderInvalid))
   - 無ければ `Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `EikenSpelling`(取れなければ空文字 → FolderInvalid)
   - `--data-dir` の値が無い・2回ある: 空文字にして FolderInvalid。知らない引数は無視して warn でログ(引数の中身は書かない)(決定 2)
5. 単語データ: `AppContext.BaseDirectory\Data\words.json` を UTF-8 で読む。無い・読めない(IOException・UnauthorizedAccessException)なら null を渡す
   → Flow が FatalScreen(WordSetInvalid)。読めなかった種類だけを warn でログに出す(パスは書かない)
6. `PracticeFlow.Start(new FlowOptions { DataDirectory, WordSetJson, AvailableModes = {Japanese, Blank} })`
7. `ShellViewModel` を作り、MainWindow を表示する。最初の画面が `Diagnostic` を持てば error でログに出す(4.6)
- 起動だけでは progress.json・settings.json を書かない(Flow の約束。バックアップからの復旧だけは Flow が書く)

### 4.2 二重起動の防止(SingleInstance)
- 名前付き Mutex `Local\EikenSpelling.SingleInstance`(同じユーザーのログオンセッション内で1つ)。保存先を変えて起動しても1つだけ
- 既存の画面を前に出す: 名前付き `EventWaitHandle`(`Local\EikenSpelling.Activate`)を1つ目が待ち、2つ目が Set する。
  1つ目は合図を受けたら UI スレッドで最小化を戻して `Activate()`(前に出ないときのために `Topmost` を一瞬だけ立てる)
- Mutex は終了まで持ち続ける(App のフィールドに持つ)。前のプロセスが異常終了した後の `AbandonedMutexException` は取得できたとみなす
- 限界(DECISIONS に記録): ユーザーの切り替えで別のログオンセッションから同じ保存先を開くと防げない / 同じセッションのほかのプロセスが先に同じ名前を作ると起動できない

### 4.3 画面の表示と操作(ShellViewModel)
- `ShellViewModel` は `PracticeFlow` を1つ持ち、公開するもの:
  - `Current`: 今の画面の ViewModel(変わったら PropertyChanged)
  - `Notices`: 通知の一覧。画面の状態の `Notices` が空でなければ足す(起動時・StartFresh 後のホーム・再読込の後)。同じ文がすでにあれば足さない。「閉じる」で消すまで残し、画面が変わっても消さない(R5)。
    足すのは Flow がその画面の状態を新しく作ったとき(操作の直後の `Refresh`)だけにし、利用者が閉じた後に同じ画面の状態から足し直さない。閉じた後のフォーカスは今の画面の初期フォーカスへ戻す
  - `ExitRequested`(イベント): 「終了」が押された。Wpf が `Shutdown` する
  - `HasUnsavedAnswer`: 今の画面が `FeedbackScreen { Saved: false }`(R25 の確認に使う)
- 操作の形はすべて同じ: `Invoke(sender, flow => flow.Xxx(...))` → `Refresh()`(`flow.Screen` を読み、`ScreenViewModelFactory` で写し、
  `Diagnostic` があればログに出す)。Presentation はどの画面へ行くかを判断しない
- **連打・古い操作の扱い(R19、レビュー P0-2)**:
  - 画面の ViewModel は基底 `ScreenViewModel` の `IsActive` を持つ。`Refresh()` で入れ替えるとき、古い ViewModel の `IsActive` を偽にし、
    その全コマンドの `RaiseCanExecuteChanged()` を出す(ボタンが無効になる)
  - `Invoke` は sender が今の `Current` でなければ何もしない(例外にしない)
  - それでも Flow が `InvalidOperationException` を投げたらプログラムの誤りなので、捕まえずに 4.7 の受け口へ任せる
- **受け付け開始のゲート(R12・R19、再レビュー P1)**: 画面の ViewModel が `Current` になってから 0.3 秒未満は、**その画面のすべてのコマンドを
  何もしない**(Enter・クリックの区別なし)。保存中に溜まったクリックや、素早い Enter が、同じ位置にある新しい画面のボタンを押すのを防ぐ
  (例: 回答のダブルクリックで判定を飛ばす、結果の「ホームへ」の連打で練習が始まる)
  - ゲートは `AcceptGate`(Presentation)。`TimeProvider.GetTimestamp()` で受け付け開始の時刻を覚え、`GetElapsedTime(...) >= 300ms` で通す
    (時計の巻き戻しに影響されない)。値 0.3 秒は画面の操作の定数(学習ルールではない)として AcceptGate に置く
  - **数え始めは、画面を描いて初期フォーカスを移した後**(再々レビュー P1): MainWindow が 4.9 の初期フォーカスの移動を終えたら `Current.BeginAccepting()` を呼び、
    そこから測る。`BeginAccepting` の前は時間に関係なくすべてのコマンドを無視する(WPF は描画を溜まった入力より先に処理するため、
    描画に時間がかかっても、溜まったクリックはゲートの中に届く)
  - ゲートは **コマンドの実行の中**で見る(`ScreenViewModel` がコマンドを作るときに、実行の前に `CanAccept()` を見る処理で包む。汎用の `RelayCommand` は画面を知らない)。WPF のボタンがフォーカス中に自分で受ける Enter も、
    必ずこの経路を通る。再保存で FeedbackViewModel が作り直されたときも測り直す
  - 文字の入力(回答欄への打鍵)はコマンドではないのでゲートの対象外
- **キーの押しっぱなし**: MainWindow の `PreviewKeyDown` で、`IsRepeat` が真の Enter と Esc を `Handled = true` にする(ボタン・入力欄・確認ダイアログの開き直しに届く前に止める)
- **直前の回答の値(R11、再レビュー P1)**: `ShellViewModel` が `LastAnswer`(直前の出題の Number・Total と、正規化した入力)を1か所で持つ。
  回答コマンドがゲートと sender の確認を通った後にだけ設定し(無視された実行で上書きしない)、`FeedbackScreen` 以外の画面に移ったら消す。`ScreenViewModelFactory` は FeedbackScreen を写すときにそれを受け取る。
  再保存で FeedbackViewModel が作り直されても同じ値が出る。FeedbackScreen を写すときに `LastAnswer` が無ければプログラムの誤りとして例外(4.7)

| Flow の画面 | ViewModel | 表示 | 操作 → Flow | 初期フォーカス |
|---|---|---|---|---|
| HomeScreen | HomeViewModel | 学習状況5項目、練習設定、`EmptyReason` の文言(情報の帯)、`Message`(警告の帯)| 始める → `StartPractice(choice)` | 始める |
| ResumePromptScreen | ResumePromptViewModel | 開始日時(日本時間)、回答済み / 全体、`CannotResumeReason`、`Message` | 続きから → `Resume()`(CanResume のときだけ)/ 終了して新しく始める → `DiscardAndGoHome()` | 続きから(無ければ終了して新しく始める)|
| QuestionScreen | QuestionViewModel | 進捗、「n / 合計」、モード名、日本語は `Japanese`、穴あきは `Blanks` と文字数、再練習の印と説明、入力欄 | 回答 → `Answer(input)`(`IsBlank` なら無効)/ やめる・Esc → 確認 → `Quit()` | 入力欄 |
| FeedbackScreen | FeedbackViewModel | 正誤、`DiffDisplay`、記録の変化、直前の「n / 合計」、保存の状態、`SaveFailure`(警告の帯)| Saved: 次へ・結果を見る → `Next()` / Saved でない: もう一度保存 → `RetrySave()`、次へは無効 | 次へ(保存失敗中はもう一度保存)|
| ResultScreen | ResultViewModel | 正答数 / 問題数、間違えた単語、再練習の結果、苦手状態の変化 | ホームへ → `GoHome()` | ホームへ |
| BothCorruptScreen | BothCorruptViewModel | 説明、`Message` | 新しい記録で始める → 確認(`IUserPrompt`、初期はキャンセル)→ `StartFresh()` / 終了 → `ExitRequested` | 終了 |
| FatalScreen | FatalViewModel | `Reason` | 終了 → `ExitRequested` / 再読込 → `Reload()`(CanReload のときだけ)| 再読込(無ければ終了)|
| WeakListScreen・SettingsScreen | ― | 今回は開く操作が無いので出ない。来たら未対応として例外(プログラムの誤り)| ― | ― |

- ホームの練習設定(R6・R7)
  - 初期値は `HomeScreen.Defaults`。画面の値はそのまま `PracticeChoice` にして渡す(範囲の検査・0語の判断は Flow と Core)
  - モードの選択肢: 「自動ミックス」+ 端末で使えるモード(`FlowOptions.AvailableModes` と同じ集合を ShellViewModel に渡す)。
    保存済みの手動モードが音声・フラッシュならその値も選択肢に出す(選んで始めると Flow が ManualModeUnavailable の理由を返す)
  - 問題数の選択肢: `HomeScreen.QuestionCountChoices`。`Defaults.QuestionCount` が選択肢に無ければその値も出す
  - フラッシュ秒数は画面に出さず `Defaults.FlashSeconds` をそのまま渡す
  - 0語・開始の保存失敗でホームに戻ったときは、`Defaults` ではなく直前に選んでいた値を残す(選び直しを防ぐ。HomeViewModel を作るときに前の選択を引き継ぐ)
- 出題(R8〜R10)
  - 入力が `SpellingJudge.IsBlank` の間は回答コマンドを無効にする(Enter も何もしない)。それ以外は入力をそのまま `Answer` に渡す(判定は Core)。
    回答欄の結び付けは `UpdateSourceTrigger=PropertyChanged`(打つたびに判定し直す)。空欄の間は「英単語を入力してください」を回答欄の下に出す
  - 回答欄の `MaxLength` は 100(誤答入力の記録の上限 `InputRules` と同じ値を参照する)
  - キーの案内「Enter で回答 ・ Esc でやめる」を回答欄の下に出す(各画面の主ボタンにも同じ形の案内)
  - 再練習の説明: `IsRetry` が真のとき「間違えた単語の再練習です(合計が増えました)」を出す。合計の値は Flow の `Total`
  - IME: View で回答欄の `InputMethod.IsInputMethodEnabled = false`。貼り付けた文字列の改行は View で除く(ほかの文字は Core の判定に任せる)
- 判定(R11〜R13)
  - 「n / 合計」と入力は `ShellViewModel.LastAnswer` から出す(FeedbackScreen には無いため。上の「直前の回答の値」)。
    入力は `SpellingJudge.Normalize` した文字列だけを持つ。FeedbackViewModel・DiffDisplay は class にし、`ToString` は入力を伏せる(record にしない)
  - 再練習が足された結果の合計は、次の出題画面で Flow の `Total` から出す(判定画面は直前の出題の値のまま)
  - Enter: フォーカス中のボタン(Saved なら次へ・結果を見る、Saved でなければもう一度保存)が押される。どちらも受け付け開始のゲートを通る
  - ボタン名: `Completed` が真なら「結果を見る」、偽なら「次へ」

### 4.4 仮の単語データ(Data/words.json)
- 準1級ライティングで綴りを間違えやすい語を十数語(例: accommodate, necessary, environment, government, recommend, definitely,
  separate, occurrence, embarrass, privilege, conscientious, maintenance)。訳は自作の短い訳、穴あきは手で指定(`blankSource: manual`)
- 空白・ハイフン・大文字を含む語は入れない(差分の文章が読みにくくなるため。正式な単語データで扱う)
- `wordSetVersion` は `sample-0.1` のように仮であることが分かる値にする。`WordSetValidator` でエラー 0 をテストで確かめる
- `sources`: 各語が実際に入っているリスト名(NGSL・NAWL・BSL)を一覧で確かめて付ける。確かめられない語は入れない(決定 4)
- 出力フォルダへのコピー: csproj の `<Content Include="Data\words.json" CopyToOutputDirectory="PreserveNewest" />`

### 4.5 判定の表示と文言
- `DiffDisplay`(Presentation): 入力は `SpellingDiff` の一覧・正しいスペル・正規化した入力
  - 正しいスペルの文字ごとに強調するか(Missing・Wrong の `CorrectPosition`)
  - 正規化した入力の文字ごとに取り消し線を引くか(Extra の `InputPosition`。正規化した文字列の位置なのでずれない)
  - 文章: 各 `SpellingDiff` に `SpellingDiffService.Describe` を使う。最初の3件まで、4件以上なら「ほか n か所」(n = 件数 − 3)
- `ScreenText`(Presentation、値の言い換えだけ):
  - 記録の変化: `AnswerChange` → 「苦手に登録されました」「克服しました」「苦手に戻りました」「再確認が進みました」(複数あれば並べる)
  - 結果の変化: `StateChangeKind` の3種類 → 「苦手に登録」「克服」「苦手に戻った」
  - 0語の理由: `SelectionEmptyReason` の `None` 以外の5値(NoWordsInSources・ManualModeUnavailable・NoEligibleWords・NoWeakWords・NothingToPractice)。判断は Flow・Core
  - モード名: 日本語・穴あき・音声・フラッシュ
- 日時: `ResumeSummary.StartedAt` を日本時間(`JstClock`)の「M月d日 HH:mm」で表示

### 4.6 ログ(FileLog)
- 場所: `%LOCALAPPDATA%\EikenSpelling\logs\app-yyyyMMdd.log`(日本時間の日付)。学習記録と分けるため保存先(`--data-dir`)とは別
- 形式: 1行1件、`time=… level=… op=… sessionId=… reason=…`(logging.md の最低限の項目)
- 書くもの: 画面の状態の `Diagnostic`(error、`FeedbackScreen` なら `SessionId` も)、単語データを読めなかった種類(warn)、
  知らない起動引数(warn)、未処理の例外(error。型名・HResult・発生したメソッド名まで。`AggregateException` は内側の例外も同じ項目だけ。
  `Message` とスタックトレースはパスを含みうるので書かない)(決定 5)
- 書かないもの: パス・誤答入力・画面の状態と ViewModel の丸ごと・`DiffDisplay` の文章・`SpellingDiff`(ToString に入力の文字が入る)・単語データの中身
- 世代: 起動時に 30 日より古いログを消す(決定 5)。消し過ぎないよう次を守る
  - ログのフォルダは `Path.IsPathFullyQualified` が真のときだけ使う。偽なら書き込みも削除もしない
  - 列挙は `SearchOption.TopDirectoryOnly`。名前が `^app-\d{8}\.log$` に完全一致するファイルだけを対象にする
  - 古さはファイル名の日付(日本時間)で決める。名前の日付が読めないファイルは消さない
  - 消すのはファイルだけ。1件の削除に失敗しても残りを続け、例外を外へ出さない
- 失敗: ログの書き込みに失敗しても例外を外へ出さない。1回失敗したら以後そのファイルには書かない

### 4.7 想定外の例外(R24、H1)
- 受け口で捕まえたら、まずログを同期で書き、`IUserPrompt` で「問題が起きたため終了します。最後に保存できた問題までの記録は残っています。」を出して終了
  - `DispatcherUnhandledException`: `Handled = true` にして既定のダイアログを出さず、ダイアログ → `Shutdown`
  - `AppDomain.UnhandledException`: UI スレッド以外から来ることがある。ログを書いた後、UI スレッドに出せるときだけダイアログを出す(出せなくても終了は止めない)
- 保存できていない回答(Saved が偽)があっても保存を試みない(Flow の状態が壊れている可能性があるため)。次の起動で再開の選択から続く
- 終了の前に App の「確認なしで閉じる」フラグを立てる。4.8 の閉じる前の確認はこのフラグが立っていれば出さない(例外のダイアログの後に別の確認が出ない)

### 4.8 閉じる操作(R25)
- MainWindow の `Closing` で `ShellViewModel.HasUnsavedAnswer` が真なら「この回答はまだ保存できていません。閉じると、この1問の回答は記録されません。閉じますか?」を確認(初期の選択は「いいえ」)
- それ以外は確認しない(回答ごとに保存済み。練習の途中で閉じても次の起動で再開を選べる)
- 「終了」ボタン(`ExitRequested`)と想定外の例外による終了では確認しない(「確認なしで閉じる」フラグ。両方破損・終了のみの画面では保存できていない回答は無い)

### 4.9 見た目と窓(R20〜R22、UI 設計 2〜4・6章)
- `Theme/Tokens.xaml` に UI 設計 2章の色(Background〜Disabled)と 3章の文字(Yu Gothic UI・Cascadia Mono、無ければ Consolas)を置く。
  View は直接の色を書かず、トークンを参照する
- `Theme/Controls.xaml` に主ボタン・副ボタン・破壊的ボタン・選択ボタン・入力欄・通知の帯・警告の帯のスタイルと状態(通常・フォーカスの輪・押下・無効)。押せる部品は高さ 44 以上
- 窓: 起動時に `SystemParameters.WorkArea` を見て、既定 1000×680・最小 800×560 をそれぞれ作業領域の大きさで頭打ちにする
- 内容は最大幅 760 の中央寄せ。全体を `ScrollViewer`(縦)に入れる。判定・結果の主ボタンは下部に固定して隠れないようにする
- 結果のカード3枚は、内容の幅が 640 未満なら縦に並べる
- 正しいスペルの文字の大きさは、幅に収まるまで 52 → 40 → 32 と段階的に下げる(Viewbox は使わず段階で切り替える)
- 初期フォーカス(R20、再レビュー P1): MainWindow が `ShellViewModel.Current` の変更を受けるたびに、表示の更新の後(`Dispatcher` の Loaded 優先度)で
  その View の初期フォーカスへ移す(4.3 の表)。`Loaded` だけに頼らない(同じ型の画面に替わると View が作り直されず Loaded が来ないため。
  例: 保存失敗 → 再保存の成功で「次へ」へ移す)。確認ダイアログを閉じた後も、今の画面の初期フォーカスへ戻す(やめるで「いいえ」→ 回答欄)。戻すのは `MessageBoxPrompt` で、ダイアログを閉じた後に MainWindow の「初期フォーカスへ戻す」を呼ぶ
- スクリーンリーダー用の名前: 回答欄・正しいスペル・差分の表示は固定の文言(「回答の入力欄」「正しいスペル」「間違いの説明」)を `AutomationProperties.Name` に明示し、
  1文字ずつの要素には名前を付けない(ToString を読ませない)。ボタンは表示している文言をそのまま名前にする
- 確認ダイアログは骨組みでは `MessageBox` を使う(UI 設計 4章の「ダイアログ内だけ Danger 地」は再現しない。見た目の妥協として decisions に記録)
- 作業領域が 760 より狭い場合は、内容の最大幅を作業領域に合わせる(横スクロールは出さない)。別のモニターへの移動・DPI の変更への追従は骨組みでは行わない(限界)

## 5. テスト方針
- 単体(Core.Tests): `SpellingJudge.IsBlank` — `null`・`""`・`"   "`・`"\t"`・`" a "`(偽)
- 単体(Flow.Tests): `HomeScreen.QuestionCountChoices` が設定の選択肢と一致する
- 単体(Presentation.Tests、Linux・Windows):
  - Flow は本物を使う(一時フォルダ + テスト用の小さな words.json)。保存の失敗は既存の `IFileOperations` の差し替えで起こす。
    モックは `IUserPrompt`(決まった答えを返す)、`AppLog`(記録するだけ)、`TimeProvider`(FakeTimeProvider 相当を手書き)だけ
  - **テストの補助は既定で「BeginAccepting を呼び、時計を 0.3 秒進めてから実行」する**(再々レビュー P1。ゲートで止まったせいで、確かめたい仕組みが壊れていても通る空振りを防ぐ)。
    「何もしない」を確かめるテストは、その前に同じ補助で正当な実行が効くこと(画面が変わること)を確かめる
  - 画面の型ごとに正しい ViewModel になる / 出す操作が Flow の受け付ける操作と一致する(Saved の真偽、CanResume、CanReload)
  - 通しの流れ: ホーム → 始める → 回答(正答・誤答)→ 判定 → 次へ → 再練習(印と説明)→ 結果 → ホーム
  - 空欄: `""`・`"   "`・`"\t"` で回答コマンドが無効、実行しても画面が変わらずファイルも書かれない
  - 連打: Home の始める・Question の回答・Feedback の次へ・Result のホームへ — 1回目で画面が変わったことを確かめ、時計を 0.3 秒以上進めてから古い ViewModel のコマンドを実行 → 何もしない・例外なし。古い ViewModel のコマンドは CanExecute が偽
  - 受け付け開始のゲート(画面の ViewModel のコマンドを通して確かめる。偽の TimeProvider): `BeginAccepting` の前は時間に関係なく無視 / 開始から 0.29 秒後に実行 → Flow の画面もファイルも変わらない / 0.30・0.31 秒後は受け付ける。
    **各画面の ViewModel が公開するすべての ICommand を一覧にして**、どれもゲートを通ることを確かめる(新しいコマンドの付け忘れを防ぐ)。再保存で作り直された FeedbackViewModel でも 0.29 秒の「次へ」を無視する。
    「判定で結果を見る → 0.1秒後に結果のホームへ → 無視」「結果のホームへ → 0.1秒後にホームの始める → 無視・progress.json は変わらない」
  - 直前の回答の値: 保存失敗 → 再保存の成功の後も「n / 合計」・正規化した入力が同じで、ToString に入力が出ない。FeedbackScreen 以外へ移ると消える
  - ホーム: 苦手のみ・0語で理由が出たあと、苦手のみが選ばれたまま / 開始の保存に失敗したら警告の帯 / 保存済みの値が選択肢に無くてもその値が出る /
    保存済みの手動モードが音声・フラッシュならその値が選択肢に出て、始めると ManualModeUnavailable の理由が出る
  - 再開の選択: 「終了して新しく始める」の保存に失敗したら Message が出る
  - 閉じる前の確認: `HasUnsavedAnswer` が保存失敗中だけ真 / 「確認なしで閉じる」フラグが立っていれば確認しない
  - 通知: 同じ文の通知が重ならない
  - `DiffDisplay.ToString` に入力が出ない
  - 判定: 差分の文章が Describe と一致 / 4件以上で「ほか n か所」/ `" Recieve"`・`"ACCOMODATE"` で取り消し線・強調の位置が正しい / 最後の通常問題を誤答したとき(再練習が足されて Completed が偽)のボタン名は「次へ」/ 最後の再練習のボタン名は「結果を見る」
  - 判定画面の「n / 合計」が直前の出題の値 / FeedbackViewModel の `ToString` に入力が出ない
  - 保存失敗 → もう一度保存(Enter でも)・次へは無効 → ログに Diagnostic と sessionId が出て、入力・パスが出ない → 再保存の成功で次へが有効
  - やめる: 確認で「いいえ」なら出題のまま、「はい」で再開の選択
  - 通知: 起動時の通知と StartFresh 後の通知が、画面が変わっても残り、閉じると消える
  - 起動して画面を出しただけではファイルが書かれない
  - 両方破損: 確認で「いいえ」なら何も書かない、「はい」で `StartFresh` / 終了で `ExitRequested`
  - `StartupArguments`: 引数なし / `--data-dir 絶対パス` / 相対パス / 値なし / 2回 / 知らない引数
  - `ScreenText`: `SelectionEmptyReason` の `None` 以外の5値すべてに文言がある。0語で始めるとホームに理由の文言が出る
  - `FileLog`: 1行の形、禁止項目を書かない、書けないフォルダで例外を外へ出さない、古い世代の削除の境界(29 / 30 / 31 日)、
    対象外を消さない、ログのフォルダが空文字・相対パスなら何も書かず何も消さない
  - 同梱の words.json が `WordSetLoader.Parse` と `WordSetValidator` でエラー 0
- Wpf: 自動テストは作らない(View とコードビハインドは薄く、合成だけにする)。Linux の CI ではビルドだけ確かめる
- 手動確認(Windows 実機。結果は tasklist.md の確認表に残す): requirements.md 5章の手動の項目と handoff.md 5.7
  (初回起動 → 練習 → 閉じる → 再起動 → 続きから → 結果、ロックでの保存失敗、破損、二重起動、IME、画面ごとの初期フォーカスと Tab の順、
  Enter・Esc(やめる → いいえ → そのまま打てる)・押しっぱなし(出題で Enter を押したまま → 判定にとどまる / Esc を押したまま → 確認は1回だけ)、
  保存中・200% 表示で回答ボタンをダブルクリック → 判定にとどまる、通知を閉じた後のフォーカス、
  保存失敗 → ロックを外して Enter → もう一度 Enter で次の問題へ(フォーカスが「次へ」に移る)、貼り付けた改行が除かれる、
  色だけで伝えていない・スクリーンリーダーで主な部品の名前が読まれる(ナレーター)、保存失敗中に閉じると確認・それ以外は確認なし、
  最小の窓と 125・150・200% 表示、想定外の例外で確認なしに終了)

## 6. リスクと未決事項
- 決定済み(詳細は decisions.md):
  1. ホームの問題数の選択肢は Flow の `HomeScreen` に `QuestionCountChoices` を足して渡す
  2. 起動引数: `--data-dir` の値なし・2回は空文字にして FolderInvalid、知らない引数は無視して warn でログ
  3. 空欄・空白だけの入力は画面から送らない。判定は Core の `SpellingJudge.IsBlank`(旧決定「誤答として記録」は取り消し。Q-UX4・レビュー P0-1)
  4. 仮の単語データの `sources` は実際に入っているリスト名を確かめて付け、確かめられない語は入れない
  5. 例外のログは型名・HResult・発生したメソッド名まで。30 日より古いログは起動時に消す
  6. DECISIONS・architecture.md・CLAUDE.md・testing.md・logging.md の更新はこの PR に含める
  7. 差分の文章は Core の Describe(H2)/ 想定外の例外は終了(H1)/ 窓は作業領域で頭打ち(H3)/ 入力は画面だけ(H4)
- 設計レビュー(第4工程、2026-11-14): `.steering/20261114-design-review/review.md`。P0 2件・P1 12件をこの版で反映。
  再レビュー(4体)の P1(受け付け開始のゲート・直前の回答の値・同じ型の画面での初期フォーカス)も反映
- ログの30日削除(決定 5)は、過剰設計の指摘があったが残す: ユーザーが承認済みで、安全策とテストも設計済み。ログが増え続けると利用者のディスクを使い続けるため
- 次のタスクへ回す(ユーザー決定): 判定画面の「苦手に登録されました」の判定を Core に寄せる(AnswerChange に BecameWeak を足す、または SessionSummary の分類を公開)
- 積み残し(P2。実装中に余裕があれば・次のタスク): `--data-dir` の UNC パスの扱い / 手動モードの選択肢を `HomeScreen` に持たせる(SSoT)/
  使えない手動モードを選択肢で使えないと示す / 単語に空白・大文字がある場合の差分の読みやすさ(正式な単語データのとき)/
  全角英字・ゼロ幅空白の貼り付け / 両方破損の確認の既定ボタンを実機で確かめる /
  `BeginAccepting` は画面ごとに最初の1回だけ数え始める(フォーカスの処理が動く前に画面が替わった場合を含む)と実装で決め、手動確認に「全画面で 0.3 秒後に操作できる」を足す / Flow の `Answer` の XML コメントに「空欄は呼び出し側で弾く(IsBlank)」と書く
- 応答の固まり: Flow は同期で保存し、保存の再試行で待つことがある(UI スレッドが止まる)。連打は 4.3 で防ぐ。実機で気になれば次のタスクで考える
- Linux の CI での WPF のビルド: `EnableWindowsTargeting` でビルドできる見込み。通らなければ止めて報告する(CI の変更は確認を取る)
- 前面化の制限: Windows はほかのプロセスの前面化を制限する。前に出ないことがあれば、タスクバーの点滅で知らせる(実機で確かめる)
