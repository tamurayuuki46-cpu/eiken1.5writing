# 単語データのライセンス

このリポジトリの単語データ(`src/EikenSpelling.Wpf/Data/words.json`、`data/curation/` の付加データ、`data/report.md`)は、
次の語彙リストをもとに作った改変物です。元のリストのライセンスに従い、単語データも
**Creative Commons Attribution-ShareAlike 4.0 International(CC BY-SA 4.0)** で公開します。
ライセンスの全文: https://creativecommons.org/licenses/by-sa/4.0/

## 元にした語彙リスト
- New General Service List 1.2(補足リストを含む) — Browne, C., Culligan, B., and Phillips, J. — CC BY-SA 4.0
- New Academic Word List 1.2 — Browne, C., Culligan, B., and Phillips, J. — CC BY-SA 4.0
- Business Service List 1.2 — Browne, C. and Culligan, B. — CC BY-SA 4.0

配布元: https://www.newgeneralservicelist.com/ (取得したファイルと日付は `data/sources/SOURCES.md`)

## このプロジェクトで加えた変更
- 3つのリストを統合し、重複をまとめ、出典を記録した
- スペル練習に向かない語(機能語・略語など)を除いた
- 日本語訳・出題の優先度(Tier)・穴あき表示を加えた(日本語訳はこのプロジェクトの自作)

## アプリのコード
アプリのソースコード(`src/`・`tests/`・`tools/`)は単語データとは別に扱います(仕様 v1.1 3.3)。
