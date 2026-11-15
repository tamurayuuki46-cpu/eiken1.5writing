# 設計: ロードマップ② 結果・苦手診断・復習

## 現行の不足(2026-11-15 確認。①は feature/home-four-modes の c993ceb に反映済み・未コミットの作業は .steering/20261114-audio-mode/ だけ)
- 「答えを見る」が無い。空文字回答で代用すると AnswerRecorder が LastWrongInput を "" で上書きする(UI v0.3 §1 の指摘どおり)。
- 苦手一覧は Flow(WeakListScreen)・Core(WeakWordList)まであるが、ViewModel・View・DataTemplate・入口が無い。入口はホームからのみ(Flow)で、結果からは開けない。戻り先を覚えない(BackToHome)。
- 行の詳細(モード別の状態・実誤入力の日時)が足りない。LastWrongInput は「最後の誤答の入力」で、答えを見る不正解が最新だと null になりうる。
- 段階別の進捗表示(通常/後回し/再練習)は ①までに実装済み(ScreenText.ProgressText、QuestionScreenFor)。テストで固定する。

## 決定
### Core
- `AnswerEvent.Revealed`(既定 false)。Revealed の回答は IsCorrect = false が必須。`ModeRecord.NormalWrong` を +1、`LastWrongAt` は新しければ更新、**`LastWrongInput` は更新しない**。
- `ModeRecord.LastWrongInputAt`(任意項目、null は書かない): 実入力の時刻。null のときは LastWrongAt と同じとみなす。
  - 実入力の誤答: LastWrongInput・LastWrongAt を更新し LastWrongInputAt = null。
  - 答えを見る: 実入力があり LastWrongInputAt が null なら、LastWrongAt を更新する前に LastWrongInputAt = 前の LastWrongAt にする。
  - 旧データ(項目なし)は従来どおり読め、書き換えない(互換)。schemaVersion は 2 のまま(任意項目の追加)。古いアプリは無視する。
- `SessionAnswer.Revealed`(false は書かない)。`SessionManager.Reveal`(Deferred のときだけ。Normal・Retry は InvalidOperationException)。`CanReveal`。
- `SessionValidator`: Revealed の採点は、後回しした通常問題への不正解でなければならない(再練習・正解・スキップしていない問題は拒否)。
- `SessionSummary`: 変更なし(Revealed も通常の不正解として数える)。`SummaryWord` に Revealed を足す(表示用)。
- `LatestMistake.TypedInputOf`: 実入力のうち最新(LastWrongInputAt ?? LastWrongAt)。苦手度は従来の LatestMistake.Of(LastWrongAt。答えを見るも含む)のまま。
- `WeakWordRow`: `LastWrongInput` は実入力の最新に変更(モード・時刻は `LastWrongInputMode`・`LastWrongInputAt`)。`LastWrongAt` は最後の誤答(答えを見るを含む)のまま。`Modes`(モード別の通常・再練習の件数と正答率)を足す。

### Flow
- `PracticeFlow.Reveal()`: QuestionScreen の CanReveal(Deferred・保存待ちでない・フラッシュは隠した後)。Answer と同じ保存手順(先に文書へ反映 → 保存 → FeedbackScreen)。FeedbackScreen.Revealed = true・Diff は空。保存失敗時の RetrySave は同じ文書を保存し直す(Reveal を呼び直さない)。
- `OpenWeakList()` はホームまたは結果から。結果から開いたときは `ResultScreen` をそのまま覚え(完了した Session は文書に無い)、`CloseWeakList()` で同じ画面に戻る。ホームからならホーム(作り直し)。`BackToHome` は `CloseWeakList` に置き換える。`WeakListScreen.Origin` で戻り先を示す。

### Presentation / WPF
- `WeakListViewModel`(行は展開・閉じるを持つ。Flow を呼ばない=無書込)、`ResultViewModel.OpenWeakListCommand`、`HomeViewModel.OpenWeakListCommand`。
- `QuestionViewModel.RevealCommand`(Flow の CanReveal だけで決める)。`FeedbackViewModel.IsRevealed`(差分・「あなたの入力」を出さない)。
- 文言: 判断を伴うものは Flow、値の言い換えは ScreenText。

## 戻し方(Schema)
- `lastWrongInputAt` と `revealed` は任意項目。旧版は無視する(旧版で保存し直すと消える: 答えを見る不正解の「実入力の時刻の分離」が失われるだけで、記録は壊れない)。自動マイグレーションは不要。
