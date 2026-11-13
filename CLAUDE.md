# CLAUDE.md(雛形・Claude Code CLI用)

<!--
置き場所(CLI)
- プロジェクト用: リポジトリ直下の CLAUDE.md(Gitにコミットしてチームで共有)
- 個人共通: ~/.claude/CLAUDE.md(すべてのプロジェクトで読まれる)
- トピック別ルール: .claude/rules/*.md
- 設定の優先順位: managed policy > local > project > user(SB 第2章)
- 一から作るなら `claude` 起動後に /init で下書きを作り、この雛形の節を移す(SB 第2章)
- `claude -p`(ヘッドレス実行)でもCLAUDE.mdは読まれる(SB 第5章)

3つの文書の位置付け
| | CLAUDE.md | ルール(.claude/rules/) | ステアリング(.steering/) |
|---|---|---|---|
| 位置付け | プロジェクトの記憶・入口(技評) | CLAUDE.mdの分冊(SB) | 1タスクの作業記録(技評) |
| 効く範囲 | 全体 | 特定トピック | 今の1タスク |
| 寿命 | 恒久 | 恒久 | タスク完了まで |
| 読み込み | 毎回自動 | 自動 | 自動では読まれない。場所を指定して読ませる |
| 書くこと | 概要・コマンド・進め方・禁止 | 規約の詳細(コード・テスト・Git) | 要件・設計・手順と進捗・決定 |
- CLAUDE.md=目次と憲法。詳細はルールへ、進行中の作業はステアリングへ振り分ける。
- 流れは「ステアリング → ルール / docs」の一方向。タスク中の決定のうち、毎回守るものはルールへ、長く残す仕様は docs/ へ移す。
- 迷ったら:「次のタスクでも守らせたい?」→ CLAUDE.md かルール/「この作業の間だけ?」→ ステアリング。
- ※ルールの自動読み込みとステアリングの運用(読ませ方・移し方)は2冊の要点に記載なし。一般仕様・運用案として書いている。

使い方
- { } を埋め、使わない節は削除する。目安は約200行以内(SB 第2章)。長くなったら .claude/rules/ に分ける。
- ここには「毎回守ること」だけを書く。手順の詳細はスキル、禁止の強制は settings.json の deny / hooks に任せる(技評 課題2、SB 制御の4層)。
- 作業が終わるたびに「今回の学びをCLAUDE.mdに反映して」と頼み、育てる(自動化 6章・補助)。
- このコメントブロックは埋め終わったら消してよい。
-->

## 1. プロジェクト概要
- 名前: {アプリ名}
- 目的: {何のためのアプリか / 何の問題を解決するか}
- 利用者: {誰が使うか}
- Core Value: {一番大事な価値を1行で}
- やらないこと: {範囲外にするもの}
- 現在のフェーズ: {企画 / MVP / 設計 / 実装 / 検証 / Stable}

## 2. 技術スタック
- 言語・フレームワーク: {例: TypeScript / React}
- 保存先・DB: {例: IndexedDB / Supabase}
- 実行環境: {例: PWA, モバイル優先}
- パッケージ管理: {例: pnpm}

## 3. よく使うコマンド
```bash
{install}    # 依存の導入
{dev}        # 開発サーバー
{build}      # ビルド
{lint}       # Lint
{typecheck}  # 型チェック
{test}       # テスト
```
- 作業の完了前に必ず {lint} / {typecheck} / {test} を通す。

## 4. ディレクトリ構成
```
{src/}        # アプリ本体
{tests/}      # テスト
docs/                 # 永続文書(長く残す設計・仕様)
.steering/_template/  # タスク文書の雛形。タスクごとに .steering/[日付]-[タスク]/ へコピー
.claude/rules/        # トピック別ルール(architecture / testing / review / logging / database)
.claude/agents/       # レビュー役(design / edge-case / security)
.claude/skills/       # 手順(tdd / create-pr)
.claude/settings.json # 権限(deny)
prompts/8steps.md     # 8ステップの依頼文
```

## 5. 文書の置き場所(技評 SDD・文書2種)
- 永続文書は docs/ に置く: {product-requirements.md, architecture.md, data-model.md など}
- タスクごとの文書は .steering/[YYYYMMDD]-[タスク名]/ に置く
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

## 7. コーディング規約
- {命名規則}
- {フォーマッタ・Lintの設定に従う}
- 1ファイル1責務を基本にする。
- 詳しい規約は .claude/rules/ を参照(architecture.md / testing.md など)。

## 8. データ・互換性
- Schema の変更は安易に行わない。既存データがある場合は「互換性確認 → バックアップ → マイグレーション → テスト」の順。
- データの持ち主(Data Owner)は {モジュール名}。他から直接書き換えない。

## 9. してはいけないこと
- {.env や秘密情報を読む・出力する}
- {main ブランチへの直接 push}
- {確認なしでの依存追加・削除}
<!-- 本当に止めたい操作は、ここに書くだけでなく .claude/settings.json の permissions.deny か PreToolUse フックで強制する(技評 課題2、SB 制御の4層) -->

## 10. 完了の条件
- ビルド・Lint・型チェック・テストが通る。
- {起動 → 入力 → 保存 → リロード → 復元} が手動で確認できる。
- コンソールエラーが出ていない。
- 変更内容と未解決事項を短く報告する。

## 11. 学びメモ(作業のたびに追記)
<!-- つまずき・エラーと解決策・効率的なやり方を1行ずつ。長くなったら .claude/rules/ へ移す -->
- {YYYY-MM-DD}: {学び}

## 12. 参照
- @docs/{product-requirements.md}
- @{AGENTS.md}  <!-- AGENTS.md は自動では読まれないので、使う場合はここで取り込む(SB 第2章) -->
