# design.md — セッション管理(途中終了・再開)

## 1. 方針
- セッションは immutable な record。`SessionManager`(Application)の関数が「新しいセッション + 新しい学習記録」を返す。呼び出し側(WPF)は両方を1つの ProgressDocument にして1回で保存し、成功してから画面を進める(v1.1 11.2)
- 出題一覧は外から受け取る(QuestionSelector は次のタスク)。問題ごとに「その単語で使えるモード」も受け取り、再練習のモード選びに使う
- セッションの中に通常問題と再練習を1本の列で持ち、列の位置をそのまま通し番号(questionIndex)にする。再練習は必要になった時点で列の最後に足す
- 検討して見送った案: 再練習の列を別に持つ → 通し番号が2系統になり answerId が衝突しやすい

## 2. 変更する場所
| ファイル | 内容 | 新規 / 変更 |
|---|---|---|
| Core/Models/PlannedQuestion.cs | 出題一覧の1件: WordId・Word(綴り)・Mode・EligibleModes | 新規 |
| Core/Models/PracticeSession.cs | SessionId・StartedAt・Settings(スナップショット)・Questions(列)・Answers(回答済み)・RetryAttempts(単語ごとの再練習回数)。今の位置 = Answers の数 | 新規 |
| Core/Models/SessionQuestion.cs | 列の1件: Index・WordId・Word・Mode・Attempt(Normal / Retry)・EligibleModes | 新規 |
| Core/Domain/RetryModeRule.cs | 再練習のモード選び(固定の優先順、手動は同じ) | 新規 |
| Core/Application/SessionManager.cs | Start / Current / Answer / Abandon / PruneAnswerIds / HasUnfinished | 新規 |
| Core/Models/ProgressDocument.cs | `PracticeSession? Session`(無ければ未完了なし) | 変更 |
| Core/Domain/ProgressValidator.cs | セッションの検査を追加 | 変更 |
| docs/DECISIONS.md | セッションの決定 | 変更 |

## 3. データ
- PracticeSession(progress.json の `session`)
  - Questions: 通常問題(0 〜 問題数−1)のあとに再練習が続く。Index は列の位置と一致
  - Answers: 回答済みの {Index, IsCorrect}(入力は持たない。誤答入力は学習記録に残る)
  - 今の位置 = Answers.Count。Questions.Count と同じなら完了
  - Settings: 開始時の LearningSettings を丸ごと(v1.2 7章)
- 完了・中止したセッションは保存しない(ProgressDocument.Session = null)。回答の記録は学習記録に残っている
- Schema: 任意項目の追加だけ。schemaVersion は 1 のまま(無い項目は null)

## 4. 処理の流れ
### Start(sessionId, startedAt, plan, settings)
- plan が空、sessionId が Guid.Empty、WordId・Word が空、Mode が定義外、Mode が EligibleModes に無い → ArgumentException
- 「出題可能な単語が0語なら理由を表示」(v1.0 9.3)は選ぶ側・画面側の責務
- 通常問題を Index 0.. で並べる。設定はそのまま保存

### Answer(session, progress, input, answeredAt)
1. 完了していれば InvalidOperationException
2. q = 今の問題。正誤 = `SpellingJudge.IsCorrect(q.Word, input)`
3. `AnswerRecorder.Apply(progress, AnswerEvent(AnswerId(SessionId, q.Index, q.Attempt), q.WordId, q.Mode, 正誤, input, answeredAt), session.Settings)`
4. Answers に {q.Index, 正誤} を足す
5. 再練習の追加:
   - 最後の通常問題に答えたとき: 通常問題で誤答した単語を、通常問題の順に1件ずつ再練習として足す(RetryCount が 0 なら足さない)
   - 再練習で誤答し、その単語の再練習回数が RetryCount 未満なら、同じ単語の再練習を最後に1件足す
   - 再練習のモードは単語ごとに最初に1回決め(RetryModeRule)、2回目以降も同じ
6. 完了したら(位置 = 列の長さ)、そのセッションの answerId を学習記録から消す(R7)
7. 新しいセッション・新しい学習記録・結果(正誤・差分の元になる正解の綴り・AnswerRecorder の結果・完了したか)を返す

