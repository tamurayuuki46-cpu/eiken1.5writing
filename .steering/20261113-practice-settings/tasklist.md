# tasklist.md — 練習設定の保存

## 状態
- フェーズ: 設計(① プラン)
- 最終更新: 2026-11-13
- 次にやること: requirements.md と design.md(【要確認】1・2)を確認してもらう

## タスク
### 準備
- [ ] requirements.md を確認してもらう
- [ ] design.md を確認してもらう(【要確認】1・2)
- [ ] ② プランのレビュー(design-reviewer・security-reviewer)
- [ ] ③ テスト計画のレビュー(edge-case-reviewer)

### 実装(tdd スキル)
- [ ] T1: PracticeChoiceApplier.FromSettings
- [ ] T2: 起動で SettingsRepository と読込の状態を持つ、Defaults
- [ ] T3: SettingsScreen と操作(SettingsFlow.cs)

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] 長く残す決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ユーザー決定 — 設定画面で保存したときだけ書く / 初期値に戻すを入れる / 保存できないときは設定画面に留めて理由を出す
