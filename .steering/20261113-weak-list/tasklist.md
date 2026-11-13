# tasklist.md — 苦手一覧・学習状況

## 状態
- フェーズ: 設計(① プラン)
- 最終更新: 2026-11-13
- 次にやること: requirements.md と design.md(【要確認】1〜3)を確認してもらう

## タスク
### 準備
- [ ] requirements.md を確認してもらう
- [ ] design.md を確認してもらう(【要確認】1〜3)
- [ ] ② プランのレビュー(design-reviewer・security-reviewer)
- [ ] ③ テスト計画のレビュー(edge-case-reviewer)

### 実装(tdd スキル)
- [ ] T1: StudyStatus(Core)
- [ ] T2: WeakWordList(Core)
- [ ] T3: Flow の HomeScreen.Status と WeakListScreen・操作

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] 長く残す決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ユーザー決定 — 範囲は苦手 + 克服済み(苦手だけの絞り込みつき)、並べ替えは苦手度の順と最後の誤答が新しい順、ホームは状態ごとの語数、Flow の画面にする
