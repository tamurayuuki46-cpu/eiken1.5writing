# decisions.md — 保存・復旧(progress.json / settings.json)

| 日付 | 決めたこと | 理由 | 見送った案 | 転記先 |
|---|---|---|---|---|
| 2026-11-13 | セッションの進行状態は今回の progress.json に入れず、次のタスクで任意項目として足す | 「無い項目は初期値」の決まりで schemaVersion を上げずに足せる | セッションの形まで今回決める | docs/DECISIONS.md(予定) |
| 2026-11-13 | settings.json の保存・読込・破損時の復旧も今回に含める | progress.json と同じ仕組みで作れる(v1.2 9章 Must・Should) | 設定画面のときにまとめる | — |
| 2026-11-13 | 置き場所の希望は「指定なし」→ 推奨案(新しい Persistence プロジェクト)で設計(【要確認】1) | 本物のファイルでテストでき、Linux と Windows で同じテストが動く | Core+WPF / WPF のみ | design.md |
