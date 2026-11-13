# design.md — 練習設定の保存

## 1. 方針
- 基本設定の値の型は既存の `PracticeChoice`(AutoMix・ManualMode・QuestionCount・WeakWordsOnly・FlashSeconds)をそのまま使う(v1.2 2章 A の5項目と一致。同じ情報を2つの型に書かない)。
- 今の設定に基本設定を重ねるのは既存の `PracticeChoiceApplier`、検査は `SettingsValidator`、保存は `SettingsRepository.Save`(新しい版・読めないときは拒否、壊れていたら退避してから保存)に任せる。
- 「保存できるか」の正本は SettingsRepository(`CanSave`)。Flow は同じ判定を書かず、それを見て画面に出す(②で SSoT の指摘)。
- 設定の編集は小さな内部クラス `SettingsEditor`(SettingsRepository・今の設定・保存の判断)に分け、PracticeFlow は画面を切り替えて呼ぶだけにする(PracticeFlow.cs を 300 行以内に保ち、責務を分ける)。
- 検討して見送った案:
  - 練習の開始時に自動で記憶する案: ユーザー決定で設定画面で保存したときだけ
  - 「初期値に戻す」を即保存にする案: 誤操作で消えるのを避け、画面の値を戻すだけにして保存で確定する
  - 基本設定用の新しい型を作る案: PracticeChoice と同じ5項目になり重複する
  - 選択肢(QuestionCountChoices・FlashSecondsChoices)に無い値を拒否する案: 練習の開始(StartPractice)も範囲内なら受け付けており、揃える(③ 2・② 4)

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Persistence/SettingsRepository.cs | `CanSave`(読込の結果が NewerVersion・Unavailable・未読込でない)と `Status` を公開。Save の拒否もこれを使う | 変更 |
| src/EikenSpelling.Flow/FlowScreens.cs | `SettingsScreen` を追加。`HomeScreen`・`WeakListScreen` に `Defaults`(PracticeChoice、null にならない) | 変更 |
| src/EikenSpelling.Flow/FlowStartup.cs | SettingsRepository を StartupResult に入れる(起動・Reload で repository・設定・CanSave を1か所で入れ替える) | 変更 |
| src/EikenSpelling.Flow/PracticeChoiceApplier.cs | 逆向き `FromSettings(LearningSettings) → PracticeChoice` | 変更 |
| src/EikenSpelling.Flow/SettingsEditor.cs | 今の設定・保存・画面の値の作り方(内部クラス) | 新規 |
| src/EikenSpelling.Flow/PracticeFlow.cs | OpenSettings / SaveSettings / ResetSettings / CloseSettings を SettingsEditor に委ねる。ホーム・一覧に Defaults | 変更 |
| tests/EikenSpelling.Persistence.Tests/RepositoryTests.cs | CanSave の状態ごとのテスト | 変更 |
| tests/EikenSpelling.Flow.Tests/FlowSettingsTests.cs、FlowInvalidOperationTests.cs | 結合・受け付けない操作の表 | 新規 / 変更 |
| docs/DECISIONS.md | 「学習設定」に保存の扱い・知らない項目の限界、「進行役 Flow」の画面一覧と練習ごとの選択の文 | 変更 |

## 3. データ
- 扱うデータ: LearningSettings(settings.json)。学習記録には触れない
- データの持ち主: SettingsRepository(変わらず)。SettingsEditor はメモリ上の今の設定を1つ持ち、保存に成功したときだけ置き換える
- Schema 変更: なし
- 保存で失われるもの: 詳細設定は失われない(今の設定に基本設定だけを重ねる)。**知らない項目**(新しい版で足された項目・手で書いた綴り違いの項目)は読込で無視しているので、保存で消える(限界。新しい版のファイルは保存自体を拒否する)
- 壊れた設定ファイル(初期値で動いている)の上で保存すると、詳細設定は初期値になる(元のファイルは退避名で残る)。画面に注意書きを出す

