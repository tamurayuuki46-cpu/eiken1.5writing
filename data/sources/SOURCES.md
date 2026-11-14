# 語彙リストの出典(取得したまま。改変しない)

取得日: 2026-11-14(日本時間)。配布元: https://www.newgeneralservicelist.com/ (Dr. Charles Browne ほか)

| ファイル | リスト・版 | 取得元 URL | 内容 | 件数(実測) | ライセンス |
|---|---|---|---|---|---|
| `NGSL_12_stats.csv` | New General Service List 1.2 | https://www.newgeneralservicelist.com/s/NGSL_12_stats.csv | 見出し語・SFI 順位・SFI・頻度 | 2,809 語(公式表記 2,809) | CC BY-SA 4.0 |
| `SUP_lemmatized.csv` | NGSL 1.2 補足(曜日・月・数など) | https://www.newgeneralservicelist.com/s/SUP_lemmatized.csv | 見出し語と変化形。順位なし | 52 語 | CC BY-SA 4.0 |
| `NAWL_12_lemmatized_for_research.csv` | New Academic Word List 1.2 | https://www.newgeneralservicelist.com/s/NAWL_12_lemmatized_for_research.csv | 見出し語と変化形。**順位なし**(アルファベット順) | 959 行(公式表記 957) | CC BY-SA 4.0 |
| `BSL_120_stats.csv` | Business Service List 1.2(ファイル名は 1.20) | https://www.newgeneralservicelist.com/s/BSL_120_stats.csv | 語・BSL 順位・Band・SFI・U(余分な空の列あり) | 1,744 語(公式表記 1,700) | CC BY-SA 4.0 |

## 作者とライセンス表記(公式ページの表記どおり)
- NGSL: Browne, C., Culligan, B., and Phillips, J. — Creative Commons Attribution-ShareAlike 4.0 International
- NAWL: Browne, C., Culligan, B., and Phillips, J. — Creative Commons Attribution-ShareAlike 4.0 International
- BSL: Browne, C. and Culligan, B. — Creative Commons Attribution-ShareAlike 4.0 International

## 注意
- 件数は公式ページの表記と少し違う(NAWL 959 行・BSL 1,744 語)。実測の値を使い、統合のレポートに両方を書く
- NAWL の頻度順位のファイルは公式サイトでは別ホスト(linguaeruditio.com)へのリンクで、今回は取得していない。NAWL だけに入っている語は頻度順位なし(Tier の中で最後・同順位は乱数)にする(design 4.2)
- NAWL のファイルは ISO-8859-1 で保存されている。読込はその文字コードで行う
