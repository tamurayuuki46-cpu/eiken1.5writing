# design.md — 保存・復旧(progress.json / settings.json)

<!-- 置き場所: .steering/20261113-progress-persistence/design.md -->

## 1. 方針
- 【確定 2026-11-13】1 **新しいプロジェクト `EikenSpelling.Persistence`(net10.0、Windows 非依存)** にファイル操作を置く。WPF は保存先フォルダを渡すだけ。
  一時フォルダを使って本物のファイルでテストでき、Linux(クラウド)と Windows の両方で同じテストが動く。
  architecture.md・database.md を更新する
- 学習記録の型・検査は Core に置く(ファイルに触れない)。JSON 変換は Core の既存の形(`LearningSettingsJson`)にそろえる
- ファイル操作は小さな `IFileOperations` を通す。本番は System.IO、テストでは途中で失敗させる実装に差し替える
- 仕様書(v1.2 4章の図)は直さず、docs/DECISIONS.md に「仕様からの差分」として書く(CLAUDE.md 5章)
- 検討して見送った案:
  - Core に手順、WPF にファイル操作 → 置き換えの安全性を Windows 実機でしか確かめられない
  - 保存用の DTO を別に作る → Core の型と二重になる。計算プロパティに `[JsonIgnore]` を付けて Core の型をそのまま使う

## 2. 変更する場所
| ファイル | 内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Persistence/EikenSpelling.Persistence.csproj | net10.0、Core を参照。NuGet の追加なし。依存は `Wpf → Persistence → Core`(逆向き禁止) | 新規 |
| Persistence/IFileOperations.cs | Exists・Length・ReadAllText・WriteAndFlush(UTF-8 BOM なし、`Flush(true)`)・Replace・Move・Copy(上書きしない)・Delete。`SystemFileOperations` が本番 | 新規 |
| Persistence/AtomicJsonFile.cs | 保存・読込・復旧・新規開始の手順(4章)。読み方(変換と検査)は呼び出し側から渡す | 新規 |
| Persistence/FileResults.cs | `LoadStatus`(Loaded / FirstRun / RecoveredFromBackup / BothCorrupt / NewerVersion / Unavailable)、`SaveResult`(成功 / 失敗と理由) | 新規 |
| Persistence/ProgressRepository.cs | `Load(LearningSettings) → ProgressLoadResult{Status, Document?, Warnings, Reason}`、`Save(ProgressDocument) → SaveResult`、`StartFresh(wordSetVersion, sourceVersion) → SaveResult` | 新規 |
| Persistence/SettingsRepository.cs | `Load() → SettingsLoadResult{Status, Settings(常に値あり), Reason}`、`Save(LearningSettings) → SettingsSaveResult(成功 / 不正(問題の一覧)/ 失敗)` | 新規 |
| Core/Models/ProgressDocument.cs | progress.json の中身: `SchemaVersion`(正本はここだけ)・WordSetVersion・SourceVersion・Progress。セッションは次のタスクで任意項目として足す | 新規 |
| Core/Models/LearningProgress.cs | `SchemaVersion` を外す(ProgressDocument に一本化) | 変更 |
| Core/Models/ModeRecord.cs・WordProgress.cs | 計算プロパティ(Accuracy・NormalMistakes)に `[JsonIgnore]` | 変更 |
| Core/Application/ProgressJson.cs | `ReadSchemaVersion(json)`(全体を変換する前に版だけ読む)、`Parse` / `Serialize`。壊れていれば InvalidDataException。値・要素の null も拒否。読込後に Ordinal を付け直す | 新規 |
| Core/Domain/ProgressValidator.cs | 学習記録の検査(4章)。結果の文言に誤答入力・パスを含めない | 新規 |
| Core/Domain/InputRules.cs | 誤答入力の上限 100 文字(固定ルール)。サロゲートペアの途中で切らない | 新規 |
| Core/Application/AnswerRecorder.cs | 誤答入力の切り詰め。回数は int の上限で止める(マイナスに回り込まない) | 変更 |
| Core/Settings/LearningSettingsJson.cs | コメント「WPF 側の JsonSettingsRepository」を Persistence に | 変更 |
| tests/EikenSpelling.Persistence.Tests/ | xUnit(既存と同じパッケージ・同じ版)。一時フォルダ・失敗させる IFileOperations・固定時刻 | 新規 |
| EikenSpelling.sln・README・docs/DECISIONS.md(仕様からの差分・固定ルール)・.claude/rules/architecture.md(15・18・29行)・database.md(13行・paths) | プロジェクトの追加と決定の反映 | 変更 |

## 3. データ
- progress.json: `{ "schemaVersion": 1, "wordSetVersion": "...", "sourceVersion": "...", "progress": { "words": {...}, "appliedAnswerIds": [...] } }`
  - 日時はオフセット付き ISO 8601 で受け取った値のまま(【確定】3)。読込後に `StringComparer.Ordinal` を付け直す
  - 読み込んだ wordSetVersion が今の単語データと違っても、そのまま読み、次の保存で今の版を書く(単語 ID は固定)
