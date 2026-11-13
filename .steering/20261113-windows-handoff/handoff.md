# Windows CLI への引き継ぎ資料(EikenSpelling / eiken1.5writing)

- 作成: 2026-11-13(クラウドセッション `session_011gTAdLNQRcXkUHxUpcRwfB`)
- 基準: main = `bc70ca2`(PR #11 のマージ)。作業ブランチ `ccr-37466a37-vb1yx9` = `fdec73a`(内容はすべて main に入っている)
- 今回は調査のみ。コード・仕様・Schema は変更していない

---

## 1. 現状確認

### 1.1 Git・PR(実際に確認した事実)
| 項目 | 結果 |
|---|---|
| 作業ブランチ | `ccr-37466a37-vb1yx9`。origin と一致(push 済み)。未コミット変更・stash なし |
| main との関係 | 作業ブランチの全コミットは main に入っている(`git merge-base --is-ancestor` で確認) |
| 未マージ PR | なし(PR #8〜#11 はマージ済み) |
| 残っているリモートブランチ | `feature/*`・`ci/github-actions`・`chore/add-claude-starter-kit`・`claude/eiken-spelling-handover-pxxiv0` など。すべて中身は main に入っている(削除してよいが、今回は触っていない) |

### 1.2 実装状況(main の最新で確認)
| プロジェクト | 規模 | 状態 |
|---|---|---|
| `EikenSpelling.Core`(net10.0) | 38 ファイル・約 2,700 行 | 単語データ・判定・差分・学習記録・苦手/克服・再確認・出題選択・セッション・集計・苦手一覧・学習状況 |
| `EikenSpelling.Persistence`(net10.0) | 5 ファイル・約 760 行 | progress.json / settings.json の安全な保存・読込・バックアップ復旧・CanSave |
| `EikenSpelling.Flow`(net10.0) | 9 ファイル・約 760 行 | 画面の手前の進行役。起動・再開・練習・保存失敗・破損・苦手一覧・設定画面の「画面の状態」と操作 |
| `EikenSpelling.Wpf` | 無い | 未着手 |

### 1.3 ビルド・テスト(main の最新、Linux で実行した結果)
- `dotnet build -warnaserror`: 警告 0・エラー 0(.NET SDK 10.0.404)
- `dotnet test`: Core 499 / Flow 378 / Persistence 83 = **960 件すべて通過**
- GitHub Actions(`.github/workflows/ci.yml`)が PR ごとに ubuntu-latest と windows-latest の両方で build と test を実行し、PR #8〜#11 はどちらも通過している(= Windows でのビルド・テストは CI で確認済み)

### 1.4 文書と実装の食い違い(報告のみ。直していない)
| # | 場所 | 食い違い | 重さ |
|---|---|---|---|
| D1 | 依頼の前提 ↔ 仕様書 | 依頼では「UI 技術は未決定」。しかし仕様 v1.0 15.1 は UI = **WPF**(音声 System.Speech)と決めており、v1.0 の「Overengineering」で「MAUI/Avalonia への移行」を v0.1 では採用しないと明記。CLAUDE.md・README・architecture.md も WPF 前提 | **解消(2026-11-13: ユーザーが WPF に決定。DECISIONS「UI 技術」)** |
| D2 | README.md | 古い。構成に Flow・Flow.Tests が無い / 実装状況が「苦手度の計算は保留」「セッションの保存は未着手」のまま(どちらも完了)/ 苦手一覧・設定保存が無い / ビルド例が `dotnet build src/EikenSpelling.Core`(全体は `dotnet build -warnaserror`)/「スマホ(クラウド)= Core」の分担 | 中 |
| D3 | `.steering/*/tasklist.md` | 8 タスクすべての「次にやること」が「⑦ PR を作る」などのまま(実際はすべてマージ済み)。未チェック項目は P2 の積み残し | 低 |
| D4 | `docs/README.md` | product-requirements.md・architecture.md・data-model.md・glossary.md を「置く例」に挙げるが、どれも存在しない(雛形の文言) | 低 |
| D5 | CLAUDE.md 6章 | 「P0・P1 が無くなったら auto-merge を有効にする」とあるが、前提の「main の保護ルールで必須チェックを確認」が未確認のため、これまで有効にしていない(人がマージしている) | 低 |
| D6 | 仕様 v1.0 2.2 ↔ DECISIONS | 仕様は「初期収録 100 語を同梱」、DECISIONS は「約 5,000 語(NGSL/NAWL/BSL)」。同梱の単語データはまだ無く、日本語訳の辞書のライセンスも未確定 | 中(UI の動作確認に要る) |
| D7 | `.claude/rules/testing.md` | 「フレームワークは xUnit(`tests/EikenSpelling.Core.Tests/`)」と Core だけを挙げる(Persistence・Flow のテストもある) | 低 |
| D8 | テストの依存 | `Microsoft.NET.Test.Sdk 17.14.1` が古いという案内が出る(テストは通る。更新は NuGet の変更なので要確認) | 低 |

---

## 2. Teleport(クラウド → Windows CLI)

### 2.1 公式記載(出典: https://code.claude.com/docs/en/claude-code-on-the-web.md)
- 対象: claude.ai/code などのクラウドセッション。**同じ claude.ai アカウント**でログインしていること(API キー・Bedrock/Vertex では不可。`claude auth login` で claude.ai にサインイン)。組織ポリシー `allow_remote_sessions` が有効なこと
- 使い方: `claude --teleport`(対話で選ぶ)/ `claude --teleport <session-id>` / セッション内で `/teleport` または `/tp` / `/tasks` で選んで `t` / claude.ai/code のメニュー「Open in > Terminal」でコマンドをコピー
- 条件: **同じリポジトリの checkout の中**で実行する(fork 不可)/ **作業ツリーがクリーン**(未コミット変更があると stash を促される)/ クラウドのブランチが **remote に push 済み**
- 引き継がれるもの: 会話履歴(自動圧縮された部分は要約から続く)とブランチ(fetch して checkout)
- 引き継ぎ後: 元のクラウドセッションは残る。ターミナル側の新しい作業はクラウドの画面には出ない。スマホからも続けるならローカルで `/remote-control`
- `claude --resume` はローカルの履歴だけで、クラウドセッションは出ない
- 逆向き(ローカル → クラウド): `claude --cloud "作業内容"`(ネイティブ Windows では未コミットの追跡ファイルがそのまま upload される点に注意)

### 2.2 このセッションで確認した事実
| 確認事項 | 結果 |
|---|---|
| 1. Teleport の対象か | クラウドセッション(`environment_kind: anthropic_cloud`)。ID `session_011gTAdLNQRcXkUHxUpcRwfB`。公式の条件は満たしていると見られる。ただし実際に Teleport できるかは PC 側で実行するまで**未確認**(プラン・組織ポリシーはこちらから見えない) |
| 2. 引き継ぎ範囲 | ブランチ `ccr-37466a37-vb1yx9` は push 済み。会話はとても長く(文脈の使用 約 68 万トークン)、引き継ぐと要約から続く部分が多い |
| 3. push の要否 | 必要。**push 済み**。未 push のコミット・未コミット変更は無い |
| 4. PC 側の準備 | 下の 3章 |
| 5. 使い方 | `cd <clone先>` → `git status`(クリーンを確認)→ `claude --teleport session_011gTAdLNQRcXkUHxUpcRwfB` |
| 6. 注意点 | コンテナ内に入れた .NET SDK(`~/.dotnet`)は引き継がれない(PC に自分で入れる)/ クラウドとローカルで同じブランチを同時に編集しない |

### 2.3 推奨: Teleport より「main から新しいセッション」
- 作業ブランチの内容はすべて main に入っているので、**会話履歴以外に Teleport で運ぶものが無い**
- 会話は長く、引き継いでも要約が中心。必要な文脈は CLAUDE.md・DECISIONS.md・`.steering/`・この資料にそろっている
- そのため、PC で main を clone し、新しいブランチで**新しいセッション**を始め、下の開始用プロンプトを渡すほうが軽く確実
- 会話の経緯も持っていきたい場合だけ `claude --teleport session_011gTAdLNQRcXkUHxUpcRwfB` を使う(その場合も作業は新しいブランチで始める)

---

## 3. Windows 開発環境

| 対象 | 必要なもの | 確認コマンド(PowerShell) |
|---|---|---|
| Claude Code CLI | 公式のネイティブ版(手順は 3.1)、claude.ai アカウントでログイン | `claude --version` / `claude doctor` |
| Git | Git for Windows | `git --version` |
| GitHub | リポジトリへのアクセス(HTTPS + 資格情報マネージャ、または `gh`) | `git ls-remote https://github.com/tamurayuuki46-cpu/eiken1.5writing` |
| .NET 10 SDK | 10.0.x(クラウドは 10.0.404 で確認) | `dotnet --list-sdks` |
| WPF | .NET SDK に含まれる(Windows Desktop ワークロード) | `dotnet new list wpf`(テンプレートがあること) |
| Avalonia(採用する場合だけ) | NuGet パッケージとテンプレート(追加は要承認) | ― |
| エディタ(任意) | Visual Studio 2026 / VS Code / Rider | ― |

### 3.1 Claude Code CLI(Windows)— 公式記載(https://code.claude.com/docs/en/setup.md)
- 要件: Windows 10 1809 以降。プランは Pro / Max / Ultra / Team / Enterprise(無料プランは不可)。管理者権限は不要
- インストール(推奨のネイティブ版、PowerShell): `irm https://claude.ai/install.ps1 | iex`(自動更新される)
  - CMD 用のコマンドを PowerShell に貼らない(`&&` のエラーになる)。別の方法: `winget install Anthropic.ClaudeCode`(自動更新なし)
- 確認: 新しいターミナルで `claude --version`、`claude doctor`
- ログイン: 初回の `claude` でブラウザの案内に従う(Teleport には claude.ai アカウントが要る。API キーでは不可)。やり直しは `/login` または `claude auth login`
- Git for Windows: 公式の表では「任意」、quickstart では「推奨」。入れると Bash ツールに Git Bash が使われる。このリポジトリは git を使うので**入れる**
- ネイティブ Windows はサンドボックス非対応(必要なら WSL 2)。WPF は Windows ネイティブで動かすので、**WSL ではなくネイティブ Windows** で使う

### 3.2 取得から確認まで
```powershell
git clone https://github.com/tamurayuuki46-cpu/eiken1.5writing.git
cd eiken1.5writing
git status                       # クリーンであること
dotnet --list-sdks               # 10.0.x があること
dotnet restore
dotnet build -warnaserror        # 警告 0・エラー 0
dotnet test                      # Core 499 / Flow 378 / Persistence 83(合計 960)が通ること
git switch -c feature/wpf-shell  # 作業ブランチ(名前は例)。main へ直接 push しない
```
- Windows ではメモリ不足の回避(`-m:1` など)は通常不要(クラウドだけの事情)
- アプリの起動(WPF 作成後): `dotnet run --project src/EikenSpelling.Wpf`

---

## 4. UI 技術の比較(WPF / Avalonia)

| 観点 | WPF | Avalonia |
|---|---|---|
| Core・Flow・Persistence の再利用 | そのまま参照できる(どれも net10.0) | 同じ |
| Flow の「画面の状態」との相性 | MVVM で写すだけ | 同じ |
| Windows での操作性 | ネイティブ。日本語 IME・キーボード操作・アクセシビリティが成熟 | Windows で動く。日本語 IME などは実機での確認が要る |
| 音声(System.Speech) | 仕様どおり使える | Windows 向けに対象フレームワークを分ければ使える(手間が増える) |
| Claude Code での自動テスト | 画面のテストは Windows でしか動かない(クラウドでは不可)。CI の windows-latest では可能 | ヘッドレスのテストが Linux でも動くので、**クラウドからも UI のテストを回せる** |
| 将来の拡張(スマホ版) | Windows 専用。スマホは別の UI が要る | 他 OS にも広げやすい |
| 仕様・文書との整合 | 仕様 v1.0 15.1 と一致(変更不要) | 仕様の変更が要る(v1.0 は v0.1 で採用しないと明記)。DECISIONS・CLAUDE.md・README も更新 |
| 追加の依存 | なし(SDK に含まれる) | NuGet パッケージの追加(要承認) |

### 判断の材料(推奨)
- **v0.1 は仕様どおり WPF を推奨**。学習ロジックと進行役はすでに UI から切り離してあり、Flow の 378 件のテストが画面の手前の振る舞いを守っている。UI 層を薄くすれば、UI 技術の差は小さい
- UI 層のうち ViewModel(Flow の画面の状態を表示用に写す部分)を net10.0 の別プロジェクトに置けば、WPF でも ViewModel のテストはクラウド(Linux)で回せる(設計案。未承認)
- Avalonia を選ぶ理由が強いのは「クラウドから UI のテストまで自動で回したい」「スマホ版を同じ UI 技術で作りたい」場合。その場合は仕様 v1.0 15.1 と Overengineering の記述を変える正式な決定が先に要る
- **決定はユーザーが行う。承認なしに Avalonia へ変えたり UI 実装を始めたりしない**

---

## 5. 引き継ぎ資料

### 5.1 現在の開発状況
- フェーズ: 実装。MVP の Must のうち、画面を除く部分(学習ロジック・保存・進行役)が完了。次は UI
- 開発の流れ: 8 ステップ(`prompts/8steps.md`)。① プラン(質問 → requirements/design)→ ② プランのレビュー(design・security)→ ③ テスト計画のレビュー(edge-case)→ ④⑤ tdd → ⑥ 3 体レビュー → ⑦ PR(create-pr スキル)

### 5.2 完了済み(main に入っている)
- 単語データのモデル・読込・検証、正誤判定、差分表示(Levenshtein)
- 学習設定(初期値・検証・JSON)と設定画面での保存・初期値に戻す
- 学習記録・苦手登録・克服判定・再確認スケジュール・苦手度
- 出題枠・出題する単語の選択・セッション(再練習・途中終了・再開)
- 保存・復旧(tmp → 検証 → Replace、bak からの自動復旧、破損ファイルの退避、新しすぎる版の拒否)
- 進行役 Flow: 起動・再開の選択・練習・判定・保存失敗時の再保存・結果・両方破損・終了のみ・苦手一覧・設定画面
- 結果画面の集計・苦手一覧・ホームの学習状況

### 5.3 未完了
- UI(画面・音声・タイマー表示・キーボード操作)
- 同梱の単語データ(初期 100 語)と日本語訳
- 二重起動の防止(WPF で名前付き Mutex。DECISIONS 決定済み)
- 起動 → 出題 → 保存 → 再起動 → 復元 の Windows 実機確認(CLAUDE.md 10章の完了条件)
- P2 の積み残し(各 `.steering/*/tasklist.md` の「片付け」)

### 5.4 確定済みの設計判断(FA)
- 正本は `docs/DECISIONS.md`(仕様書より先に見る)。主なもの:
  - 層と依存: `Wpf → Flow → Persistence → Core`。Core・Persistence・Flow は Windows 非依存(net10.0)
  - 保存先 `%APPDATA%\EikenSpelling\`(progress.json・progress.bak.json・settings.json)。保存の手順・復旧・破損時は勝手に初期化しない
  - 画面の状態は Flow が持つ。WPF は Flow の画面の状態を表示し、操作を渡すだけ
  - 文言とログ用の理由(Diagnostic)を分け、パス・誤答入力を入れない。画面の状態を丸ごとログに出さない
  - 練習ごとの選択は settings.json に書かない。設定画面で保存したときだけ書く
  - 再開できない練習(問題が出せない)は「終了して新しく始める」だけ

### 5.5 未決定事項
| # | 事項 | 選択肢・メモ |
|---|---|---|
| U1 | UI 技術 | **決定済み: WPF**(2026-11-13、ユーザー決定。DECISIONS「UI 技術」) |
| U2 | 日本語訳の辞書 | ライセンス確認待ち。初期 100 語の作り方(自作の訳など) |
| U3 | auto-merge | main の保護ルール(必須チェック)を確認してから有効にするか |
| U4 | README・古い tasklist の更新 | D2〜D4 を直すか(文書だけの変更) |
| U5 | テスト SDK の更新 | Microsoft.NET.Test.Sdk の更新(NuGet の変更なので要承認) |

### 5.6 次に着手すべきタスク(順に)
1. ~~U1 を決める~~ → **WPF に決定済み**(2026-11-13)
2. **UI の骨組み**(① プランから): アプリの起動・二重起動の防止・保存先・単語データの読み込み → Flow の画面の状態を表示する画面の切り替え。ホーム → 練習 → 判定 → 結果 の最小の流れ
3. 仮の単語データ(数語)で 起動 → 出題 → 保存 → 再起動 → 復元 を実機で確認
4. 音声(System.Speech)・フラッシュ表示の計時・苦手一覧・設定画面
5. 同梱の単語データ(U2 が決まり次第)

### 5.7 Windows で実施すべきテスト
- `dotnet build -warnaserror` と `dotnet test`(960 件)が PC で通ること
- Persistence の Windows 固有の挙動(File.Replace・退避・権限)は CI の windows-latest で確認済み。PC でも一度 `dotnet test` で確かめる
- 実機の通し(UI 作成後): 初回起動 → 練習 → 1 問ごとの保存 → アプリを閉じる → 再起動 → 再開の選択 → 続きから → 結果
- 保存失敗: progress.json を他のアプリで開いてロック → 判定画面で「保存失敗・再保存」→ ロックを外して再保存
- 破損: progress.json を壊す → バックアップから復旧の通知 / 両方壊す → 「新しい記録で開始」「終了」
- 日本語 IME での入力(英字入力に切り替わるか)、Enter での回答、フォーカス
- 音声モード(音声のある PC・無い設定)
- 二重起動(2 つ目が既存の画面を前に出して終わる)
- 高 DPI・ウィンドウの大きさ、例外ダイアログが出ないこと

### 5.8 Windows CLI に渡す開始用プロンプト
```
このリポジトリ(eiken1.5writing)の開発を、クラウドの Claude Code から Windows の CLI へ引き継いだ。
まず CLAUDE.md と docs/DECISIONS.md を読み、prompts/8steps.md の手順で進めて。

最初にやること(コードは変えない):
1. git status と git log -1 で、main の最新(bc70ca2 以降)にいることと作業ツリーがクリーンなことを確かめる。
2. dotnet --list-sdks で .NET 10 があることを確かめ、dotnet build -warnaserror と dotnet test を実行して全件の結果を見せる(期待: 警告0、Core 499 / Flow 378 / Persistence 83)。
3. UI 技術は WPF に決定済み(DECISIONS.md「UI 技術」)。Avalonia などへの変更はしない。

続けて、8steps の ① プランとして「UI の骨組み(起動・二重起動の防止・保存先・単語データの読み込み・Flow の画面の状態の表示・ホーム → 練習 → 判定 → 結果)」の requirements.md と design.md を、書く前に私に質問してから作って。
守ること: WPF は Flow の画面の状態を表示して操作を渡すだけにし、学習ロジックを画面に書かない。NuGet の追加は理由を添えて確認を取る。main へ直接 push しない。作業はブランチを切って PR にする。
```

---

## 6. Git 操作の提案(既存の作業を失わない方法)
- 今は未コミット変更・未 push のコミットが無く、作業ブランチの中身はすべて main に入っている → **失うものは無い**
- PC 側は main から新しいブランチを切って始める(`git switch -c feature/<名前>`)。クラウドの作業ブランチ `ccr-37466a37-vb1yx9` は使い続けない
- クラウドと PC で同じブランチを同時に編集しない。切り替えるときは「push → 相手側で pull」
- 片付け(任意・要承認): マージ済みのリモートブランチの削除、ローカルの古い `main` の更新
- この資料をリポジトリに残す場合の案(要承認): `.steering/20261113-windows-handoff/handoff.md` として作業ブランチにコミットし PR にする

---

## 7. 判定: **GO WITH CONDITIONS**
- GO の根拠: 未コミット・未 push の変更なし、未マージ PR なし、main で警告 0・960 件通過、CI は Windows でも通過、学習ロジック・保存・進行役は UI から切り離され文書(CLAUDE.md・DECISIONS.md・`.steering/`)もそろっている
- 条件:
  1. PC で .NET 10 SDK・Git・Claude Code(claude.ai アカウントでログイン)を入れ、main で `dotnet build -warnaserror` と `dotnet test` が通ることを確かめる
  2. ~~UI 技術(U1)をユーザーが決める~~ → WPF に決定済み(2026-11-13)
  3. クラウドと PC で同じブランチを同時に使わない(PC は main から新しいブランチ)
  4. README などの古い記述(D2〜D4)を把握しておく(直すかは U4)
