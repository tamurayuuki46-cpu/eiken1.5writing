# design.md — 練習設定の保存

## 1. 方針
- 基本設定の値の型は既存の `PracticeChoice`(AutoMix・ManualMode・QuestionCount・WeakWordsOnly・FlashSeconds)をそのまま使う(v1.2 2章 A の5項目と一致。同じ情報を2つの型に書かない)。
- 今の設定に基本設定を重ねるのは既存の `PracticeChoiceApplier`、検査は `SettingsValidator`、保存は `SettingsRepository.Save`(新しい版・読めないときは拒否、壊れていたら退避してから保存)に任せる。Flow は順番と画面だけを持つ。
- 検討して見送った案:
  - 練習の開始時に自動で記憶する案: ユーザー決定で設定画面で保存したときだけ
  - 「初期値に戻す」を即保存にする案: 誤操作で消えるのを避け、画面の値を戻すだけにして保存で確定する
  - 基本設定用の新しい型を作る案: PracticeChoice と同じ5項目になり重複する

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Flow/FlowScreens.cs | `SettingsScreen` を追加。`HomeScreen`・`WeakListScreen` に `Defaults`(PracticeChoice) | 変更 |
| src/EikenSpelling.Flow/FlowStartup.cs | SettingsRepository と読込の状態を StartupResult に入れる | 変更 |
| src/EikenSpelling.Flow/PracticeChoiceApplier.cs | 逆向き `FromSettings(LearningSettings) → PracticeChoice` を追加(初期値・画面の値に使う) | 変更 |
| src/EikenSpelling.Flow/PracticeFlow.cs | 設定の操作は別ファイルに分ける(300 行を超えないため)。ホーム・一覧を作る所で Defaults を入れる | 変更 |
| src/EikenSpelling.Flow/SettingsFlow.cs | 設定画面の操作(開く・保存・初期値に戻す・閉じる)。PracticeFlow の partial | 新規 |
| tests/EikenSpelling.Flow.Tests/FlowSettingsTests.cs、FlowInvalidOperationTests.cs | 結合・受け付けない操作の表 | 新規 / 変更 |
| docs/DECISIONS.md | 「進行役 Flow」の「練習ごとの選択は書かない」に設定画面の保存を追記、画面一覧に設定 | 変更 |

## 3. データ
- 扱うデータ: LearningSettings(settings.json)。学習記録には触れない
- データの持ち主: SettingsRepository(変わらず)。Flow はメモリ上の今の設定 `_settings` を1つ持ち、保存に成功したときだけ置き換える
- Schema 変更: なし

## 4. 処理の流れ

### 4.1 画面の状態
| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen`(変更) | 既存 + `Defaults`(今の設定の基本設定) | StartPractice / OpenWeakList / OpenSettings |
| `WeakListScreen`(変更) | 既存 + `Defaults` | 既存 |
| `SettingsScreen`(新規) | Values(画面の値。PracticeChoice)、QuestionCountChoices・FlashSecondsChoices(今の設定から)、ManualModes(使えるモード)、CanSave、CannotSaveReason、Message、Issues(検査の項目と文言)、Diagnostic | SaveSettings(values)(CanSave のときだけ)/ ResetSettings / CloseSettings |

### 4.2 起動
- 設定の読込結果(Status)を Flow が覚える。NewerVersion・Unavailable → CanSave = false(理由「新しい版のアプリで作られた設定のため保存できません」「設定ファイルを読めないため保存できません」)。それ以外(Loaded・FirstRun・RecoveredFromBackup・BothCorrupt)は保存できる。
- Reload(学習記録が読めなかったとき)では SettingsRepository も作り直す(既存どおり)。

### 4.3 操作
- OpenSettings(): ホームから。Values = FromSettings(今の設定)。何も書かない。
- SaveSettings(values):
  1. 新しい設定 = Apply(今の設定, values)
  2. `SettingsRepository.Save(新しい設定)`
     - Saved → `_settings` を置き換え、ホーム(Message「設定を保存しました。」、Defaults は新しい値)
     - Invalid → 設定画面のまま Values = values、Issues = 検査の結果、Message「設定に正しくない値があります。」。`_settings` は変えない
     - Failed → 設定画面のまま Values = values、Message「設定を保存できませんでした。」、Diagnostic = Reason。`_settings` は変えない
  - 保存できない(CanSave = false)ときは受け付けない(InvalidOperationException。状態・ファイルは変えない)
- ResetSettings(): Values = FromSettings(DefaultSettings.Create())。Issues・Message・Diagnostic は消す。何も書かない。
- CloseSettings(): ホームへ(Defaults は今の設定。保存しなかった値は捨てる)。何も書かない。
- 未完了の練習があるときはホームにならない(既存)ので、設定の保存は練習中には起きない。

### 4.4 練習の初期値
- `Defaults` = FromSettings(今の設定)。WPF の練習設定画面はこの値を最初に選んでおく。StartPractice は渡された choice を使う(変わらない)。

## 5. テスト方針
- 単体: FromSettings と Apply の往復で5項目が一致し、他の設定は変わらない
- 結合(Flow、一時フォルダの本物のファイル):
  - 初回のホームの Defaults が初期値 / 開く → 値を変えて保存 → ホームの Defaults と settings.json の値が変わる / 作り直すと読まれる
  - 詳細設定(retryCount を書いた settings.json)が保存の後も残る。知らない項目は Serializer の扱いどおり(今は消える。既存の限界として記録)
  - 不正な値(問題数 0・101、フラッシュ 0.05・11 秒、定義外のモード)→ Issues と Message、settings.json は変わらない、今の設定も変わらない(閉じた後の Defaults が元のまま)
  - 書き込み失敗 → 設定画面に留まり Values = 入力した値、Message・Diagnostic、ファイル不変。失敗を外して保存で成功
  - 新しい版・読めない設定 → CanSave = false と理由、SaveSettings は例外でファイル不変、ResetSettings・CloseSettings はできる
  - 壊れた設定 → 保存で成功し、壊れたファイルが退避名で残る。次の起動で通知が出ない
  - 初期値に戻す → Values が初期値、Issues・Message が消える、何も書かない。保存で初期値が書かれる
  - 開く・初期値に戻す・閉じるの前後でフォルダのファイル一覧とバイト列が同じ
  - 保存した設定でホームの Defaults と、その値で始めた練習のセッションの設定が一致
  - 受け付けない操作の表に SettingsScreen と新しい操作を足す。SaveSettings(null) は ArgumentNullException で状態不変
  - 学習記録(progress.json)は設定の操作の前後でバイト列が同じ

## 6. リスクと未決事項
- 【要確認 1・案で確定】保存に成功した後の行き先: 案は「ホームへ戻り『設定を保存しました』を出す」
- 【要確認 2・案で確定】手動モードの選択肢: 案は「使えるモード(音声の無い端末では音声を除く)だけを出す。ただし保存済みの値が使えないモードでも、そのまま表示・保存はできる(端末が変わることがあるため)」
- 限界: 知らない項目(新しい版で足された項目)は保存で消える(LearningSettingsJson の既存の扱い。新しい版のファイルは保存を拒否するので、同じ版の範囲では起きない)
