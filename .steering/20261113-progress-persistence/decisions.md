# decisions.md — 保存・復旧(progress.json / settings.json)

| 日付 | 決めたこと | 理由 | 見送った案 | 転記先 |
|---|---|---|---|---|
| 2026-11-13 | セッションの進行状態は今回の progress.json に入れず、次のタスクで任意項目として足す | 「無い項目は初期値」の決まりで schemaVersion を上げずに足せる | セッションの形まで今回決める | docs/DECISIONS.md(予定) |
| 2026-11-13 | settings.json の保存・読込・破損時の復旧も今回に含める | progress.json と同じ仕組みで作れる(v1.2 9章 Must・Should) | 設定画面のときにまとめる | — |
| 2026-11-13 | 置き場所の希望は「指定なし」→ 推奨案(新しい Persistence プロジェクト)で設計(【確定 2026-11-13】1) | 本物のファイルでテストでき、Linux と Windows で同じテストが動く | Core+WPF / WPF のみ | design.md |
| 2026-11-13 | 誤答入力の上限 100 文字 / 日時のオフセットはそろえない / 壊れたファイルは退避名で残す / 新しすぎる版は上書きしない(【確定】2〜5) | ① の要確認を案どおり確定 | — | docs/DECISIONS.md(予定) |
| 2026-11-13 | ②③ レビュー反映: 復旧は「壊れた主を移動 → bak から主を作り直す」/ 読めない(Unavailable)と壊れている(Corrupt)を分け再試行 / NewerVersion を版の先読みで判定しロック / 退避名に元の名前とミリ秒・連番 / Save(ProgressDocument)・Load(設定, 時刻)/ schemaVersion は ProgressDocument だけ / 50MB 上限 / 仕様書は直さず DECISIONS.md に差分 | 3体レビュー(P0 3・P1 多数) | 壊れた主をコピーで残す / 読込失敗を一律で破損扱い | design.md |
| 2026-11-13 | 新しすぎる settings.json は初期値で動かし、設定の保存を拒否して知らせる / 誤答入力の上限は設定ではなく固定ルール | 仕様が未定のためおすすめ案で決定 | 初期値で上書き / SettingsLimits に置く | docs/DECISIONS.md(予定) |
| 2026-11-13 | ⑥ レビュー反映: 保存は OutOfMemory 以外の例外をすべて失敗として返す / 新規開始・設定の作り直しは先に中身を検証し、残った tmp も orphan 名へ / 復旧は tmp を退避できなければ中止 / settings も版を先読み(design.md の順序は確定5と矛盾していたため修正)/ schemaVersion の食い違い・int 超えの扱い / 回数の合計のあふれ防止 | 3体レビュー(P1 重複込み 9件) | — | design.md |
