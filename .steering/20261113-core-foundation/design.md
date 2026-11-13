# design.md — Core の土台(後追い記録)

## 方針
- 実現方法: `EikenSpelling.Core`(net10.0 クラスライブラリ)を層別(Models / Domain / Application / Settings)に分ける。処理は状態を持たない static クラスと record で書き、テストしやすくする
- 検討して見送った案: モード比率を `Dictionary<LearningMode,int>` で持つ案 → キーが欠けると重み0になる事故が起きるので、4項目の型 `ModeWeights` にした

## 変更するファイル
| ファイル | 役割 | 種別 |
|---|---|---|
| Models/WordEntry.cs | WordEntry / WordSet / BlankSource | 新規 |
| Models/LearningMode.cs | 出題モード(日本語・音声・穴あき・フラッシュ) | 新規 |
| Domain/SpellingJudge.cs | 正規化と正誤判定 | 新規 |
| Domain/SpellingDiffService.cs | Levenshtein の編集手順を復元し、差分と説明文を返す | 新規 |
| Domain/WordSetValidator.cs | ID 重複・綴り・穴あき・克服モードの有無の検証 | 新規 |
| Domain/JstClock.cs | TimeProvider から JST の時刻・日付を出す | 新規 |
| Domain/MasteryRules.cs | 克服対象モード(日本語・音声)の固定ルール | 新規 |
| Application/WordSetLoader.cs | words.json の読込 | 新規 |
| Application/QuestionPlanner.cs | 出題枠(新規/苦手/再確認)とモード配分 | 新規 |
| Settings/LearningSettings.cs ほか | 設定の型・初期値・検証・JSON 変換 | 新規 |

## データ
- 扱うデータ(Entity): WordSet / WordEntry(単語データ)、LearningSettings(学習設定)
- データの持ち主(Data Owner): ファイルの読み書きは WPF 側(未作成)。Core は文字列・Stream を受け取って変換するだけ
- Schema: `WordSet.CurrentSchemaVersion = 1`、`LearningSettings.CurrentVersion = 1`。既存データなし(初版)

## 処理の流れ
1. JSON 文字列/Stream → WordSetLoader / LearningSettingsJson で読み込む
2. WordSetValidator / SettingsValidator で検証し、問題を一覧で返す(例外にしない)
3. QuestionPlanner が問題数と設定から枠を計算し、回答は SpellingJudge / SpellingDiffService で判定・説明する

## テスト方針
- 単体: xUnit 84件(JstClock 3 / LearningSettings 11 / SettingsValidator 13 / QuestionPlanner 18 / SpellingDiffService 12 / SpellingJudge 11 / WordData 16)
- 結合・手動確認: なし(WPF 未作成のため)

## リスク・未決
- `RetryTiming` は値が1つだけ(AfterNormalQuestions)。将来の追加時に JSON 互換を確認する
- 単語データ約5,000語の取り込みと日本語訳の辞書は未着手
