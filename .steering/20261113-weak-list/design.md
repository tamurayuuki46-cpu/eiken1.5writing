# design.md — 苦手一覧・学習状況

## 1. 方針
- 一覧と語数は学習記録・単語データ・設定・時刻だけから求まる純粋な計算なので Core の Application に置く(スマホ版でも使う)。
- Flow は画面の状態に `WeakListScreen` を足し、ホームの画面の状態に学習状況を持たせる。今の「受け付けない操作は例外」の仕組みに乗せる。
- 苦手度は `WeakWordScorer`(Order・Score)、再確認待ちは `ReviewScheduler.IsDue`(克服済みだけを見る)、正答率は `ModeRecord.Accuracy` を使い、計算を重ねて書かない。
- 単語データは WordSetValidator を通ったもの(ID の重複は起動で止まる)を前提にし、重複の扱いは書かない(②で過剰と指摘)。
- 検討して見送った案:
  - 一覧のデータを WPF が直接 Core から取る案: 「苦手のみ練習への導線」を Flow でテストできない。ユーザー決定で Flow の画面にする
  - 克服済みにも苦手度を付けて混ぜて並べる案: 苦手度は未克服の出題順に使う値(v1.1 7.4)。克服済みは後ろに再確認日の順で置く

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Core/Application/WeakWordList.cs | 苦手一覧の行(`WeakWordRow`)・`ModeAccuracy`・並べ方(`WeakListSort`)と `Build` | 新規 |
| src/EikenSpelling.Core/Application/StudyStatus.cs | 状態ごとの語数と `From`、`Empty` | 新規 |
| src/EikenSpelling.Core/Domain/JstClock.cs | `TryToJst(DateTimeOffset) → DateTimeOffset?`(変換できない極端な日時は null) | 変更 |
| src/EikenSpelling.Core/Domain/MasteryRules.cs | `CountedToday(WordProgress, DateOnly)`(今日すでに対象正答した) | 変更 |
| src/EikenSpelling.Core/Domain/WeakWordScorer.cs | 上の2つを使うように置き換え(振る舞いは変えない) | 変更 |
| src/EikenSpelling.Flow/FlowScreens.cs | `HomeScreen` に `Status`、`WeakListScreen` を追加 | 変更 |
| src/EikenSpelling.Flow/PracticeFlow.cs | `OpenWeakList` / `ChangeWeakList` / `StartWeakPractice` / `BackToHome`。練習の開始を1つの処理にまとめ、戻る画面だけを変える | 変更 |
| src/EikenSpelling.Flow/FlowViews.cs | ホーム(学習状況つき)・苦手一覧の画面を作る内部の小さなクラス(PracticeFlow を 300 行以内に保つ) | 新規 |
| tests/EikenSpelling.Core.Tests/WeakWordListTests.cs、StudyStatusTests.cs | 単体 | 新規 |
| tests/EikenSpelling.Flow.Tests/FlowWeakListTests.cs、FlowInvalidOperationTests.cs | 結合・受け付けない操作の表に追加 | 新規 / 変更 |
| docs/DECISIONS.md | 「苦手一覧・学習状況」の節、「進行役 Flow」の画面一覧と誤答入力の扱い | 変更 |

## 3. データ
- 扱うデータ: LearningProgress(読むだけ)、WordSet、LearningSettings
- データの持ち主: 変わらず(Core は値を返すだけ。Flow の新しい操作で書くのは StartWeakPractice の開始の保存だけ)
- Schema 変更: なし

## 4. 処理の流れ

### 4.1 `StudyStatus.From(WordSet, LearningProgress, DateOnly today)`
- 単語データの語ごとに、記録が無ければ Unseen、あれば状態(Normal / Weak / Mastered)で数える。Mastered のうち `ReviewScheduler.IsDue` なら ReviewDue にも数える(IsDue は克服済みだけを見るので、苦手に戻った単語の古い予定日は数えない)。
- 単語データに無い記録(データ更新で消えた単語)は数えない(ホームは「今の単語データのうち」の数)。苦手一覧には出るので、一覧の行数とホームの苦手の数は合わないことがある(一覧の行の `InWordSet = false` で分かる)。
- `StudyStatus.Empty`(すべて 0)は単語データを読む前の仮の値。

