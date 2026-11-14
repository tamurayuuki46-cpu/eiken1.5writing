# CLAUDE.md(Claude Code CLI用)

## 1. プロジェクト概要
- 名前: EikenSpelling(eiken1.5writing)
- 目的: 英検準1級ライティングで使う単語のうち、自分が間違えるスペルを見つけて克服する Windows 用練習ソフト
- 利用者: 英検準1級を受ける学習者本人(1人・1台の PC)
- Core Value: 間違えたスペルを差分で示し、苦手語を克服するまで繰り返し出題する
- やらないこと(v0.1): 手動バックアップ・JSON エクスポート/インポート・PC 移行(仕様 v1.0 13.6)、スマホ版の画面
- 現在のフェーズ: 実装。Core・保存(Persistence)・進行役(Flow)まで完了。WPF の骨組み(ホーム → 練習 → 判定 → 結果、日本語・穴あき)を実装中(`.steering/20261114-wpf-shell/`)。UI の工程は UI 技術選定 → UX 設計 → UI 設計 → 設計レビュー → 実装 → 検証の順(各工程の資料は `.steering/20261114-*`)

## 2. 技術スタック
- 言語・フレームワーク: C# / .NET 10 / WPF(画面のみ)
- 保存先: JSON ファイル `%APPDATA%\EikenSpelling\progress.json`(バックアップ `progress.bak.json`)、設定 `settings.json`。DB は使わない
- 実行環境: Windows PC。Core は Windows 非依存(将来のスマホ版で再利用。テストは Linux でも動く)
- パッケージ管理: NuGet(dotnet CLI)

## 3. よく使うコマンド
```bash
dotnet restore              # 依存の導入
dotnet build -warnaserror   # ビルド(警告をエラー扱い = lint・型チェックの代わり)
dotnet test                 # テスト(xUnit)
dotnet run --project src/EikenSpelling.Wpf   # 起動(Windows のみ。WPF 作成後)
```
- 作業の完了前に必ず `dotnet build -warnaserror` と `dotnet test` を通し、全件の結果を見せる。
- クラウド(Linux)では Core とテストだけを扱う。WPF の起動・音声・実機確認は Windows PC 側で行う。

## 4. ディレクトリ構成
```
src/EikenSpelling.Core/   # 学習ロジック(Windows 非依存)
  Models/ Domain/ Application/ Settings/   # 層別(.claude/rules/architecture.md)
src/EikenSpelling.Persistence/  # 保存・読込・復旧(Windows 非依存)
src/EikenSpelling.Flow/   # 画面の手前の進行役: 起動・練習・保存失敗・破損時の画面の状態(Windows 非依存)
src/EikenSpelling.Presentation/  # ViewModel(Windows 非依存。Flow の画面の状態を写す・受け付け開始のゲート・ログ・起動引数)
src/EikenSpelling.Wpf/    # 画面(XAML)・テーマ・起動の合成・二重起動の防止(Windows 専用)。単語データ Data/words.json(道具で生成。手で直さない)
tests/EikenSpelling.Core.Tests/  # xUnit
tests/EikenSpelling.Persistence.Tests/  # xUnit(一時フォルダ)
tests/EikenSpelling.Flow.Tests/  # xUnit(一時フォルダの本物のファイルで通しの流れ)
tests/EikenSpelling.Presentation.Tests/  # xUnit(本物の Flow と手で進める時計で ViewModel を確かめる)
tools/EikenSpelling.WordData/  # 単語データ(words.json)と検証レポートを作る道具(アプリに含めない)
tests/EikenSpelling.WordData.Tests/  # xUnit(道具の統合・組み立て・出力)
data/sources/             # 語彙リストの公式 CSV(改変しない)と SOURCES.md
data/curation/            # 付加データ(日本語訳 ja/*.tsv・Tier 1・除外・手動の穴あき)。words.json はここから作る
data/LICENSE-DATA.md      # 単語データのライセンス(CC BY-SA 4.0)
docs/spec/                # 仕様書(正本)v1.0〜v1.2
docs/DECISIONS.md         # コードに入っている決定の早見表
.steering/_template/      # タスク文書の雛形。タスクごとに .steering/[日付]-[タスク]/ へコピー
.claude/rules/            # トピック別ルール(architecture / testing / review / logging / database)
.claude/agents/           # レビュー役(design / edge-case / security)
.claude/skills/           # 手順(tdd / create-pr)
.claude/settings.json     # 権限(deny)
prompts/8steps.md         # 8ステップの依頼文
```

## 5. 文書の置き場所(技評 SDD・文書2種)
- 仕様の正本は `docs/spec/`。v1.0 が本体、v1.1・v1.2 は v1.0 への変更提案。
- 採用済みの決定と実装で使う値は `docs/DECISIONS.md`。仕様書より先にここを見る。
- タスクごとの文書は `.steering/[YYYYMMDD]-[タスク名]/` に置く
  - requirements.md(何をやるか)/ design.md(どう作るか)/ tasklist.md(手順と進捗)
  - 必要に応じて blockers.md(詰まり)/ decisions.md(決めたこと)
- 文書と実装が食い違ったら、勝手に直さず先に報告する。

