# tasklist.md — 苦手一覧・学習状況

## 状態
- フェーズ: 検証(⑥)
- 最終更新: 2026-11-13
- 次にやること: ⑥ 3体レビュー

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜3 は案で確定)
- [x] ② プランのレビュー(design: P1 2・P2 7 / security: P1 2・P2 6)— P1 をすべて反映、P2 も大半を反映
- [x] ③ テスト計画のレビュー(edge-case: P1 9・P2 6)— P1 をテスト方針に反映

### 実装(tdd スキル)
- [x] T0: JstClock.TryToJst・MasteryRules.CountedToday と WeakWordScorer の置き換え
- [x] T1: StudyStatus(Core)
- [x] T2: WeakWordList(Core)
- [x] T3: Flow の HomeScreen.Status と WeakListScreen・操作

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(Red を確認してから実装。見るだけの操作で書かない・ホームの学習状況は実装を壊して Red を確認)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] P2(②③): 誤答入力の改行・制御文字の表示(WPF)
- [x] 長く残す決定を docs/DECISIONS.md へ(「苦手一覧・学習状況」の節)
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: DateTimeOffset.MinValue は日本時間に直せる(+9時間で範囲内)。直せないのは MaxValue 側だけ
- 2026-11-13: PracticeFlow.cs は 294 行。次に操作を足すときは分割を考える
- 2026-11-13: ユーザー決定 — 範囲は苦手 + 克服済み(苦手だけの絞り込みつき)、並べ替えは苦手度の順と最後の誤答が新しい順、ホームは状態ごとの語数、Flow の画面にする
