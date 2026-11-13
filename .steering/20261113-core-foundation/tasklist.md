# tasklist.md — Core の土台(後追い記録)

## 状態
- フェーズ: 検証(3体レビューの P1 を修正済み。163/163 通過)
- 最終更新: 2026-11-13
- 次にやること: 修正のコミット・push。次のタスク(学習記録・苦手/克服判定・再確認スケジュール)の ① プラン

## タスク
- [x] T1: 単語データモデル・読込・検証(仕様 19章 1〜2)
- [x] T2: 正誤判定・差分計算(19章 3〜4)
- [x] T3: 日本時間の日付処理(TimeProvider)
- [x] T4: 学習設定(初期値・検証・JSON 変換)(v1.2)
- [x] T5: 出題枠の計算(新規/苦手/再確認、モード配分)
- [x] T6: xUnit を実行して全件通過を確認(84/84、2026-11-13)
- [x] T7: main(スターターキット入り)を merge して取り込む(強制 push を避けるため rebase ではなく merge)
- [x] T8: 3体レビュー(P0 0件 / P1 7件。2026-11-13 に報告し、修正の承認を得た)
- [x] T9(P1-1・P1-2): JSON の null と壊れた JSON を InvalidDataException にそろえる
  - 設定: `"practice": null` / `"modeWeights": null` / `"questionCountChoices": null` / `"reviewLimits": null` / `"reviewLimits": [null]` / `"enabledSources": ["NGSL", null]` / `null` だけ / 空文字 / 型違い `"ten"` / 桁あふれ 2147483648 / 未知の enum `"Foo"`
  - 単語: 壊れた JSON(Parse・LoadAsync)/ 必須の id が無い / `"words": null` / `"words": [null]` / `"id": null` / `"sources": null`
- [x] T10(P1-3): 設定値に上限を付ける(問題数と選択肢 1〜100、重み 0〜100、再練習 0〜3、フラッシュ 0.1〜10秒、再確認間隔 1〜3650日)。前後の値(n-1 / n / n+1)で確認。重み20億でも例外にならない
- [x] T11(P1-4): 定義外の enum 値(`"manualMode": 99` など)を検証で指摘する(manualMode / retryTiming / ordering)
- [x] T12(P1-7): 未テストの検証ルール(新しい settingsVersion、reviewLimits 空・UpTo 0・Limit -1、weakWordScore の負数、手動モードが使えるときは指摘なし)
- [x] T13(P1-7): 出題枠の境界(PlanSlots 0/1/6/11/21/-1、AllocateModes 0/1/-1、巨大な重みでも合計が問題数になる)
- [x] T14(P1-5・P1-6): DECISIONS.md に JSON の形・null の扱い・上限・Domain→Settings 参照可 を追記。architecture.md(PR #2)にも1行

## 片付け
- [ ] P2(3体レビュー): JSON オプションの重複・可変 / 全角・特殊文字の判定方針 / 同点時の穴あき→フラッシュのテスト / Manual なのに Blanks が null / 設定スナップショットの不変化 / Tier 別の優先順位 / 苦手度の式が v1.1 で FA でない件 / SpellingDiffService の到達しない分岐 / 検証テストを Assert.Empty でも確認
- [ ] P2: Microsoft.NET.Test.Sdk 17.14.1 が古いという案内が出る(テストは通る)。更新するかを確認する
- [ ] 長く残す決定が docs/DECISIONS.md にそろっているか確認する

## 学び
- 2026-11-13: 実装を先に進めたので、要件・設計は後追いで記録した。次のタスクからは 8steps の ① から始める
