# design.md — 苦手一覧・学習状況

## 1. 方針
- 一覧と語数は学習記録・単語データ・設定・時刻だけから求まる純粋な計算なので Core の Application に置く(スマホ版でも使う)。
- Flow は画面の状態に `WeakListScreen` を足し、ホームの画面の状態に学習状況を持たせる。今の「受け付けない操作は例外」の仕組みに乗せる。
- 苦手度は `WeakWordScorer`(Order・Score)、再確認待ちは `ReviewScheduler.IsDue`、正答率は `ModeRecord.Accuracy` を使い、計算を重ねて書かない。
- 検討して見送った案:
  - 一覧のデータを WPF が直接 Core から取る案: 「苦手のみ練習への導線」を Flow でテストできない。ユーザー決定で Flow の画面にする
  - 克服済みにも苦手度を付けて混ぜて並べる案: 苦手度は未克服の出題順に使う値(v1.1 7.4)。克服済みは後ろに再確認日の順で置く

## 2. 変更する場所
| ファイル / モジュール | 変更内容 | 新規 / 変更 |
|---|---|---|
| src/EikenSpelling.Core/Application/WeakWordList.cs | 苦手一覧の行(`WeakWordRow`)と並べ方(`WeakListSort`)、`Build` | 新規 |
| src/EikenSpelling.Core/Application/StudyStatus.cs | 状態ごとの語数と `From` | 新規 |
| src/EikenSpelling.Flow/FlowScreens.cs | `HomeScreen` に `Status`、`WeakListScreen` を追加 | 変更 |
| src/EikenSpelling.Flow/PracticeFlow.cs | `OpenWeakList` / `ChangeWeakList` / `StartWeakPractice` / `BackToHome`。ホームを作る所で学習状況を入れる | 変更 |
| tests/EikenSpelling.Core.Tests/WeakWordListTests.cs、StudyStatusTests.cs | 単体 | 新規 |
| tests/EikenSpelling.Flow.Tests/FlowWeakListTests.cs、FlowInvalidOperationTests.cs | 結合・受け付けない操作の表に追加 | 新規 / 変更 |

## 3. データ
- 扱うデータ: LearningProgress(読むだけ)、WordSet、LearningSettings
- データの持ち主: 変わらず(Core は値を返すだけ。Flow は保存しない操作だけを足す)
- Schema 変更: なし

## 4. 処理の流れ

### 4.1 `StudyStatus.From(WordSet, LearningProgress, DateOnly today)`
- 単語データの語(ID の重複は1件)ごとに、記録が無ければ Unseen、あれば状態(Normal / Weak / Mastered)で数える。Mastered のうち `ReviewScheduler.IsDue` なら ReviewDue にも数える。
- 単語データに無い記録(データ更新で消えた単語)は数えない(ホームは「今の単語データのうち」の数)。

### 4.2 `WeakWordList.Build(LearningProgress, WordSet, LearningSettings, DateTimeOffset now, WeakListSort sort, bool weakOnly)` → `ImmutableArray<WeakWordRow>`
- 対象: 状態が Weak の記録と、weakOnly でなければ Mastered の記録。単語データに無い単語も出す(綴りは不明なので WordId を出し、`InWordSet = false`)。
- `WeakWordRow`:
  - WordId、Word(単語データの綴り。無ければ WordId)、InWordSet
  - State(Weak / Mastered)
  - NormalMistakes(`WordProgress.NormalMistakes`。再練習の誤答は含めない)
  - Accuracy: `ModeAccuracy(Japanese, Audio, Blank, Flash)`(各 `double?`。通常問題の正答率、未出題は null)
  - LastWrongInput・LastWrongAt: モードの記録のうち LastWrongAt が最も新しいもの(再練習の誤答も含む。苦手度の「直近の誤答」と同じ取り方)。無ければ null
  - 苦手のとき: MasteryStreak、RequiredCorrectDays(設定)、RemainingForMastery = max(必要回数 − 連続正答, 0)、CountedToday(今日すでに対象正答した = LastEligibleCorrectDate == 今日)
  - 克服済みのとき: NextReviewDate、ReviewDue(IsDue)
  - WeakScore(苦手のときだけ。WeakWordScorer.Score。画面で並びの理由を見せる用)
