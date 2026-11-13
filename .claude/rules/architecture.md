---
paths:
  - "src/**"
---
<!-- paths: を書くと、該当ファイルを扱うときだけ読み込まれる(一般仕様) -->

# アーキテクチャルール

## 層構成(仕様 v1.2 のフォルダ構成)
- 機能別ではなく層別に分ける。`src/EikenSpelling.Core/` の下:
  - `Models/` 単語データ・出題モードなどのデータ型
  - `Domain/` 正誤判定・差分・検証・日本時間・固定ルール(MasteryRules)
  - `Application/` 読込・出題枠の計算など、複数の部品を組み合わせる処理
  - `Settings/` 学習設定(LearningSettings)・初期値・検証・JSON 変換
- 画面・音声・ファイル保存は `src/EikenSpelling.Wpf/` に置く。

## 依存
- 依存の向き: `Wpf → Core`、Core の中は `Application → Domain / Settings → Models`。逆向きの参照は禁止。
- Domain → Settings の参照は認める(苦手度・克服の判定が設定値を使うため。v1.2 4章、DECISIONS.md「層の依存」)。Settings → Domain は禁止。
- Core では次を使わない: `System.Windows.*`、ファイルの読み書き、`DateTime.Now` / `DateTime.Today`、`%APPDATA%` などの環境依存。
  時刻は `TimeProvider`、データは引数で受け取る。
- 新しい NuGet パッケージの追加は、理由を添えて確認を取ってから。

## 責務
- 1ファイル1責務。1ファイルが 300 行を超えたら分割を検討する。
- 同じ情報・同じ処理を2か所に書かない(SSoT)。
  - 学習ルールの値の正本は `LearningSettings` の各プロパティ初期値(v1.2)。ロジックに数値を直接書かない。
  - 設定にしない固定ルールは DECISIONS.md の「固定ルール」に挙げたものだけ。
- データの持ち主(Data Owner)以外は、そのデータを直接書き換えない。保存ファイルの持ち主は Wpf 側の Repository。

## 迷ったら
- 構成を変える提案は、実装前に design.md に書いて確認を取る。
- 仕様(docs/spec/)・DECISIONS.md と実装が食い違ったら、直さずに報告する。
