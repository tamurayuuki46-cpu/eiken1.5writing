# 単語データの検証レポート

道具 `tools/EikenSpelling.WordData` が自動で書く(手で直さない)。出典は `data/sources/SOURCES.md`。

## 語数
- NGSL 1.2: 2809
- NGSL 1.2 補足: 52
- NAWL 1.2: 959
- BSL 1.2: 1744
- 統合後(重複をまとめた後): 5046
- 収録: **4922**(Tier 1: 696 / Tier 2: 2107 / Tier 3: 2119)
- 日本語モード可: 4922

## 検証エラー: 0

## 穴あきの作り方の内訳
- DoubleConsonant: 639
- Fallback: 19
- IeEi: 72
- Manual: 18
- MiddleVowel: 3650
- Short: 172
- SilentLetter: 64
- Suffix: 288

## 訳がまだ無い語: 0
なし

## 同じ訳の語(日本語モード不可): 0 組

## 除外: 124
- 1〜2文字(24): a, ad, as, at, be, by, do, ex, go, he, hi, i, if, in, it, no, of, on, or, pi, so, to, up, we
- be・have・do(1): have
- 代名詞(27): another, anyone, anything, either, everyone, everything, herself, himself, itself, myself, neither, nobody, none, nothing, ourselves, she, someone, something, themselves, they, what, whatever, which, who, whoever, you, yourself
- 別綴り(advisor を残す)(1): adviser
- 別綴り(among を残す)(1): amongst
- 別綴り(while を残す)(1): whilst
- 助動詞(8): can, could, might, must, shall, should, will, would
- 口語・間投詞(6): alas, alright, damn, okay, yeah, yes
- 接頭辞(単独の語ではない)(9): anti, con, mid, neo, non, pre, pro, semi, sub
- 敬称・略語(2): info, mister
- 機能語(4文字以下)(44): all, also, and, any, both, but, each, even, ever, few, for, from, here, how, into, just, many, more, most, much, nor, not, now, off, only, onto, out, over, per, some, such, than, that, the, then, this, too, upon, very, via, when, why, with, yet

## 統合しなかった語(英字以外を含む): 1
- e-book(BSL)

## 付加データにあるが元リストに無い語: 0
なし
