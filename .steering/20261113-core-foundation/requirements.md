# requirements.md — Core の土台(後追い記録)

<!-- 2026-11-13 に、実装済みの feature/core-foundation を後から記録したもの。
     実装はクラウド側で先行しており、本来の「要件→設計→実装」の順ではない点に注意 -->

## 目的
- なぜやるか: WPF 画面より先に、Windows に依存しない学習ロジックの土台を作り、テストで固める(仕様 v1.0 19章「Core・テストを先行させる」)
- 終わったときの状態(Outcome): 単語データの読込・検証、正誤判定、差分の表示文、学習設定、1回の練習の出題枠が Core の API として使える

## 範囲
- やること: 仕様 v1.0 19章の 1〜4(単語データモデル・検証・正誤判定・差分計算)、v1.2 の学習設定(LearningSettings / DefaultSettings / SettingsValidator / JSON 変換)、8 の一部(出題枠とモード配分の計算)
- やらないこと: 学習記録・苦手/克服判定・再確認スケジュール(19章 5〜7)、出題する単語の選択、セッション管理、保存・復旧、WPF 画面・音声、単語データ約5,000語の取り込み

## 機能要件
| ID | 内容 | 優先度 | 入力 | 出力 |
|---|---|---|---|---|
| R1 | 単語データモデルと読込 | Must | words.json(schemaVersion / wordSetVersion / sourceVersion / words) | WordSet |
| R2 | 単語データ検証 | Must | WordSet | ValidationIssue の一覧(Error / Warning) |
| R3 | 正誤判定 | Must | 正解の綴り・入力 | 正誤(大文字小文字を区別しない、前後の空白を無視、途中の空白は無視しない) |
| R4 | 差分計算と表示文 | Must | 正解の綴り・入力 | 置換 / 不足 / 余分の一覧と日本語の説明 |
| R5 | 日本時間の日付 | Must | TimeProvider | JST の現在時刻・日付 |
| R6 | 学習設定 | Must | settings.json(欠けや未知の項目を含む) | LearningSettings(欠けは初期値で補い、未知は無視) |
| R7 | 設定検証 | Must | LearningSettings | SettingsIssue の一覧 |
| R8 | 出題枠の計算 | Must | 問題数・設定 | 新規 / 苦手 / 再確認の枠数、モード別の問題数 |

## 非機能要件
- Core は Windows 非依存(net10.0、WPF・ファイル・現在時刻に触れない)。将来のスマホ版で再利用する
- 学習ルールの値は LearningSettings に置き、ロジックに直接書かない(v1.2)

## 受入条件
- [x] `dotnet build -warnaserror` が警告 0 で通る
- [x] `dotnet test` が全件通る(2026-11-13 にクラウドで xUnit 実行、84/84 通過)
- [x] DECISIONS.md の例(出題枠・モード配分・差分の表示)がテストで確認されている

## 参照
- 関連する永続文書: docs/spec/v1.0(4・7・8・19章)、v1.1、v1.2、docs/DECISIONS.md
- 前提・制約: 日本語訳の辞書は未確定(ライセンス確認待ち)