- settings.json: 既存の `LearningSettingsJson` の形のまま
- ファイル(すべて同じフォルダ): `X.json` / `X.bak.json` / `X.json.tmp`(X = progress・settings)
  - 退避名: `<元のファイル名>.corrupt-<yyyyMMdd-HHmmss-fff>.json`、同名があれば `-1`, `-2`…を付ける(上書きしない)。時刻は TimeProvider から。残った tmp は `<X.json.tmp>.orphan-<日時>` で残す
- 読む前にファイルの大きさを確かめ、50MB を超えれば壊れている扱い
- Data Owner: ProgressRepository / SettingsRepository だけがファイルを書く。保存先フォルダは絶対パスだけ受け付け(`Path.GetFullPath`)、無ければ作る
- 呼び出し順: 設定の Load → 学習記録の Load(検査に設定と今日の日付を使う)

## 4. 処理の流れ
### ファイル1つの読み方(内部)
- 無い → Missing / 入出力の例外(ロック・権限)→ 少し待って最大3回再試行し、駄目なら Unavailable / 50MB 超・空・変換失敗・検査で重大 → Corrupt / 版が新しすぎる → Newer / それ以外 → Ok(警告つき)

### 読込(AtomicJsonFile.Load)
| 主ファイル | bak | 結果 | ファイルの変更 |
|---|---|---|---|
| Ok | 何でも | Loaded(警告) | 残った tmp を orphan 名で残す |
| Unavailable | — | Unavailable | なし |
| Newer | — | NewerVersion | なし(bak から復旧しない) |
| Missing | Missing | FirstRun | 残った tmp を orphan 名で残す |
| Missing・Corrupt | Ok | RecoveredFromBackup(「最後の保存以降の記録が失われた可能性」を理由に入れる) | 下の「復旧」 |
| Missing・Corrupt | Unavailable | Unavailable | なし |
| Missing・Corrupt | Newer | NewerVersion | なし |
| Missing・Corrupt | Corrupt | BothCorrupt | なし(tmp も触らない) |
| Corrupt | Missing | BothCorrupt | なし |
- **復旧**(P0 対応): ① bak の中身を tmp に書いて flush → ② 壊れた主ファイルがあれば退避名へ**移動** → ③ tmp を主ファイルへ移動。これで次の保存では正常な主ファイルが bak に回る。途中で失敗したら Unavailable を返す(bak は変えないので、次の起動でまた復旧できる)

### 保存(AtomicJsonFile.Save)
1. 内容を tmp に書き、`FileStream.Flush(flushToDisk: true)` でディスクまで書き出す(読み戻しの検証はキャッシュからなので、ディスクに書けた保証ではない)
2. tmp を読み戻して変換・検査。駄目なら tmp を消して失敗(主・bak は触らない)
3. 主ファイルがあれば `File.Replace(tmp, 主, bak)`、無ければ `tmp → 主` に移動。入出力の例外は最大3回再試行
4. 例外はすべて捕まえて `SaveResult` の失敗と理由(パス・内容を含めない)を返す。Windows の Replace は途中で止まると「主が無く tmp が残る」ことがある → 次の読込で bak から復旧し、tmp は orphan 名で残る(最後の1問分が失われる可能性がある。限界として記録)

### 新規開始(StartFresh)
- BothCorrupt を返した後にだけ受け付ける(それ以外は失敗を返す)
- 壊れた主・bak を退避名へ移動 → すべて成功したときだけ空の記録を保存(Move の経路)

### Repository のロック
- ProgressRepository: Load 前の Save / StartFresh、NewerVersion・Unavailable・BothCorrupt(StartFresh 前)の後の Save は失敗を返す(上書きしない)
- SettingsRepository: NewerVersion・Unavailable の後の Save は失敗を返す(初期値で動かし、理由を知らせる)。BothCorrupt の後の Save は、壊れたファイルを退避名へ移動してから保存する

### 学習記録の検査(ProgressValidator)
- 重大(読込で Corrupt): 辞書のキーと WordId の不一致 / 空の WordId / 単語・モード別記録の値が null / 定義外の状態・モード / 回数・連続正答・段階が負 / 状態ごとの組み合わせの違反(Normal: 連続正答0・対象正答日なし・段階0・予定日なし、Weak: 段階0・予定日なし・連続正答>0 なら対象正答日あり、Mastered: 段階≧1・MasteredAt あり・対象正答日あり)/ 空の SessionId・負の通し番号の answerId
- 警告(読めるが知らせる): 現在より後の日時・今日より後の日付 / Weak の連続正答が今の必要回数以上 / 段階が今の間隔の数より大きい / 誤答入力が上限を超えている
- 重大と警告が両方あるときは重大(Corrupt)

