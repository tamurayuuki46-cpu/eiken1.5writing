# 設計: 語彙セット閲覧・範囲指定学習

## Core
- `VocabularySets`(Application): 整列(PriorityTier 昇順 → FrequencyRank ありを先に昇順 → WordId の Ordinal)。単位は 50 / 100 だけ。
  100語は先頭から隣接する 50語の2組(= 100語ずつの切り出しと同じ)。末尾の端数も1セット。`NumberContaining(ordered, unit, wordId)` で単位切替時の戻り先を求める。
  語の掃除(空 ID・重複 ID)は QuestionSelector と同じ `CleanWords` を共有する。
- `WordStudyState`(Unseen / Normal / Weak / Mastered / ReviewDue)と `VocabularyWordRow`: 一覧の各語の状態(StudyStatus と同じ分類)。
- `VocabularySetScope`(Application、セッションに保存): `UnitSize`・`Number`・`TotalSets`・`WordSetVersion`・`WordIds`(許可集合。表示順)。
  `PracticeSession.Scope`(任意項目。null は書かない)。null = 範囲指定なし。空配列は不正(範囲が空の学習は始められない)。
- `QuestionSelector.Select(..., IReadOnlySet<string>? allowedWordIds = null)`: 語の掃除の直後(モード適格性・カテゴリ分類より前)で交差させる。
  null は従来どおり(乱数の消費順も同じ)。空集合は `SelectionEmptyReason.EmptyScope`。
- `SessionManager.Start(..., VocabularySetScope? scope = null)`: 範囲があれば全問が範囲内であることを検査して保存する。
- `SessionValidator`: Scope の形(単位・番号・重複・空)と、全 Questions(再練習を含む)が許可集合に入っていること。範囲が無ければ何も検査しない。
- schemaVersion は 2 のまま(任意項目 `scope` の追加。flashShownStep と同じ扱い)。古いアプリは無視する(セッションの列は固定なので範囲外は出ない)。

## Flow
- 画面: `VocabularySetScreen`(閲覧)、`SetStartPreviewScreen`(不足確認・0問・開始保存失敗)。
- 操作: `OpenVocabularySets` / `NextSet` / `PreviousSet` / `ChangeSetUnit` / `CloseVocabularySets` / `PreviewSetPractice(choice)` / `ConfirmSetStart` / `CancelSetStart`。
- `PreviewSetPractice`: Selector を範囲つきで1回だけ実行して `SetPlan`(範囲・選択・設定・Plan・可用モードの写し・語彙版)を保持する。
  Plan 件数 == 希望 → 確認なしで開始 / 0 → 開始不可の画面 / 途中 → 確認の画面。どれも保存しない(開始の保存だけが書く)。
- `ConfirmSetStart`: 失効検査(語彙版・可用モード・設定・範囲が preview と同じか)。違えば Selector を撃ち直して**新しい確認を出し、開始しない**。
  同じなら、保持した Plan から SessionId を1回だけ採番して候補文書を作り保存。失敗したら候補を保持し、同じ画面で再保存(同じ SessionId・Plan)。
- 通常の `StartPractice` / `StartRecommended` / `StartWeakPractice` は範囲を渡さない(セット制限は `SetPlan` と `session.Scope` にだけある)。
- `ResumeSummary` に範囲の情報(単位・番号・総数・語彙更新)と段階を足す。再開は既存の ResumeCheck + 範囲検査(読込時 Validator)。
- 語彙更新の通知: 起動時、保存済みの語彙版と今の版が違えば「語彙セットの区切りが変わった可能性」を通知(閲覧は今のデータで再編)。

## Presentation / WPF
- `VocabularySetViewModel`(一覧・前後・50/100・開始条件=問題数/出題/苦手のみ)、`SetStartPreviewViewModel`(希望・可能・「N問で開始」・「条件に戻る」・再保存)。
- ホームに「語彙セット」の入口。MainWindow に DataTemplate、Esc で戻る。

## 仕様・資料との差分(判断)
- P0-ST の本文が参照できなかったため ST-T01〜22 の個別定義は未確認。AC 4.2 の要約の分類に合わせ、テスト名に ST-nn / AC-Xnn を付けた。
- セット開始の条件はホームの自由選択と同じ項目(問題数・出題方法・苦手のみ。フラッシュ秒数は保存済みの値)に絞った。
- 0問のときも確認の画面(開始ボタンなし・理由・「条件に戻る」)を使う(「条件変更の導線」を同じ部品で持つため)。