### 4.2 `WeakWordList.Build(LearningProgress, WordSet, LearningSettings, DateTimeOffset now, WeakListSort sort, bool weakOnly)` → `ImmutableArray<WeakWordRow>`
- 対象: 状態が Weak の記録と、weakOnly でなければ Mastered の記録。単語データに無い単語も出す(綴りは不明なので WordId を出し、`InWordSet = false`)。
- 定義外の sort は `ArgumentOutOfRangeException`。
- `WeakWordRow`:
  - WordId、Word(単語データの綴り。無ければ WordId)、InWordSet
  - State(Weak / Mastered)
  - NormalMistakes(`WordProgress.NormalMistakes`。再練習の誤答は含めない)
  - Accuracy: `ModeAccuracy(Japanese, Audio, Blank, Flash)`(各 `double?`。通常問題の正答率、通常問題で未出題は null)
  - 最後の誤答: モードの記録のうち LastWrongAt(UTC の時点で比べる)が最も新しいもの。再練習の誤答も含む(苦手度の「直近の誤答」と同じ取り方)。同じ時点ならモードの順(日本語 → 音声 → 穴あき → フラッシュ)で先のもの
    - LastWrongInput: そのモードの最後の誤答入力を `InputRules` で 100 文字に切ったもの。無ければ null
    - LastWrongAt: 日本時間に直した値(`JstClock.TryToJst`)。変換できない極端な日時は null(画面で例外にしない)
  - 苦手のとき(Mastered では null):
    - MasteryStreak、RequiredCorrectDays(設定)
    - RemainingForMastery = max(必要回数 − 連続正答, 1)。連続正答が必要回数以上で苦手に残っている記録(必要回数を後から下げた)も、次に別の日に対象正答すれば克服になるため最低1(MasteryEvaluator の判定と一致)
    - CountedToday(`MasteryRules.CountedToday`)
    - WeakScore(`WeakWordScorer.Score`。並びの理由を見せる用)
  - 克服済みのとき(Weak では null。苦手の単語に予定日がある記録は ProgressValidator が重大として弾くが、防御として出さない): NextReviewDate、ReviewDue(IsDue)
  - `ToString` は誤答入力を伏せる(record の自動生成ではログ・バインドエラーに入力が出るため)
- 並べ方:
  - `WeakFirst`(既定、苦手度の順): 苦手は `WeakWordScorer.Order` の並び → 克服済みは NextReviewDate の早い順(null は最後)→ WordId
  - `RecentMistake`(最後の誤答が新しい順): 最後の誤答の時点の新しい順(未来の日時も時点どおり先頭に来る。誤答の無い単語は最後)→ WordId
  - 最後の決め手は WordId(Ordinal)で、結果は記録の辞書の順に依らない