### settings.json の読み方
- 変換失敗 → Corrupt / settingsVersion が新しすぎる → Newer / SettingsValidator(語彙データは渡さない)で問題あり → Corrupt(理由に問題の一覧)/ それ以外 → Ok
- Loaded・RecoveredFromBackup 以外では初期値を返す。初期値で動かすときにファイルは書かない
- Save: SettingsValidator で問題があれば保存せず問題の一覧を返す(語彙データの確認は練習の開始時)

## 5. テスト方針
- 一時フォルダはテストごとに `GetTempPath()/Guid` で作り、`IDisposable` で後始末する(並列でも衝突しない)。権限や読み取り専用属性で失敗させず、失敗は IFileOperations の差し替え(「何回目の呼び出しで失敗」を指定)で起こす。時刻は固定(自前の FixedTimeProvider)。再試行の待ち時間は 0 にする
- Core: ProgressJson の往復(全項目・日時のオフセット +09:00 と Z・Ordinal の付け直し・反映済み answerId)/ 壊れた JSON・空・`null`・型違い・値の null・新しすぎる版・版が無い / 知らない項目は無視 / ProgressValidator の重大・警告それぞれと境界(日本時間 0:00 前後の「今日」と「明日」、連続正答と段階の n-1・n・n+1、通し番号 0 と -1、重大と警告の同時)/ 誤答入力の切り詰め(99・100・101 文字、サロゲートペアが境目、空・null)/ 回数が上限で止まる
- Persistence 保存: 初回(主ができる)/ 2回目(前の内容が bak)/ 読み戻しの検証失敗 / flush・Replace・Move・tmp の削除の各段階で失敗 → 主と bak は前の内容のまま(中身を比べる)/ Replace が「主→bak 済み・tmp→主 で失敗」の途中状態 → 次の Load で RecoveredFromBackup / 古い tmp が残っていても上書きして成功 / UTF-8 BOM なし
- Persistence 読込: 表の各行 / 0バイト・空白だけ・`null`・BOM 付き / 主が正常で bak が壊れている → Loaded で bak 不変 / 主が JSON として正しいが検査で重大 → 復旧 / 警告だけ → Loaded に警告 / ロック(読込の例外)→ 再試行のあと Unavailable、ファイル不変 / 50MB 超 / 復旧 → 保存 → bak が正常な内容(P0)/ 復旧の途中で失敗 → Unavailable で bak 不変
- 新規開始: 退避が同じミリ秒でも両方残る / 退避に失敗したら空の記録を保存しない / BothCorrupt 以外では失敗 / 片方無く片方壊れている状態でも動く / その後の Load は Loaded
- Repository: Load 前の Save は失敗 / NewerVersion・Unavailable・BothCorrupt の後の Save は失敗しファイル不変 / 設定の各結果(Loaded・FirstRun・Recovered・BothCorrupt・NewerVersion・値が不正)/ 不正な設定は保存しない / 設定の BothCorrupt の後の保存で壊れたファイルが退避名で残る / 設定の失敗で progress.json が変わらない / 相対パスは拒否
- 結合・手動確認: WPF ができたら Windows で「保存 → 再起動 → 復元」と、Replace の途中失敗の挙動を確かめる(Linux の File.Replace は rename なので Windows の途中失敗は差し替えでしか再現できない)

## 6. リスクと未決事項
- 【確定 2026-11-13】1 置き場所は新しい Persistence プロジェクト
- 【確定 2026-11-13】2 誤答入力の上限は 100 文字。設定ではなく固定ルール(DECISIONS.md「固定ルール」)。サロゲートペアの途中で切らない。読み込んだ記録で超えていたら警告だけ
- 【確定 2026-11-13】3 日時のオフセットはそろえず、受け取った値のまま保存する
- 【確定 2026-11-13】4 壊れたファイルは消さず退避名で残す。上限は設けない(復旧時は「移動」なので、起動のたびに増えることはない)
- 【確定 2026-11-13】5 新しすぎる schemaVersion / settingsVersion は NewerVersion で知らせ、上書きも復旧もしない。settings は初期値で動かし、設定の保存を拒否する
- 【確定 2026-11-13】6 ②③ レビュー反映(復旧は移動+作り直し / 読めないと壊れているを分ける / 退避名の一意化 / API の形 / schemaVersion の一本化 ほか)
- セッション管理のタスクへ: 古いセッションの answerId を消す条件 / 遅れて届いた回答 / 同じ answerId で中身が違う回答 / DateTimeOffset の範囲
- 限界: Windows の Replace の途中失敗では最後の1問分が失われることがある(orphan の tmp に残る)
- リスク: 1問ごとに progress.json 全体を書き直す。5,000語・answerId の増加でファイルが大きくなる(P2、tasklist に記録)
