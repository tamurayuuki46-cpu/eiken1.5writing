# decisions.md — Core の土台(後追い記録)

<!-- 長く残す決定は docs/DECISIONS.md に転記済み。ここはこのタスクで決めた経緯の記録 -->

| 日付 | 決めたこと | 理由 | 見送った案 | 転記先 |
|---|---|---|---|---|
| 2026-11 | 学習ルールの値は LearningSettings のプロパティ初期値に一本化 | SSoT。初期値 = v1.1 の決定値(v1.2) | DefaultSettings に値を書く | docs/DECISIONS.md |
| 2026-11 | 設定ファイルの欠けは初期値で補い、未知の項目は無視 | 前方・後方の互換性 | 読込エラーにする | docs/DECISIONS.md |
| 2026-11 | モード比率は 4項目の型 ModeWeights | キーの欠けで重み0になる事故を防ぐ | Dictionary | docs/DECISIONS.md |
| 2026-11 | 克服対象モード(日本語・音声)は設定にせず MasteryRules で固定 | v0.1 では変更不要。誤設定で克服判定が壊れるのを防ぐ | 設定にする | docs/DECISIONS.md |
| 2026-11 | 新規/苦手の端数は新規側に切り上げ、再確認は苦手枠と入れ替え、上限は区切り形式 reviewLimits | 任意の問題数にも対応できる形 | 問題数ごとの固定表 | docs/DECISIONS.md |
| 2026-11 | モードの自動配分は最大剰余法、同点は 日本語→音声→穴あき→フラッシュ | 結果を決定的にしてテストできるようにする | 乱数で配分 | docs/DECISIONS.md |
| 2026-11 | 差分は同点のとき 置換→不足→余分→一致 を優先、同じ文字が続く不足は後ろ側で表示 | 表示を安定させる(recieve は2文字違い、accomodate は6文字目の m が不足) | 隣接入れ替えを1操作とみなす | docs/DECISIONS.md |
| 2026-11 | 日付は日本時間 +9:00 固定、時刻は TimeProvider で受け取る | 日付の境目を決定的にし、テストで固定できるようにする | DateTime.Now | docs/DECISIONS.md |
| 2026-11-13 | xUnit の実行はクラウド側でも .NET 10 SDK を入れて行う | NuGet に接続できるようになったため。84/84 通過 | 簡易ランナーで代用 | CLAUDE.md 学びメモ |
| 2026-11-13 | settings.json の形は実装の形(camelCase・列挙は型名の文字列・reviewLimits は配列)を正式にする | 保存処理の前に確定しておかないと後で移行が要る | v1.2 6章の例の形に実装を合わせる | docs/DECISIONS.md |
| 2026-11-13 | 明示的な null・壊れた JSON は InvalidDataException にそろえる(RespectNullableAnnotations + 要素の null 確認) | 呼び出し側の例外処理を1種類にし、database.md の破損時の手順を書きやすくする | null を初期値で補う | docs/DECISIONS.md |
| 2026-11-13 | 設定値に上限を付ける(SettingsLimits) | 手で書き換えた値で桁あふれ・巨大確保を起こさない | 上限なし | docs/DECISIONS.md |
| 2026-11-13 | Domain → Settings の参照を認める | v1.2 4章の WeakWordService / MasteryService が設定を使うため | Domain は数値を引数で受け取る | docs/DECISIONS.md・architecture.md |