- 並べ方:
  - `WeakFirst`(既定、苦手度の順): 苦手は `WeakWordScorer.Order` の並び → 克服済みは NextReviewDate の早い順(null は最後)→ WordId
  - `RecentMistake`(最後の誤答が新しい順): LastWrongAt の新しい順(null は最後)→ WordId
  - 同じ並びの中の最後の決め手は WordId(Ordinal)で、結果は記録の辞書の順に依らない

### 4.3 Flow
| 状態 | 中身 | 受け付ける操作 |
|---|---|---|
| `HomeScreen`(変更) | 既存 + `Status`(StudyStatus) | StartPractice / OpenWeakList |
| `WeakListScreen`(新規) | Rows、Sort、WeakOnly、直前の開始が0語のときの理由、開始の保存失敗の文言(Diagnostic つき) | ChangeWeakList(sort, weakOnly) / StartWeakPractice(choice) / BackToHome |

- OpenWeakList(): ホームから。既定は WeakFirst・weakOnly = false。
- ChangeWeakList(sort, weakOnly): 作り直す(時刻は今)。
- StartWeakPractice(choice): choice の WeakWordsOnly を真にして、ホームの StartPractice と同じ手順で始める(出題の選択 → 開始 → 保存)。0語・保存失敗のときは WeakListScreen のまま理由を出す。始め方の処理は1つにまとめ、戻る画面だけを変える。
- BackToHome(): ホームへ(学習状況を作り直す)。
- ホームを作るときは毎回 `StudyStatus.From` で学習状況を入れる(起動・GoHome・Discard・StartFresh・0語・開始失敗)。
- 未完了の練習があるときはホームにならない(既存の約束)ので、苦手一覧からも練習は1つだけ。

## 5. テスト方針
- 単体(Core):
  - StudyStatus: 記録なし / 各状態 / 再確認の期限の前日・当日・翌日(日本時間 0:00 の前後)/ 単語データに無い記録は数えない / 単語データの ID の重複
  - WeakWordList: 対象(通常・未出題は出ない、weakOnly で克服済みが消える)/ 誤答回数に再練習を含めない / 正答率の null / 最後の誤答は再練習を含めた最新 / 残り回数は設定の必要回数から(2 と 3 で確かめる)/ CountedToday / 克服済みの再確認日と期限 / 単語データに無い単語 / 並び(WeakWordScorer.Order と一致、克服済みは後ろで再確認日順・null 最後、RecentMistake・同点は WordId)/ 記録の辞書の順を変えても同じ結果 / 5,000語で 100ms 程度(目安。大きく外れたら知らせる)
- 結合(Flow):
  - 初回のホームの学習状況 / ホーム → 一覧 → 苦手のみ練習 → 結果 → ホームで語数が変わる / 一覧の並べ替え・絞り込み / 苦手0語で一覧から開始 → 理由つきで一覧のまま・何も書かない / 一覧からの開始の保存失敗 → 一覧のまま文言と Diagnostic / 再起動後(ResumePrompt → Discard → ホーム)でも学習状況がある
  - 受け付けない操作の表に WeakListScreen と新しい操作を足す(ホームで ChangeWeakList、一覧で Answer など)

## 6. リスクと未決事項
- 【要確認 1・案で確定】克服済みの単語の並び(苦手度の順のとき): 案は「苦手の後ろに、次回の再確認日の早い順」
- 【要確認 2・案で確定】単語データに無い単語(データ更新で消えた)を一覧に出すか: 案は「出す(綴りの代わりに ID、消えたことが分かる印)」。学習記録は残っているため
- 【要確認 3・案で確定】苦手一覧から始める苦手のみ練習の、モード・問題数: 案は「練習設定と同じ選択(PracticeChoice)を受け取り、苦手のみだけ真にする」。WPF は一覧画面にも練習設定を出すか、ホームの前回の選択を使う
- リスク: ホームに戻るたびに 5,000語を数える。単純な数え上げなので軽い想定。性能テストで確かめる
