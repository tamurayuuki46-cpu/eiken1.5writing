# design.md — セッション管理(途中終了・再開)

## 1. 方針
- セッションは immutable な record。`SessionManager`(Application)の関数が「新しいセッション + 新しい学習記録」を返す。呼び出し側(WPF)は両方を1つの ProgressDocument にして1回で保存し、成功してから画面を進める(v1.1 11.2)
- 出題一覧は外から受け取る(QuestionSelector は次のタスク)。問題ごとに「その単語で使えるモード」も受け取り、再練習のモード選びに使う
- セッションの中に通常問題と再練習を1本の列で持ち、列の位置をそのまま通し番号(questionIndex)にする。再練習は必要になった時点で列の最後に足す
- 検討して見送った案: 再練習の列を別に持つ → 通し番号が2系統になり answerId が衝突しやすい

## 2. 変更する場所
| ファイル | 内容 | 新規 / 変更 |
|---|---|---|
| Core/Application/PlannedQuestion.cs | 出題一覧の1件: WordId・Word(綴り)・Mode・EligibleModes | 新規 |
| Core/Application/PracticeSession.cs | SessionId・StartedAt・Settings(スナップショット)・Questions・Answers。位置・通常問題数・完了は Questions と Answers から求める(保存しない) | 新規 |
| Core/Application/SessionQuestion.cs / SessionAnswer.cs | 列の1件(WordId・Word・Mode・Attempt・EligibleModes)/ 回答の1件(IsCorrect・StateBefore・StateAfter。入力は持たない) | 新規 |
| Core/Domain/RetryModeRule.cs | 再練習のモード選び(手動は同じ、自動は固定の優先順) | 新規 |
| Core/Application/SessionManager.cs | Start / Current / Answer / Abandon / PruneAnswerIds / Replay(回答から列を作り直す。検査と共用) | 新規 |
| Core/Application/SessionValidator.cs | セッションの検査(不備があればセッションだけ捨てる) | 新規 |
| Core/Models/ProgressDocument.cs → Core/Application/ProgressDocument.cs | 層のルールに合わせて Application へ移し、`PracticeSession? Session` を足す(2026-11-13 決定) | 移動・変更 |
| Core/Domain/ProgressValidator.cs | `Validate(ProgressDocument …)` の形を削除(Domain から Application を参照しないため)。学習記録の検査はそのまま | 変更 |
| Persistence/ProgressRepository.cs | 読込時にセッションを検査し、不備ならセッションだけ捨てて知らせる(`SessionDiscarded`・警告)。壊れたファイルの写しを退避名で残す。読込後に answerId を整理 | 変更 |
| Persistence/AtomicJsonFile.cs | `PreserveMainCopy()`(主ファイルの写しを退避名で書く) | 変更 |
| docs/DECISIONS.md / .claude/rules/architecture.md | セッションの決定、保存失敗時の約束、ProgressDocument の置き場所 | 変更 |

## 3. データ
- PracticeSession(progress.json の `session`。無ければ未完了なし)
  - Questions: 通常問題のあとに再練習が続く1本の列。列の位置が通し番号(questionIndex)
  - Answers: 回答した順の {IsCorrect, StateBefore, StateAfter}。Answers[i] は Questions[i] への回答。入力は持たない
  - 位置 = Answers の数、通常問題数 = Questions のうち Normal の数、単語ごとの再練習回数 = Questions のうちその単語の Retry の数(いずれも保存しない = SSoT)
  - Settings: 開始時の LearningSettings を丸ごと(v1.2 7章)
- 完了・中止したセッションは保存しない(Session = null)。v1.0 12.1 の保存項目との対応: 選択モード・設定問題数 → Settings、出題単語と出題順 → Questions、回答済み・未回答 → Answers の数、誤答単語 → Answers の IsCorrect、再練習の進行 → Questions の Retry と Answers
- Schema: 任意項目の追加だけ。schemaVersion は 1 のまま。
  - 互換性: セッションの無い既存の progress.json はそのまま読める
  - 戻し方: 古い版のアプリで開くと `session` は無視され、次の保存で消える(学習記録は残る。失うのは途中の練習の続きだけ)。この版に戻せば新しい形のまま読める

