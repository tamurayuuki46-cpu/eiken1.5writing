# eiken1.5writing

英検準1級ライティングで使う単語のうち、**自分が間違えるスペルを見つけて克服する**ための Windows 用練習ソフト(C# / .NET 10 / WPF)。

## 構成

```
EikenSpelling.sln
├─ src/EikenSpelling.Core          … 学習ロジック(Windows 非依存。将来のスマホ版でも再利用)
│   ├─ Models/        単語データ・出題モード
│   ├─ Domain/        正誤判定・差分計算・単語データ検証・日本時間・克服の固定ルール
│   ├─ Application/   単語データ読込・出題枠の計算
│   └─ Settings/      学習設定・初期値・設定検証・settings.json 変換
├─ src/EikenSpelling.Wpf           … 画面・音声・ファイル保存(未作成。Windows PC 側で作る)
├─ tests/EikenSpelling.Core.Tests  … xUnit
└─ docs/DECISIONS.md               … コードに入っている設計上の決定の早見表
```

## ビルドとテスト

```
dotnet build src/EikenSpelling.Core
dotnet test
```

テストには NuGet から xUnit を取得する必要があります。

## 開発の分担

- **スマホ(クラウドの Claude Code)**:Core・テスト・単語データ
- **Windows PC(CLI)**:WPF 画面・音声・実機での確認
- 同じリポジトリを使い、作業ごとにブランチを切ってから統合する

## 実装状況

- [x] 単語データモデル・読込・検証
- [x] 正誤判定・差分計算
- [x] 日本時間の日付処理(TimeProvider)
- [x] 学習設定(初期値・検証・JSON 変換)
- [x] 出題枠の計算(新規/苦手/再確認、モード配分)
- [ ] 学習記録・苦手/克服判定・再確認スケジュール
- [ ] 出題する単語の選択・セッション管理
- [ ] 保存・復旧(WPF 側)
- [ ] WPF 画面・音声
- [ ] 単語データ(約5,000語)の取り込み

## ライセンス

- 単語データ:NGSL / NAWL / BSL(Browne, C., Culligan, B., Phillips, J.)由来。CC BY-SA 4.0 に従って公開予定
- アプリのコード:未定
