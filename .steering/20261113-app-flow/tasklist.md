# tasklist.md — 画面の手前の進行役(Flow)

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
- [ ] T1: SessionSummary(Core)
- [ ] T2: Flow プロジェクトとテストプロジェクトの追加(sln・CI・architecture.md・CLAUDE.md)
- [ ] T3: 起動(設定 → 単語データ → 学習記録 → 最初の画面、FatalScreen・BothCorrupt・StartFresh・Reload)
- [ ] T4: 再開・終了して新しく始める
- [ ] T5: 練習の開始(選択の重ね合わせ・0語・開始の保存失敗)
- [ ] T6: 回答・判定・保存・再保存・次へ・結果
- [ ] T7: 受け付けない操作・通知の文言・再起動の通し

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー

### 片付け
- [ ] 長く残す決定を docs/DECISIONS.md へ
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ユーザー決定 — 置き場所は新プロジェクト、範囲は通し + 結果画面の集計、単語データはテスト用、画面の状態は進行役が持つ