## 4. 処理の流れ
### Start(sessionId, startedAt, plan, settings)
- 次のどれかなら ArgumentException: sessionId が Empty / plan が空・100件超 / settings が SettingsValidator で不正(版が新しすぎる場合を含む)/ WordId・Word が空 / WordId の重複 / Mode が定義外 / EligibleModes が空・定義外を含む・Mode を含まない / 手動モード(AutoMix=false)なのに Mode が ManualMode と違う
- 「出題可能な単語が0語なら理由を表示」(v1.0 9.3)は選ぶ側・画面側の責務
- 未完了のセッションがあるときは、呼び出し側が先に Abandon してから Start する

### Answer(session, progress, input, answeredAt)
1. 完了していれば InvalidOperationException。input が null なら ArgumentNullException(空文字・空白は誤答)
2. q = Questions[位置]。正誤 = `SpellingJudge.IsCorrect(q.Word, input)`
3. `AnswerRecorder.Apply(progress, AnswerEvent(AnswerId(SessionId, 位置, q.Attempt), q.WordId, q.Mode, 正誤, input, answeredAt), session.Settings)`。結果が重複なら InvalidOperationException(セッションと学習記録が食い違っている)
4. Answers に {正誤, 前後の状態} を足し、再練習を足す(下)
5. 完了したら、そのセッションの answerId を学習記録から消す(PruneAnswerIds)
6. 返す値: 新しい学習記録 / 保存用のセッション(完了なら null)/ 結果画面用のセッション(完了でもそのまま)/ 正誤 / 正解の綴り / AnswerRecorder の結果 / 完了したか

### 再練習の足し方(Answer と Replay で共通)
- 最後の通常問題に答えたとき: 通常問題で誤答した単語を通常問題の順に、再練習として1件ずつ足す(RetryCount = 0 なら足さない)
- 再練習で誤答し、その単語の Retry の数が RetryCount 未満なら、同じ単語・同じモードの再練習を最後に1件足す
- モード: RetryModeRule(手動は元のモード、自動は 日本語 → 音声 → 穴あき → フラッシュ の順で元以外かつ使える最初のもの。無ければ元のモード)
- RetryTiming は AfterNormalQuestions だけを前提にする(他の値は SettingsValidator が拒否)

### Abandon(session, progress) / PruneAnswerIds(progress, keepSessionId?)
- Abandon: そのセッションの answerId を消した学習記録を返す。回答済みの記録はそのまま。呼び出し側は Session = null で保存する
- PruneAnswerIds: keepSessionId 以外の answerId を消す。完了時・Abandon・読込時(ProgressRepository)に使う

### Replay(normalQuestions, answers, settings)
- 通常問題と回答の列から、再練習の足し方を同じように再生して Questions を作り直す。検査で「保存された Questions と一致するか」を確かめるのに使う

### 検査(SessionValidator。不備があれば ProgressRepository がセッションだけ捨てる)
- SessionId が Empty / Settings が SettingsValidator で不正・版が新しすぎる
- Questions・Answers・EligibleModes の要素が null / Questions が空 / 通常問題が 1〜100 件でない / 通常問題の WordId の重複 / 通常問題の後に通常問題がある / WordId・Word が空 / Mode・Attempt・EligibleModes が定義外 / Mode が EligibleModes に無い
- Answers が Questions 以上(完了したセッションは保存しない約束)
- Replay の結果が保存された Questions と一致しない(正解した単語の再練習・回数超過・通常問題を答える前の再練習・モードの違い をまとめて検出)
- 再練習の単語が学習記録に無い
- ※ 明示的な null の項目(`"settings": null` など)は従来どおり読込失敗(ファイル全体が壊れている扱い)

### 読込(ProgressRepository、設定の Load の後)
1. 学習記録の検査で重大 → 従来どおり壊れている扱い
2. セッションがあり SessionValidator で不備 → セッションを null にし、警告「前回の途中の練習を復元できませんでした」、`SessionDiscarded = true`。主ファイルの写しを退避名で残す(PreserveMainCopy)
3. answerId を整理(残すのは今のセッションの分だけ)。メモリ上だけで、次の保存で書かれる

### 保存失敗時の約束(DECISIONS.md に記載)
- 呼び出し側は Answer が返した値(学習記録とセッション)を保存し直す。Answer を同じ問題でもう一度呼ばない(呼ぶと重複として例外)