## 6. 作業の進め方
- いきなり実装しない。順序: 要件(requirements)→ 設計(design)→ テスト方針 → 実装 → 検証(技評 SDD)。
- 新機能・大きな変更は、着手前に tasklist.md を作って確認をもらう。
- 1タスクは小さく区切り、区切りごとに動作確認する。
- フェーズが変わったら /clear または /compact で文脈を整理する(技評 課題1、SB 第2章)。
- 広い調査はサブエージェントに任せ、要約だけ受け取る(技評 課題1)。
- 実装は tdd スキル、レビューは .claude/agents/ の3体、PRは create-pr スキルを使う。各段階の依頼文は prompts/8steps.md。
- PR のマージは自動(2026-11-13 決定): Claude が作った PR は、3体レビューの P0・P1 が無くなったらドラフトを解除し、auto-merge(マージコミット)を有効にする。main の保護ルールで CI(Linux・Windows)の通過が必須。CI が落ちたら原因を直して push する。main の保護ルールで必須チェック `build & test (ubuntu-latest)`・`build & test (windows-latest)` を設定し、落ちる PR がマージされないことを確かめるまでは auto-merge を有効にしない。

## 7. コーディング規約
- 命名は .NET の標準(型・メソッド・プロパティは PascalCase、ローカル変数・引数は camelCase、非公開フィールドは _camelCase)。
- Nullable 有効。警告はエラー扱いで 0 件を保つ。
- 学習ルールの値は `LearningSettings` に置き、ロジックに直接書かない(v1.2)。固定ルールは DECISIONS.md の「固定ルール」だけ。
- 時刻は `TimeProvider` から受け取る。日付は日本時間(+9:00 固定、`JstClock`)。
- 1ファイル1責務を基本にする。詳しい規約は .claude/rules/ を参照。

## 8. データ・互換性
- Schema の変更は安易に行わない。既存データがある場合は「互換性確認 → バックアップ → マイグレーション → テスト」の順。
- 学習記録・設定のファイルの持ち主(Data Owner)は Persistence の ProgressRepository / SettingsRepository。Core はファイルを読み書きせず、値を受け取って結果を返す。
- 保存の手順・二重記録防止・破損時の復旧は .claude/rules/database.md に従う。

## 9. してはいけないこと
- `.env` や秘密情報を読む・出力する
- main ブランチへの直接 push、強制 push
- 確認なしでの NuGet パッケージの追加・削除
- 学習ルールの値をロジックに直接書く
- 保存ファイルが壊れているときに勝手に初期化する

## 10. 完了の条件
- `dotnet build -warnaserror` と `dotnet test` が全件通る。
- 起動 → 出題 → 保存 → 再起動 → 復元 が Windows 実機で確認できる(WPF 作成後)。
- 例外・エラーダイアログが出ていない。
- 変更内容と未解決事項を短く報告する。

## 11. 学びメモ(作業のたびに追記)
- 2026-11-13: ImmutableArray を Assert.Equal で比べると参照比較になる。.ToArray() か SequenceEqual で比べる(2回目で testing.md に昇格)。
- 2026-11-13: ファイル保存は Windows 非依存の Persistence に分けると、本物のファイルで Linux でもテストできる。失敗は IFileOperations の差し替えで起こす(権限は root で効かない)。
- 2026-11-13: クラウド環境でも NuGet に届けば .NET 10 SDK を入れて xUnit を実行できる(Core 84件通過を確認)。
- 2026-11-13: クラウドでは dotnet をまとめて動かすとメモリ不足で exit 137 になることがある。`-m:1 -p:UseSharedCompilation=false` でビルドし、テストは `--no-build` でプロジェクトごとに実行する。
- 2026-11-13: 足したテストが修正前のコードでも通ったら、上限や件数で問題が表に出ていないことを疑う(再確認の重複は10問では上限1で隠れ、20問で Red になった)。修正前のコードに一時的に戻して Red を確かめる。
- 2026-11-13: 再開できるかの判定は「列に今ある問題」だけでなく「これから足されうる問題(再練習)」まで見る。列に足される前の分を見落とすと、レビューで2往復かかった。
- 2026-11-13: ImmutableDictionary の列挙順はキーのハッシュで決まり、入れる順を変えても変わらない。並びの決め手(同点は ID 順など)は、同点の要素を多数並べて期待の順と比べて確かめる。
- 2026-11-14: 守りを二重にした箇所(コマンドの IsActive と ShellViewModel の「今の画面か」)は、片方を壊しても通るテストになりやすい。内側の守りは直接呼んで確かめる。
- 2026-11-14: WPF(net10.0-windows)の ImplicitUsings には System.IO が入らない。File・Path を使うファイルは `using System.IO;` を書く。
- 2026-11-14: XAML の誤りは実行するまで分からない。画面外に窓を置いて全画面を通し、PresentationTraceSources で結び付けの誤りを数えると、デスクトップ操作なしで確かめられる。
- 2026-11-13: 起動時の復旧が先にファイルを作り直すと、「保存の後に再起動して通知が無い」だけでは保存の成否が分からない。保存の後のファイルの中身と画面を直接確かめる。

## 12. 参照
- @docs/DECISIONS.md
- 仕様書(docs/spec/)は長いので、必要な章だけ読む
