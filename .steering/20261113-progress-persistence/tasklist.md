# tasklist.md — 保存・復旧(progress.json / settings.json)

## 状態
- フェーズ: 設計(②③ レビュー中)
- 最終更新: 2026-11-13
- 次にやること: ②③ の結果を報告し、P0・P1 を設計に反映

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [ ] ② プランのレビュー(design-reviewer・security-reviewer)
- [ ] ③ テスト計画のレビュー(edge-case-reviewer)

### 実装(tdd スキル)
- [ ] T1: Core — ProgressDocument / ProgressJson / [JsonIgnore] / 誤答入力の切り詰め
- [ ] T2: Core — ProgressValidator(重大・警告)
- [ ] T3: Persistence プロジェクトとテストプロジェクトの追加(sln)
- [ ] T4: IFileOperations / AtomicJsonFile(保存・読込・復旧)
- [ ] T5: ProgressRepository(Load / Save / StartFresh)
- [ ] T6: SettingsRepository(Load / Save)

### 検証
- [ ] `dotnet build -warnaserror` と `dotnet test` が全件通る
- [ ] 受入条件(requirements.md 5.)を1つずつ確認
- [ ] ⑥ 3体レビュー(P0・P1 が残っていない)

### 片付け
- [ ] decisions.md の決定を docs/DECISIONS.md へ。architecture.md・database.md・README を更新
- [ ] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、セッションは後で追加・設定ファイルも含める・置き場所は指定なし(推奨案で設計)と決まった
