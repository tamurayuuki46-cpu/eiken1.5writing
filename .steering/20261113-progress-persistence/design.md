# design.md — 保存・復旧(progress.json / settings.json)

<!-- 置き場所: .steering/20261113-progress-persistence/design.md -->

## 1. 方針
- 【要確認】1 **新しいプロジェクト `EikenSpelling.Persistence`(net10.0、Windows 非依存)** にファイル操作を置く。WPF は保存先フォルダを渡すだけ。
  一時フォルダを使って本物のファイルでテストでき、Linux(クラウド)と Windows の両方で同じテストが動く。
  v1.2 4章の図(`EikenSpelling.Wpf/Persistence`)と architecture.md「ファイル保存は Wpf」を更新する
- 学習記録の型・検査は Core に置く(ファイルに触れない)。JSON 変換は Core の既存の形(`LearningSettingsJson`)にそろえる
- ファイル操作は小さな `IFileOperations` を通す。本番は System.IO、テストでは途中で失敗させる実装に差し替える
- 検討して見送った案:
  - Core に手順、WPF にファイル操作 → 置き換えの安全性を Windows 実機でしか確かめられない
  - 保存用の DTO を別に作る → Core の型と二重になる。計算プロパティに `[JsonIgnore]` を付けて Core の型をそのまま使う

## 2. 変更する場所
| ファイル | 内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Persistence/EikenSpelling.Persistence.csproj | net10.0、Core を参照。NuGet の追加なし | 新規 |
| Persistence/AtomicJsonFile.cs | 安全な保存と、主ファイル・バックアップの読込(2章の手順) | 新規 |
| Persistence/IFileOperations.cs | 書き込み・置き換え・移動・コピー・読込の最小限の操作。`SystemFileOperations` が本番 | 新規 |
| Persistence/ProgressRepository.cs | progress.json の Load / Save / StartFresh | 新規 |
| Persistence/SettingsRepository.cs | settings.json の Load / Save | 新規 |
| Persistence/LoadResult.cs | 読込の結果: `Loaded` / `FirstRun` / `RecoveredFromBackup` / `BothCorrupt`(+ 理由) | 新規 |
| Core/Models/ProgressDocument.cs | progress.json の中身: schemaVersion・wordSetVersion・sourceVersion・progress。セッションは次のタスクで任意項目として足す | 新規 |
| Core/Application/ProgressJson.cs | ProgressDocument と文字列の変換(壊れていれば InvalidDataException) | 新規 |
| Core/Domain/ProgressValidator.cs | 学習記録の検査(4章) | 新規 |
| Core/Models/ModeRecord.cs・WordProgress.cs | 計算プロパティ(Accuracy・NormalMistakes)に `[JsonIgnore]` | 変更 |
| Core/Application/AnswerRecorder.cs・Settings/SettingsLimits.cs | 誤答入力を `MaxInputLength`(【要確認】2: 100文字)で切り詰める | 変更 |
| tests/EikenSpelling.Persistence.Tests/ | xUnit(既存のテストプロジェクトと同じパッケージ・同じ版) | 新規 |
| EikenSpelling.sln・README・docs/DECISIONS.md・.claude/rules/architecture.md・database.md | プロジェクトの追加と決定の反映 | 変更 |

## 3. データ
- progress.json: `{ "schemaVersion": 1, "wordSetVersion": "...", "sourceVersion": "...", "progress": { "words": {...}, "appliedAnswerIds": [...] } }`
  - 日時はオフセット付きの ISO 8601 で、受け取った値のまま保存(【要確認】3: オフセットはそろえない。比較は瞬間で行われるため)
  - 読込後に辞書の比較方法(`StringComparer.Ordinal`)を付け直す
- settings.json: 既存の `LearningSettingsJson` の形のまま
- ファイル: `progress.json` / `progress.bak.json` / `progress.json.tmp`、`settings.json` / `settings.bak.json` / `settings.json.tmp`。
  壊れたファイルの退避名は `progress.corrupt-<日時>.json`(【要確認】4)
- Data Owner: ProgressRepository / SettingsRepository だけがファイルを書く
- Schema: 新規(progress.json の初版)。バージョンが新しすぎるファイルは読まずに「壊れている」と同じ扱いにはせず、別の結果(`NewerVersion`)で知らせて上書きしない

