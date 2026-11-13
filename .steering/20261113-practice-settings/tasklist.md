# tasklist.md — 練習設定の保存

## 状態
- フェーズ: 実装(tdd)
- 最終更新: 2026-11-13
- 次にやること: T0 から実装

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1・2 は案で確定)
- [x] ② プランのレビュー(design: P1 2・P2 7 / security: 通過・P2 6)— P1 をすべて反映、P2 も大半を反映
- [x] ③ テスト計画のレビュー(edge-case: P1 11・P2 4)— P1 をテスト方針に反映

### 実装(tdd スキル)
- [ ] T0: SettingsRepository.CanSave・Status
- [ ] T1: PracticeChoiceApplier.FromSettings
- [ ] T2: 起動で SettingsRepository と読込の状態を持つ、Defaults
- [ ] T3: SettingsScreen と操作(SettingsEditor.cs)

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] 長く残す決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ユーザー決定 — 設定画面で保存したときだけ書く / 初期値に戻すを入れる / 保存できないときは設定画面に留めて理由を出す