## 4. 処理の流れ

### 4.1 画面の状態
| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen`(変更) | 既存 + `Defaults` | StartPractice / OpenWeakList / OpenSettings |
| `WeakListScreen`(変更) | 既存 + `Defaults` | 既存 |
| `SettingsScreen`(新規) | Values(画面の値)、QuestionCountChoices・FlashSecondsChoices(今の設定から)、ManualModes(端末で使えるモード)、CanSave・CannotSaveReason、Note(壊れた設定の上で保存する注意)、Message、Issues、Diagnostic | SaveSettings(values)(CanSave のときだけ)/ ResetSettings / CloseSettings |

- Values は Choices に無い値・ManualModes に無いモードのこともある(手で書いた設定・端末が変わった)。WPF はその値も表示する。

### 4.2 起動・Reload
- StartupResult に SettingsRepository を入れ、PracticeFlow は起動・Reload のたびに repository・今の設定を一緒に入れ替える(SettingsEditor を作り直す)。
- CanSave = `SettingsRepository.CanSave`。偽のときの理由:
  - NewerVersion: 「新しい版のアプリで作られた設定のため、保存できません。画面の値は初期設定です。」
  - Unavailable: 「設定ファイルを読めないため、保存できません。画面の値は初期設定です。アプリを再起動すると保存できることがあります。」
- Note(保存はできる): 読込が BothCorrupt のとき「設定ファイルが壊れているため、保存すると詳細設定は初期値になります(元のファイルは残します)。」

### 4.3 操作
- OpenSettings(): ホームから。Values = FromSettings(今の設定)。何も書かない。
- SaveSettings(values)(null は ArgumentNullException、CanSave でなければ InvalidOperationException。どちらも状態・ファイルは変えない):
  1. 新しい設定 = Apply(今の設定, values)
  2. `SettingsRepository.Save(新しい設定)`
     - Saved → 今の設定を置き換え、ホーム(Message「設定を保存しました。」、Defaults は新しい値)。Note は消える(以後は正常なファイル)
     - Invalid → 設定画面のまま Values = values、Issues = 検査の結果、Message「設定に正しくない値があります。」、Diagnostic = Reason。今の設定は変えない
       - 基本設定の画面で直せない項目(例: 自動ミックスにしたが詳細設定のモードの重みがすべて 0 → `practice.modeWeights`)も Issues に項目名つきで出る(限界。詳細設定の画面は将来)
     - Failed → 設定画面のまま Values = values、Message「設定を保存できませんでした。もう一度保存するか、保存せずに閉じてください。」、Diagnostic = Reason、Issues は空。今の設定は変えない
  - 前の Issues・Message・Diagnostic は毎回置き換える(Invalid → Failed で Issues が消える、Failed → Invalid で前の Diagnostic が消える)
- ResetSettings(): Values = FromSettings(DefaultSettings.Create())(基本設定の5項目だけ。詳細設定は戻さない)。Issues・Message・Diagnostic は消す。何も書かない。
- CloseSettings(): ホームへ(Defaults は今の設定。保存しなかった値は捨てる)。何も書かない。
- 手動モード: 端末で使えないモード(保存済みの値)でもそのまま保存できる(要確認 2)。その値で練習を始めると QuestionSelector が ManualModeUnavailable を返す(既存)。
- 未完了の練習があるときはホームにならない(既存)ので、設定の保存は練習中には起きない。

### 4.4 練習の初期値
- `Defaults` = FromSettings(今の設定)。ホーム・一覧の画面を作るたびに入れる(起動時の仮のホームでも初期値で、null にならない)。WPF の練習設定画面はこの値を最初に選んでおく。StartPractice は渡された choice を使う(変わらない)。

## 5. テスト方針
- 単体
  - FromSettings と Apply の往復で5項目が一致し、他の設定(retryCount など)は変わらない
  - SettingsRepository.CanSave: 読込前 / Loaded / FirstRun / RecoveredFromBackup / BothCorrupt は真、NewerVersion / Unavailable は偽。保存に成功すると真のまま(BothCorrupt → Loaded)
- 結合(Flow、一時フォルダの本物のファイル)
  - 初回のホームの Defaults が初期値 / 開く → 値を変えて保存 → ホームの Defaults と settings.json の値が変わる / 作り直すと読まれる / その Defaults で始めた練習のセッションの設定が一致
  - 境界: 問題数 1・100 は保存でき再起動後も同じ、0・101 は Issues(practice.questionCount)/ フラッシュ 0.1・10 は保存でき 0.1 は厳密に往復、0.09・10.01・NaN・+∞・負は Issues(practice.flashSeconds)/ 定義外の手動モード(99)は自動ミックスでも Issues
  - 選択肢に無い値(問題数 7、0.75 秒)は保存でき、次に開くと Values にそのまま出る
  - 詳細設定(retryCount を書いた settings.json)が保存の後も残る / 自動ミックスにすると重みすべて 0 の詳細設定で Issues(practice.modeWeights)、ファイル不変
  - 不正な値・書き込み失敗では settings.json と今の設定が変わらない(閉じた後の Defaults が元のまま、もう一度開くと Values は今の設定)
  - 書き込み失敗 → 設定画面に Values = 入力した値、Message・Diagnostic。失敗を外して保存で成功
  - 失敗の後の組み合わせ: Failed → Reset(文言が消える)/ Invalid → Failed(Issues が消える)/ Failed → Invalid(前の Diagnostic が消える)/ Invalid → Saved
  - 読込の状態ごと: RecoveredFromBackup で開くと bak の値、保存でき、作り直すと通知が出ない / settings.json だけ読めない(学習記録は読める)→ ホームまで進み CanSave = false、失敗を外して作り直すと真 / 新しい版 → CanSave = false、SaveSettings は例外でファイル不変、Reset・Close はできる / 主が壊れて bak が新しい版 → CanSave = false
  - 壊れた設定 → Note が出る、保存で成功し壊れたファイルが退避名で1つ残る(中身は元のバイト列)。退避の後に書き込みだけ失敗 → もう一度保存で成功し、退避ファイルは増えない
  - Reload: 設定・学習記録とも読めない → Reload で両方読める → 開くと CanSave = true、Defaults はファイルの値
  - 学習記録が両方壊れて StartFresh した後のホームにも Defaults があり、設定を開ける
  - 苦手一覧: 保存後に開くと Defaults が新しい値、ChangeWeakList の後も残る。Defaults が苦手のみ偽でも一覧から始めた練習は苦手のみ
  - 手動=音声を保存した設定で音声の無い端末: ManualModes に音声が無い、Values は音声のまま出て保存もできる、その Defaults で StartPractice すると ManualModeUnavailable
  - 開く・初期値に戻す・閉じるの前後でフォルダのファイル一覧とバイト列が同じ。progress.json は設定の操作の前後で同じ
  - 文言・CannotSaveReason・Note・Diagnostic・Issues に一時フォルダのパスが入らない
  - 受け付けない操作の表に SettingsScreen と新しい操作を足す(OpenSettings は ホーム以外で、Save・Reset・Close は設定画面以外で例外。CanSave = false の SaveSettings も例外)。SaveSettings(null) は ArgumentNullException で状態不変

## 6. リスクと未決事項
- 【要確認 1・案で確定】保存に成功したらホームへ戻り「設定を保存しました」を出す
- 【要確認 2・案で確定】手動モードの選択肢は使えるモードだけ。保存済みの値が使えないモードでも、そのまま表示・保存はできる
- 限界: 知らない項目は保存で消える / 基本設定の画面で直せない詳細設定の Issues がある / Unavailable の設定は再起動するまで保存できない