## 4. 処理の流れ
### 保存(AtomicJsonFile.Save)
1. 内容を `*.tmp` に書き、ディスクへ確実に書き出す(flush)
2. `*.tmp` を読み戻して変換・検査し、駄目なら `*.tmp` を消して失敗を返す(主ファイルは触らない)
3. 主ファイルがあれば `File.Replace(tmp, 主, bak)` で「主 → bak、tmp → 主」を一度に行う。無ければ `tmp → 主` に移動
4. 例外はすべて捕まえて「失敗(理由)」を返す。主ファイルと bak はそのまま。呼び出し側は元の学習記録を持っているので再保存できる

### 読込(AtomicJsonFile.Load)
1. 残っている `*.tmp` は使わない(前回の保存が途中で止まったもの。消す)
2. 主ファイルが読めて検査を通る → `Loaded`
3. 主ファイルが無い・壊れている → bak が読めれば `RecoveredFromBackup`。壊れた主ファイルは退避名にコピーして残す。
   次の保存で主ファイルが作り直される
4. 両方とも無い → `FirstRun`(空の学習記録)
5. 両方壊れている(片方が無く、もう片方が壊れている場合も)→ `BothCorrupt`。何も書き換えない
6. `StartFresh()`: `BothCorrupt` のあと利用者が確認したときだけ呼ぶ。壊れた主・bak を退避名にコピーしてから、空の記録を保存する

### 学習記録の検査(ProgressValidator)
- 重大(読込失敗と同じ扱い): 辞書のキーと WordId の不一致 / 空の WordId / 定義外の状態・モード / Modes の値が null / 回数・連続正答・段階が負 / 状態ごとの組み合わせの違反(Normal・Weak なのに予定日あり、Mastered なのに MasteredAt なし など。前タスク design.md 2章の表)/ 空の SessionId・負の通し番号の answerId
- 警告(読めるが知らせる): 今日より後の日時・日付(時計のずれ)/ Weak の連続正答が今の必要回数以上 / 段階が今の間隔の数より大きい(設定変更で起きうる)

### settings.json
- Save: `SettingsValidator` で問題があれば保存せず、問題の一覧を返す。問題が無ければ AtomicJsonFile.Save
- Load: AtomicJsonFile.Load と同じ。`BothCorrupt` と `FirstRun` のときは初期値を返し、理由を結果に入れる(設定は初期値で動かしてよい。学習記録とは別)。
  読めても検証で問題があれば初期値を使い、理由を返す(v1.2 7章「不正な設定値は使わない」)

## 5. テスト方針
- Core(既存のテストプロジェクト): ProgressJson の往復(全項目・Ordinal の付け直し)/ 壊れた JSON・null・新しすぎる版 / 知らない項目は無視 / ProgressValidator の重大・警告それぞれ / 誤答入力の切り詰め(上限 n-1・n・n+1)
- Persistence(新しいテストプロジェクト、一時フォルダを使い後始末する):
  - 保存: 初回 / 2回目で bak ができる / 読み戻しの検証に失敗したら主ファイル不変 / tmp の書き込み・置き換えの各段階で失敗させても主と bak が前の内容
  - 読込: 正常 / 初回 / 主が壊れている・主が無い → bak から復旧し退避ファイルができる / 両方壊れている・片方無く片方壊れている → 何も変えない / 残った tmp は使わない / 新しすぎる版は上書きしない
  - StartFresh: 壊れたファイルが退避名で残り、空の記録が保存される
  - 設定: 不正な設定は保存しない / 壊れた settings.json で初期値と理由 / 設定の失敗で progress.json が変わらない
- 結合・手動確認: WPF ができたら Windows で「保存 → 再起動 → 復元」を確かめる(このタスクの外)

## 6. リスクと未決事項
- 【要確認】1 置き場所は新しい Persistence プロジェクト(ユーザーの希望は「指定なし」だったので推奨案で設計)
- 【要確認】2 誤答入力の上限は 100 文字(超えた分は切り捨て)
- 【要確認】3 日時のオフセットはそろえず、受け取った値のまま保存する
- 【要確認】4 壊れたファイルは消さず `progress.corrupt-<yyyyMMdd-HHmmss>.json` の名前でコピーして残す。いくつまで残すかの上限は設けない
- 【要確認】5 新しすぎる schemaVersion のファイルは「壊れている」とは別の結果で知らせ、上書きも復旧もしない(新しい版のアプリで作った記録を守る)
- リスク: `File.Replace` は同じドライブ上でしか使えない。保存先・bak・tmp は同じフォルダに置くので問題ない
- リスク: 新しいテストプロジェクトのパッケージは既存と同じ版を使うので、新しい依存は増えないが、プロジェクトは1つ増える
