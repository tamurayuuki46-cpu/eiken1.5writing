# tasklist.md — 保存・復旧(progress.json / settings.json)

## 状態
- フェーズ: 検証(⑥ 3体レビュー待ち)
- 最終更新: 2026-11-13
- 次にやること: ⑥ 実装の3体レビュー

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1〜5 は案どおり確定)
- [x] ② プランのレビュー(design-reviewer・security-reviewer)— P0 3 / P1 13(重複あり)を反映
- [x] ③ テスト計画のレビュー(edge-case-reviewer)— P0 2 / P1 7 を反映

### 実装(tdd スキル)
- [x] T1: Core — ProgressDocument / ProgressJson / [JsonIgnore] / SchemaVersion の一本化 / InputRules(切り詰め)/ 回数の上限
- [x] T2: Core — ProgressValidator(重大・警告)
- [x] T3: Persistence プロジェクトとテストプロジェクトの追加(sln)
- [x] T4: IFileOperations / AtomicJsonFile(保存・読込・復旧)
- [x] T5: ProgressRepository(Load / Save / StartFresh)
- [x] T6: SettingsRepository(Load / Save)

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(Core 285 / Persistence 57。Red: Core 50件・Persistence 56件を確認してから実装)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [ ] ⑥ 3体レビュー(1往復目: P0 0 / P1 9(重複込み)→ A〜D・F を修正、Core 291 / Persistence 67。E(二重起動)は WPF の Mutex で防ぐと決定(WPF のタスクの要件)。2往復目の再レビュー中)

### 片付け
- [ ] P2(⑥): 警告を ProgressIssue のまま返す / Corrupt の理由を BothCorrupt・Recovered の結果に残す / JSON オプションの共通化と MakeReadOnly(LearningSettingsJson)/ 版の先読みと Parse で JSON を2回解析 / 不正な UTF-8 を Corrupt に / 50MB を超えるファイルを書けてしまう / orphan が起動のたびに増えうる / StartFresh の結果に文書を含める / Repository はスレッドセーフでない(1スレッドで使う前提を WPF のタスクへ)/ 読込の catch の範囲の理由を記録 / IFileOperations の表(Copy→CreateDirectory)を design に反映
- [ ] P2(②③): 1問ごとの全体書き直しとファイルサイズ / 退避ファイルの数 / 知らない項目は保存し直すと消える(セッション追加時に確認)/ 退避ファイルが多くても読込に影響しないことの確認 / 単語データの版違いの扱いの詳細 / 結果の理由の文言を画面に出す前提の整理
- [x] decisions.md の決定を docs/DECISIONS.md へ。architecture.md・database.md・README を更新
- [x] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: ① の質問で、セッションは後で追加・設定ファイルも含める・置き場所は指定なし(推奨案で設計)と決まった
