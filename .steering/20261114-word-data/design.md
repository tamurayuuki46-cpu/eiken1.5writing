# design.md — 約5,000語の単語データ

<!-- 置き場所: .steering/20261114-word-data/design.md
     状態: AI の暫定案。requirements の Q1〜Q4 の回答を受けて確定する -->

## 1. 方針
- 単語データは「元の CSV + Claude が書いた付加データ(訳・Tier 1・除外・手動の穴あき)」から道具で生成する。生成物 words.json は手で直さない
  (直すときは付加データを直して作り直す)。こうすると、元リストの更新や訳の修正をやり直せる
- 穴あきの生成と検証は Core の Domain に置く(既存の WordSetValidator.ValidateBlanks と同じ場所。Linux でテスト)
- 訳は Claude が書く。数百語ずつのファイルに分け、1ファイルごとに検証(空・重複・不正文字)してから次へ進む

## 2. 置き場所
| 場所 | 内容 |
|---|---|
| `data/sources/NGSL_1.2_stats.csv` ほか | 公式 CSV(取得したまま。改変しない) |
| `data/sources/SOURCES.md` | 版・URL・取得日・ライセンス・作者 |
| `data/curation/ja/*.tsv` | 日本語訳(`word<TAB>訳`)。Claude が自作。数百語ずつ |
| `data/curation/tier1.txt` | Tier 1 の語(1行1語) |
| `data/curation/excluded.tsv` | 除外する語と理由 |
| `data/curation/blanks-manual.tsv` | 手動の穴あき(仮データ18語の手動指定を引き継ぐ) |
| `data/LICENSE-DATA.md` | 単語データのライセンス(CC BY-SA 4.0)と出典表示 |
| `src/EikenSpelling.Core/Domain/BlankGenerator.cs` | 穴あきの自動生成(新規) |
| `tools/EikenSpelling.WordData/` | 生成の道具(net10.0 のコンソール。Core を参照) |
| `tests/EikenSpelling.WordData.Tests/` または既存テストへの追加 | 統合・生成・検証・性能のテスト |
| `src/EikenSpelling.Wpf/Data/words.json` | 生成物(置き換え) |
| `data/report.md` | 検証レポート(生成物) |

## 3. データ
- words.json の形は既存のまま(schemaVersion 1)。`wordSetVersion` は `2026.11-5k-1` のように版が分かる値、`sourceVersion` は `NGSL-1.2_NAWL-1.2_BSL-1.2`
- 単語 ID = 正規化した綴り(既存の仮データと同じ)。仮データの18語は同じ ID で残る(学習記録を保つ)
- Schema 変更なし

## 4. 処理の流れ(道具)
1. 読む: 3つの CSV(列名で読む。BSL の余分な列は無視)
2. 正規化と統合(4.1)
3. 頻度順位(4.2)
4. 除外(Q1)
5. 付加データを重ねる: 訳・Tier 1・手動の穴あき
6. 穴あきの自動生成(4.3)
7. 日本語モードの可否: 訳が空 → 検証エラー、同じ訳の語 → japaneseEligible = false(仕様 v1.1 6.1)
8. WordSetValidator で検証し、エラーがあれば生成をやめる
9. words.json とレポートを書く

### 4.1 正規化と統合
- 小文字・前後の空白を除く。英字以外(ハイフン・空白・アポストロフィ)を含む語は除外候補にしてレポートへ(Core の正誤判定は途中の空白を区別するため)
- 同じ綴りは1件に。sources は NGSL → NAWL → BSL の順で重複なし

### 4.2 頻度順位
- NGSL の SFI Rank をそのまま使う。NGSL に無い語は「NGSL の最大順位 + そのリストの順位」(NAWL → BSL の順)。元の順位は report に残す
- NAWL の順位のファイルは公式サイトでは別ホストへのリンク。取れなければ研究用の lemma CSV の並び順を使い、その旨を SOURCES.md に書く

### 4.3 穴あきの自動生成(BlankGenerator)
- 入力: 綴り。出力: 穴あき表示(`_` で隠す)と、使ったルール名
- ルール(上から順に試し、最初に当たったもの。仕様 v1.1 5.2):
  1. 二重子音(cc, ll, mm, nn, rr, ss, tt など): その2文字を隠す(例 committee → co__i__ee のように複数あれば最大2か所)
  2. -able / -ible, -ance / -ence, -ant / -ent: 母音の1文字を隠す
  3. ie / ei: 2文字を隠す
  4. 黙字の代表(kn-, -mb, wr-, -gh-, ps- など): その文字を隠す
  5. 汎用: 語の中ほどの母音を1〜2文字隠す(先頭と末尾の文字は隠さない)
- 制約: 長さは同じ・穴は1つ以上・隠すのは全体の半分まで・3文字以下の語は1文字だけ隠す
- 検証は既存の `WordSetValidator.ValidateBlanks` を使う(生成物も同じ検査を通す)

## 5. テスト方針
- Core.Tests: BlankGenerator のルールごとの例、制約(長さ・穴の数・半分まで・短い語)、生成物が ValidateBlanks を通る、手動優先は道具側
- 道具のテスト: 小さな CSV で統合(重複・出典の配列・頻度順位・除外・英字以外)、訳の欠け・同じ訳の扱い、生成の再現性(同じ入力 → 同じ出力)
- 生成物のテスト(Presentation.Tests の BundledWordsTests を拡張): 語数の範囲、検証エラー 0、仮データの18語の ID がある、全語に訳と穴あき、Tier の件数の範囲
- 性能: 5,000語の WordSetLoader.Parse + Validate + QuestionSelector.Select が 1 秒以内

## 6. 作業の順(tasklist の案)
1. CSV の取得と SOURCES.md・LICENSE-DATA.md
2. BlankGenerator(TDD)
3. 道具: 読込・統合・頻度順位・除外(TDD)
4. 除外の一覧と Tier 1 の一覧を作る → ユーザーに確認
5. 訳を数百語ずつ作る(各ファイルを検証)→ 抜き取り 100 語をユーザーに確認
6. words.json とレポートを生成、生成物のテスト、性能のテスト
7. 4体レビュー → PR

## 7. リスク
- 訳の誤り: 抜き取り確認と、同じ訳の検出で減らす。完全にはなくせない(レポートに「自作の訳」と明記)
- 作業量: 約5,000語の訳は大きい。数百語ずつ区切り、途中で止めても再開できるよう tasklist に進み具合を残す
- ライセンス: 単語データは CC BY-SA 4.0 の改変物として同じライセンスで公開する。アプリのコードは別(仕様 v1.1 3.3)
- BSL の作者表記(Browne & Culligan)は NGSL・NAWL(Browne, Culligan & Phillips)と異なる。ページの表記どおりに書く
