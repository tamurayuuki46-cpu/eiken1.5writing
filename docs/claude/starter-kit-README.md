# Claude Code スターターキット(CLI用)

新しいプロジェクトの直下にフォルダごとコピーして、`{ }` を埋めて使う雛形一式。
根拠は主軸の2冊(技評=『実践Claude Code入門』、SB=『Claude Code実践入門』)。
補助として『プロフェッショナルAI駆動開発』(駆動)付録Aを見本にしている。
2冊の要点に無い部分(ルールの `paths:` など)は、各ファイルに「一般仕様」「運用案」と注記している。

## 構成
```
CLAUDE.md                  プロジェクトの記憶・入口(技評)。毎回自動で読まれる
docs/                      永続文書。長く残す仕様・設計(技評)
.steering/_template/       タスク文書の雛形(技評)。タスクごとにコピー
  requirements.md / design.md / tasklist.md / decisions.md / blockers.md
.claude/
  settings.json            権限の deny(SB 制御の4層)
  rules/                   トピック別ルール(SB)。見本: 駆動 A.1.2〜A.1.6
    architecture.md / testing.md / review.md / logging.md / database.md
  agents/                  レビュー役。読み取り専用(SB)。見本: 駆動 A.1.7
    design-reviewer.md / edge-case-reviewer.md / security-reviewer.md
  skills/                  手順(SB)。見本: 駆動 A.1.8
    tdd/SKILL.md / create-pr/SKILL.md
prompts/8steps.md          8ステップの依頼文。見本: 駆動 A.4
```

## 位置付け
| | 範囲 | 寿命 | 読み込み |
|---|---|---|---|
| CLAUDE.md | 全体 | ずっと | 毎回自動 |
| rules/ | トピック | ずっと | 自動(paths: 指定なら該当時のみ) |
| .steering/ | 1タスク | タスク完了まで | 場所を指定して読ませる |
| docs/ | 全体 | ずっと | 必要時に @ で参照 |
| agents/ ・ skills/ | 役割・手順 | ずっと | 説明(description)を見てClaudeが呼ぶ |

内容の流れは一方向: **.steering/ → docs/(長く残す仕様)・rules/(毎回守ること)**。CLAUDE.md には入口だけを書く。

## はじめ方
1. フォルダの中身をプロジェクト直下にコピーする(`.claude` と `.steering` は隠しフォルダなので注意)。
2. CLAUDE.md の `{ }` を埋め、使わない節を消す。目安は約200行以内(SB)。
3. `.claude/settings.json` の deny をプロジェクトに合わせる。
4. rules/ を見直す。使わないもの(例: DBが無いなら database.md)は消す。
5. `claude` を起動し、prompts/8steps.md の ① から始める。

## 運用のルール
- フェーズが変わったら `/clear` か `/compact`(技評 課題1)。
- 同じ指摘が2回出たら rules/ に1行追加(駆動 A.5)。
- タスク後は tasklist.md の「片付け」を必ず実行する。
- 品質が揺れたら X(文書)→ Y(テスト)→ F(モデル・仕組み)の順に疑う(駆動 A.5.4)。

## 注意
- settings.json の deny 書式は一般的な例。導入前に `/permissions` などで効いているか確認する。
- create-pr スキルは GitHub CLI(gh)が使える前提。
- 3体レビューは文脈とトークンを使う。数行の修正では呼ばない。
