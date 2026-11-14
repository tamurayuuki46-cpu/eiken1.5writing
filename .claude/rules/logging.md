---
paths:
  - "src/EikenSpelling.Wpf/**"
  - "src/EikenSpelling.Presentation/**"
---
<!-- Core・Flow はログを出さず、結果(成功・失敗・警告)と Diagnostic を値で返す。ログは Presentation(FileLog)と Wpf で扱う -->
<!-- 書かないもの(必ず守る): ファイルのパス、誤答入力、画面の状態・ViewModel の丸ごと、DiffDisplay の文章、SpellingDiff(ToString に入力の文字が入る)、例外の Message とスタックトレース(型名・HResult・メソッド名だけ) -->

# ログルール

## 形式
- 構造化ログにする(キーと値)。最低限: 時刻 / レベル / 処理名 / sessionId(あれば answerId)
- レベル: error=要対応 / warn=想定外だが継続 / info=主要な処理の区切り / debug=開発時のみ

## 書かないもの
- 回答内容の全文を大量に残さない。個人情報(ユーザー名を含むパスなど)は必要最小限にする。

## 書く場所
- エラーは握りつぶさない。捕まえたら、原因が分かる情報と一緒に error で出し、画面にも伝える(保存失敗・復旧)。
- 調査用の一時ログは、作業が終わったら消す。
