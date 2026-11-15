# 設計

## Core
- フラッシュの表示開始の印 `PracticeSession.FlashShownStep`(int?)。値は表示を始めたときの「処理した数」= Answers.Length + Skips.Length(提示ごとに一意。後回しで同じ問題がもう一度出ても別の値)
- `SessionManager.MarkFlashShown` / `IsFlashShown`(今の問題がフラッシュで、印が今の処理数と同じ)
- 検査: null か 0 ≦ 値 ≦ 今の処理数
- 保存形式: schemaVersion 2 のまま項目を足す(任意項目。2026-11-15 版のアプリは知らない項目として無視し、最悪でも再起動後に単語を1回再表示するだけ。学習記録は壊れない)

## Flow
- `StartRecommended()`: 保存済みの問題数・自動ミックス ON・苦手のみ OFF(手動モード・フラッシュ秒数は保存済みの値)。判断は Flow に置く
- QuestionScreen に `Flash`(NotStarted / Showing / Hidden)
  - `ShowFlash()`: NotStarted のときだけ。印を付けた候補を保存してから Showing(書いてから見せる)。失敗したら同じ問題で保存待ち(後回しと同じ仕組み)、RetrySave で同じ候補を保存
  - `EndFlash()`: Showing → Hidden(書かない。印は保存済み)
  - 読込・再開: 印が今の提示なら Hidden、無ければ NotStarted
  - フラッシュの NotStarted・Showing では回答・後回しを受け付けない(中断はできる)
- 保存待ちを後回し専用から一般化(`SaveFailure` / `SavePending`。文言は種類ごと)

## Presentation
- 差し替え口: `ISpeechPlayer`(IsAvailable / Speak(text, onFailed) / Stop)、`IUiScheduler`(Schedule(delay, action) → IDisposable)。ShellOptions に任意で渡す(無ければ何もしない実装)
- ScreenViewModel: 受け付け開始の初回と Deactivate のフック
- QuestionViewModel
  - 音声: 案内文、文字数なし、受け付け開始で1回 Speak、ReplayCommand(Ctrl+Space)、Deactivate で Stop、失敗で帯と warn ログ(単語は書かない)
  - フラッシュ: NotStarted なら受け付け開始で ShowFlash。Showing なら単語を出し FlashSeconds 後に EndFlash。Hidden なら入力欄
- HomeViewModel: RecommendedCommand、FreeChoice の展開・閉じる、フラッシュ秒数(フラッシュが出うるときだけ)、音声が使えない理由

## WPF
- NuGet System.Speech(承認済み)を Wpf にだけ。`SystemSpeechPlayer`(en-* の声、SpeakAsync、失敗は UI スレッドへ)、`DispatcherScheduler`
- App: 音声が使えれば Audio、フラッシュは常に、を端末のモードに
- HomeView: 2つの同じ大きさのボタン、自由選択のパネル。QuestionView: 音声・フラッシュの表示
