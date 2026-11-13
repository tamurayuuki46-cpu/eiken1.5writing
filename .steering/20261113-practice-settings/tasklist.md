# tasklist.md — 練習設定の保存

## 状態
- フェーズ: 検証(⑥)
- 最終更新: 2026-11-13
- 次にやること: ⑥ 3体レビュー

## タスク
### 準備
- [x] requirements.md を確認してもらう
- [x] design.md を確認してもらう(【要確認】1・2 は案で確定)
- [x] ② プランのレビュー(design: P1 2・P2 7 / security: 通過・P2 6)— P1 をすべて反映、P2 も大半を反映
- [x] ③ テスト計画のレビュー(edge-case: P1 11・P2 4)— P1 をテスト方針に反映

### 実装(tdd スキル)
- [x] T0: SettingsRepository.CanSave・Status
- [x] T1: PracticeChoiceApplier.FromSettings
- [x] T2: 起動で SettingsRepository と読込の状態を持つ、Defaults
- [x] T3: SettingsScreen と操作(SettingsEditor.cs)

### 検証
- [x] `dotnet build -warnaserror` と `dotnet test` が全件通る(Core 499 / Flow 377 / Persistence 83。Red を確認してから実装。失敗で今の設定を変えない・保存できないときは書かないは実装を壊して Red を確認)
- [x] 受入条件(requirements.md 5.)を1つずつ確認(すべて対応するテストあり)
- [x] ⑥ 3体レビュー 1往復目(design・security: 通過 / edge: P1 1 = 復旧した設定の保存のテストが保存失敗でも通る → 修正、保存しない実装で Red を確認)。P2 の多くも反映
- [ ] ⑥ 2往復目(edge のみ)

### 片付け
- [ ] P2(⑥): 壊れた設定の上で退避後に置き換え(Move)だけ失敗すると、再保存で自分の tmp が orphan 名で残る / 保存先フォルダを作れないときの CanSave のテスト(FaultyFileOperations の CreateDirectory は失敗を起こせない)/ 読込後に外から新しい版の settings.json を置かれると上書きする(アプリは1つだけ動く前提)/ PracticeFlow.cs 295 行: 次に操作を足すときは苦手一覧の操作を partial に分ける / FromSettings の単体テストは internal のため結合テストで代えている
- [ ] 報告(人の判断): DECISIONS「起動だけでは何も書かない」と、バックアップからの復旧で起動時に書くこと(以前からの動き)の食い違い
- [x] 長く残す決定を docs/DECISIONS.md へ(「学習設定」に保存の扱い)
- [x] 学びを CLAUDE.md の「学びメモ」に1行追記

## メモ
- 2026-11-13: 退避名は `settings.json.corrupt-<日時>.json`(元の名前に拡張子まで残る)。テストで最初に名前を誤った
- 2026-11-13: PracticeFlow は partial にし、設定画面の操作は PracticeFlow.Settings.cs、中身は SettingsEditor へ(PracticeFlow.cs は 295 行のまま)
- 2026-11-13: ユーザー決定 — 設定画面で保存したときだけ書く / 初期値に戻すを入れる / 保存できないときは設定画面に留めて理由を出す