### RetryModeRule
- 手動モード(settings.Practice.AutoMix = false): 元のモード
- 自動ミックス: 日本語 → 音声 → 穴あき → フラッシュ の順で、元のモード以外かつ EligibleModes にある最初のもの。無ければ元のモード

### Abandon(session, progress)
- そのセッションの answerId を学習記録から消し、セッションなしを返す。回答済みの記録はそのまま

### PruneAnswerIds(progress, activeSession?)
- activeSession の SessionId 以外の answerId を消す(読込時の整理。古いセッションの残りや、強制終了で残った分)

### HasUnfinished(document)
- document.Session があり、完了していない

### 検査(ProgressValidator に追加。すべて重大)
- SessionId が Empty / Questions が空 / Index が 0.. の連続でない / 通常問題の後に再練習以外がある / Answers が Questions より多い・Index が順に並んでいない / Mode・Attempt が定義外 / Mode が EligibleModes に無い / WordId・Word が空 / 設定のスナップショットが SettingsValidator で不正
- 警告: 学習記録に、セッションの回答済みなのに answerId が無い(整理済みの可能性)は検査しない(answerId は安全網なので)

## 5. テスト方針
- 単体(xUnit。時刻は固定の DateTimeOffset):
  - Start: 正常 / 空の一覧・Empty の ID・空の WordId・定義外モード・使えないモード → 例外
  - 通常問題だけで全問正解 → 完了・再練習なし
  - 10問中3問誤答 → 13問で完了、再練習は通常問題の順(v1.0 6.2 の例)
  - RetryCount 0 / 1 / 2 / 3(n-1・n・n+1): 再練習で間違え続けたときの回数、正解したら終わり
  - 再練習のモード: 自動で別モード(優先順)、元が日本語なら音声、音声が使えなければ穴あき、別モードが無ければ同じ / 手動は同じ / 2回目以降も同じモード
  - 再練習の回答は学習記録で Retry として数えられる(状態は変わらない)
  - answerId: 通し番号が列の位置と一致、再練習も別の番号 / 同じ問題を2回反映しても1回
  - 設定のスナップショット: 開始後に設定を変えても、回答はスナップショットの値で判定(例: 必要正答日数)
  - 日付をまたぐ: 23:59 開始・翌 0:01 回答で、回答日が克服判定に使われる
  - 完了 → そのセッションの answerId が消える / Abandon → 消える・記録は残る / PruneAnswerIds で他のセッションの分が消える
  - 完了後の Answer → 例外 / 元のセッション・学習記録は変わらない(immutable)
  - 保存 → 読込で同じ問題から続けられる(ProgressJson の往復。Persistence の ProgressRepository 経由でも1件)
  - セッションの無い既存の JSON が読める / 検査の各項目(重大)
- 結合・手動確認: WPF で「途中で閉じる → 再起動 → 再開」を確かめる(このタスクの外)

## 6. リスクと未決事項
- 【確定 2026-11-13】1 再練習のモードは単語ごとに最初の1回で決め、2回目以降も同じモードにする
- 【確定 2026-11-13】2 2回目以降の再練習は、1回目の再練習がすべて終わった後(列の最後)に足す
- 【確定 2026-11-13】3 完了・中止したセッションは progress.json に残さない(Session = null)。セッションの履歴は持たない
- 【確定 2026-11-13】4 answerId は「未完了のセッションの分だけ」残す。完了・中止の時点と、読込時(PruneAnswerIds)に整理する
- 【確定 2026-11-13】5 回答済みの記録(Answers)には入力を持たない(誤答入力は学習記録のモード別記録だけ)。再開した画面で前の回答の入力は表示しない
- 前タスクからの引き継ぎの扱い: 遅れて届いた回答・同じ answerId で中身が違う回答は「今の問題にしか回答できない」作りで起きなくなる。DateTimeOffset の範囲は AnswerRecorder の既存の動きのまま(範囲外は JstClock が例外)
- リスク: 出題一覧を作る QuestionSelector が無いので、実際の画面で通しの確認ができるのは次のタスクの後