## 5. テスト方針
- 単体(xUnit。時刻は固定の DateTimeOffset):
  - Start: 正常 / 空・101件の一覧、Empty の ID、不正な設定、空の WordId・Word、WordId の重複、定義外モード、空・定義外の EligibleModes、使えないモード、手動モードでモードが違う → 例外
  - 1問だけ: 正解でその場で完了 / 誤答で2問目が再練習
  - 10問中3問誤答 → 13問で完了、再練習は通常問題の順(v1.0 6.2)
  - RetryCount 0(全問誤答でも通常問題数で完了)/ 1 / 2 / 3(10問全問誤答で 40問)。2回目以降の再練習は1回目がすべて終わった後(W1r1, W2r1, W1r2 …)。再練習で正解したら終わり
  - 再練習のモード: 自動で別モード(優先順)、音声が使えなければ穴あき、使えるモードが1つだけなら同じ / 手動は同じ / 2回目以降も同じ
  - 再練習の回答は学習記録で Retry として数えられ、状態は変わらない。日付をまたいだ再練習(23:59 誤答 → 翌 0:01 再練習で正解)も克服に数えない
  - 通常問題の日付またぎ: 回答日が克服判定に使われる
  - answerId: 通し番号 = 列の位置 / 同じ問題をもう一度 Answer に通す(古いセッション + 新しい学習記録)→ 例外
  - スナップショット: 開始後に今の設定を変えても、必要正答日数・RetryCount・AutoMix はスナップショットの値
  - 完了 → answerId が消え、保存用セッションは null・結果画面用は完了したセッション / Abandon(通常問題の途中・再練習の途中)→ 記録は残り answerId は消える / PruneAnswerIds: 今のセッションの分は残り、null なら全部消える
  - 完了後の Answer・null の入力 → 例外 / 空文字は誤答 / 元のセッション・学習記録は変わらない
  - 状態の記録: Answers の StateBefore・StateAfter が学習記録の変化と一致
  - 保存・読込を挟む: 最後の通常問題の直前で保存 → 読込 → 回答で再練習ができる / 再練習の途中で保存 → 読込 → 2回目の再練習が同じモード / 読込で他のセッションの answerId が消える(ProgressJson の往復と ProgressRepository 経由)
  - 検査: 上の各項目を1つずつ(要素の null・再練習の不一致・回数超過・通常問題の前の再練習・完了済み・学習記録に無い単語・スナップショット不正)→ セッションだけ捨てられ、学習記録は Loaded、写しが退避名で残る
  - セッションの無い既存の JSON が読める / `"session": null` が読める
- 結合・手動確認: WPF で「途中で閉じる → 再起動 → 再開」を確かめる(このタスクの外)

## 6. リスクと未決事項
- 【確定 2026-11-13】1 再練習のモードは単語ごとに最初の1回で決め、2回目以降も同じモードにする
- 【確定 2026-11-13】2 2回目以降の再練習は、1回目の再練習がすべて終わった後(列の最後)に足す
- 【確定 2026-11-13】3 完了・中止したセッションは progress.json に残さない(Session = null)。セッションの履歴は持たない
- 【確定 2026-11-13】4 answerId は「未完了のセッションの分だけ」残す。完了・中止の時点と、読込時(PruneAnswerIds)に整理する
- 【確定 2026-11-13】5 回答済みの記録(Answers)には入力を持たない(誤答入力は学習記録のモード別記録だけ)。再開した画面で前の回答の入力は表示しない
- 【確定 2026-11-13】6 セッションに不備があれば、セッションだけ捨てて知らせる(学習記録は読む。壊れたファイルの写しは退避名で残す)
- 【確定 2026-11-13】7 セッションの型と ProgressDocument は Application に置く(層のルールは変えない)
- 前タスクからの引き継ぎの扱い: 遅れて届いた回答・同じ answerId で中身が違う回答は「今の問題にしか回答できない」作りで起きなくなる。DateTimeOffset の範囲は AnswerRecorder の既存の動きのまま(範囲外は JstClock が例外)
- リスク: 出題一覧を作る QuestionSelector が無いので、実際の画面で通しの確認ができるのは次のタスクの後
