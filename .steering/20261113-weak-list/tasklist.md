# tasklist.md — 苦手一覧・学習状況

## 状態
- フェーズ: 実装(tdd)
- 最終更新: 2026-11-13
- 次にやること: T0 から実装

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜3 は案で確定)
- [x] ② プランのレビュー(design: P1 2・P2 7 / security: P1 2・P2 6)— P1 をすべて反映、P2 も大半を反映
- [x] ③ テスト計画のレビュー(edge-case: P1 9・P2 6)— P1 をテスト方針に反映

### 実装(tdd スキル)
- [ ] T0: JstClock.TryToJst・MasteryRules.CountedToday と WeakWordScorer の置き換え
- [ ] T1: StudyStatus(Core)
- [ ] T2: WeakWordList(Core)
- [ ] T3: Flow の HomeScreen.Status と WeakListScreen・操作

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] P2(②③): 誤答入力の改行・制御文字の表示(WPF)
- [ ] 長く残す決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ユーザー決定 — 範囲は苦手 + 克服済み(苦手だけの絞り込みつき)、並べ替えは苦手度の順と最後の誤答が新しい順、ホームは状態ごとの語数、Flow の画面にする