### 4.3 Flow
| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen`(変更) | 既存 + `Status`(StudyStatus。単語データを読んだ後のホームでは必ず今の記録から作る) | StartPractice / OpenWeakList |
| `WeakListScreen`(新規) | Rows、Sort、WeakOnly、直前の開始が0語のときの理由、開始の保存失敗の文言(Diagnostic つき) | ChangeWeakList(sort, weakOnly) / StartWeakPractice(choice) / BackToHome |

- OpenWeakList(): ホームから。既定は WeakFirst・weakOnly = false。何も書かない。
- ChangeWeakList(sort, weakOnly): 作り直す(時刻は今)。前の理由・文言・Diagnostic は消える。何も書かない。定義外の sort は ArgumentOutOfRangeException(状態は変えない)。
- StartWeakPractice(choice): choice の WeakWordsOnly を真にして、ホームの StartPractice と同じ処理で始める(出題の選択 → 開始 → 保存)。
  - 苦手のみの練習でも期限が来た再確認は出る(DECISIONS「出題する単語の選択」)。出題できる苦手も期限が来た再確認も無ければ0語で、WeakListScreen のまま(Sort・WeakOnly を保ち)理由を出す。何も書かない
  - 保存失敗も WeakListScreen のまま(Sort・WeakOnly を保ち)文言と Diagnostic
  - 一覧の苦手が単語データに無い単語だけ・出せるモードが無いときも0語(理由は QuestionSelector が返すもの)
- BackToHome(): ホームへ(学習状況を作り直す)。何も書かない。
- 練習を始めた後は既存の流れ(結果 → GoHome、Quit → 再開の選択)。一覧には戻らない。
- 未完了の練習があるときはホームにならない(既存の約束)ので、苦手一覧からも練習は1つだけ。
- 誤答入力は画面の状態(一覧の行)に表示用のデータとして持つ。文言・Diagnostic には入れない。画面の状態をログに出さない(既存の約束)。

## 5. テスト方針
- 単体(Core):
  - StudyStatus: 記録なし(Unseen = 語数)/ 各状態 / 再確認の予定日の前日・当日・翌日 / 苦手に戻って古い予定日が残る単語は ReviewDue に数えない / 単語データに無い記録は数えない / 空の単語データ・空の記録
  - WeakWordList:
    - 対象: 通常・未出題は出ない、weakOnly で克服済みが消える、単語データに無い単語は ID と InWordSet = false
    - 誤答回数に再練習を含めない(通常の誤答0で再練習の誤答だけある苦手単語 → 0、最後の誤答は再練習のもの)
    - 正答率: 通常問題で未出題のモードは null(再練習だけのモードも null)
    - 最後の誤答: 複数モードで最新を選ぶ / 同じ時点(オフセット違いを含む)はモードの順 / 入力は 100 文字に切る / LastWrongAt が MaxValue でも例外にならず日時は null(MinValue は +9 時間で直せる)
    - 残り回数: 必要回数 3 で連続正答 0/1/2/3/4 → 3/2/1/1/1、必要回数 1 で連続正答 0 → 1。WeakScore は WeakWordScorer.Score と一致
    - CountedToday: now を日本時間 0:00 の前後(14:59:59Z と 15:00:00Z)にして切り替わる
    - 克服済み: 再確認日と ReviewDue(予定日の前日・当日、日本時間 0:00 前後)/ 予定日なし(並びの最後、複数なら WordId 順)/ DateOnly.MaxValue
    - 苦手に戻った単語: 古い NextReviewDate があっても行に再確認日・再確認待ちが出ない
    - 並び: 苦手は WeakWordScorer.Order と同じ並び、その後ろに克服済みが再確認日順 / RecentMistake は新しい順・未来の日時が先頭・誤答なしは最後・同点は WordId / 記録の辞書の順を変えても同じ / 定義外の sort は例外
    - ToString に誤答入力が出ない
    - 性能: 5,000語の記録で一覧と語数を作る時間を出力する(目安 100ms)。アサーションは CI で不安定にならない緩い上限(1秒)にする
  - JstClock.TryToJst・MasteryRules.CountedToday: 境界と極端値。WeakWordScorer の既存テストがすべて通る(置き換えで振る舞いが変わらない)
- 結合(Flow):
  - ホームの学習状況が入る全経路(起動 / StartFresh 後 / Discard 後 / StartPractice の0語 / 開始の保存失敗 / GoHome / BackToHome)。値は件数をそのまま比べる
  - ホーム → 一覧 → 苦手のみ練習 → 結果 → ホームで、苦手・克服済み(前日までに連続正答を積んで今日克服)の件数が期待どおり
  - 一覧の並べ替え・絞り込み(並べ替え後に保存失敗・0語になっても Sort・WeakOnly が保たれ、ChangeWeakList で理由が消える)
  - 見るだけの操作(OpenWeakList・ChangeWeakList・BackToHome・0語の開始)の前後で、フォルダのファイル一覧とバイト列が同じ(tmp・bak・corrupt もできない)
  - 0語: 苦手も期限の再確認も無い → 理由つきで一覧のまま / 苦手は無いが期限の再確認がある → 始まる / 苦手が単語データに無い単語だけ → 0語 / 使えるモードがフラッシュだけ → 0語
  - 一覧からの開始の保存失敗 → 一覧のまま文言と Diagnostic、再試行で始まる
  - 一覧から始めた練習: choice の WeakWordsOnly が偽でも苦手のみになる / Quit → 再開の選択 → Discard → ホーム(一覧には戻らない)/ 練習中に作り直す → 再開の選択
  - 日本時間 0:00 の前後でホームの ReviewDue が切り替わる
  - 受け付けない操作の表に WeakListScreen と新しい操作を足す(一覧で OpenWeakList・StartPractice・Answer、ホームで ChangeWeakList・StartWeakPractice・BackToHome、ResumePrompt・Question・Fatal・BothCorrupt で OpenWeakList・ChangeWeakList など)。StartWeakPractice(null) と ChangeWeakList(定義外) は引数の例外で状態が変わらない

## 6. リスクと未決事項
- 【要確認 1・案で確定】克服済みの単語は苦手の後ろに、次回の再確認日の早い順
- 【要確認 2・案で確定】単語データに無い単語も一覧に出す(綴りの代わりに ID、InWordSet = false)
- 【要確認 3・案で確定】一覧から始める苦手のみ練習は PracticeChoice を受け取り、苦手のみだけ真にする
- 誤答入力の改行・制御文字は Core では置き換えない(表示の仕方は WPF のタスクで決める)
- リスク: ホームに戻るたびに 5,000語を数える。単純な数え上げなので軽い想定。性能テストで時間を出す
