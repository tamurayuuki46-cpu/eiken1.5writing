# 第1工程: UI 技術選定(WPF / Avalonia)

- 作成: 2026-11-14
- 位置づけ: **AI の暫定提案**。最終選定は人間が行い、承認されたら「FA(正式決定)」として DECISIONS.md に記録する
- この文書は第1工程だけを扱う。UX 設計・UI 設計・実装には進まない

---

## 0. 先に伝えること(現状の確認)

| # | 事実 | 出典 |
|---|---|---|
| F1 | UI 技術は **すでに WPF で決定済み**(2026-11-13、ユーザー決定)。仕様 v1.0 15.1 とも一致 | docs/DECISIONS.md「UI 技術」、`.steering/20261113-windows-handoff/handoff.md` 4章 |
| F2 | 当時の比較は表1枚。Android 対応・IME・音声の調べは浅い | 同上 4章 |
| F3 | 第1工程の承認前に、WPF 前提のプラン(requirements・design・decisions)が書かれている。ブランチ `feature/wpf-shell` に1コミット済み、requirements.md と design.md に未コミットの変更あり | `git log` / `git diff` |
| F4 | F3 のプランは画面の構成・MVVM・ViewModel の分割など、新しい工程の第2・第3工程にあたる内容を含む | `.steering/20261114-wpf-shell/` |

→ 判断してほしいこと
1. 新しい工程の下で、WPF の決定を **そのまま FA として再確認する** か、**白紙に戻して選び直す** か
2. F3 のプランを **AI 暫定案として扱う**(第2〜4工程で見直す前提で、実装には使わない)ことにしてよいか

この文書は、どちらを選んでも判断できるように、比較・推奨・リスクを書く。

---

## 1. 前提(既存資産。技術で差が出ない部分)

- `EikenSpelling.Core` / `Persistence` / `Flow` はすべて net10.0。Windows 非依存。テスト 960 件(Core 499 / Flow 378 / Persistence 83)は通過
- 画面の状態と操作は Flow が持つ。UI は表示して操作を渡すだけ(DECISIONS.md「進行役 Flow」)
- CI は ubuntu-latest と windows-latest の両方で build・test を実行
- 仕様 v1.0 は「スマホアプリ(v0.1 では対象外)」「将来のスマホ版で Core を再利用できる構造」とする(2・15章)

→ どちらの UI 技術を選んでも、学習ロジック・保存・進行役は作り直さない。差が出るのは **UI 層・音声・UI テスト・将来のスマホ** に限られる。

---

## 2. 比較表

凡例: ◎ 有利 / ○ 問題なし / △ 注意 / ? 未確認(根拠不足)

| 評価項目 | WPF | Avalonia | メモ |
|---|---|---|---|
| 既存 C# の再利用 | ◎ 参照するだけ | ◎ 同じ | 両方とも net10.0 の Core・Flow をそのまま参照できる |
| Windows の動作・操作性 | ◎ Windows ネイティブで成熟 | ○ Windows で動く。描画は独自 | Avalonia は Skia 描画。Windows 標準の見た目・挙動との差は実機で確認が要る(?) |
| 日本語 IME・キーボード | ◎ 標準で成熟 | △ 対応済みだが実績は WPF より浅い | Avalonia 12.1.0 の更新内容に「Windows で IME の未確定文字のカーソル位置を反映」がある。つまり最近まで細部の不具合があった。本アプリは英字入力が中心なので影響は小さいが、**IME オフの切替・フォーカス移動は実機での確認が必須** |
| 英語音声の読み上げ | ○ `System.Speech`(Windows 専用) | △ 同じ `System.Speech` は Windows でしか使えない | どちらも **音声は専用の差し替え口(インターフェース)が要る**。Avalonia でも Android・Linux では別実装が必要。`System.Speech` は互換用で新機能は入らない(NuGet の説明) |
| Claude Code クラウド(Linux)での開発・テスト | △ WPF 自体はビルド・UI テストとも不可。ViewModel を net10.0 の別プロジェクトに置けば、その部分は Linux でテスト可 | ○ Avalonia.Headless があり、Linux でも UI の部分まで自動テストできる(NuGet に公開) | 画面の見た目の確認(スクリーンショット)は、どちらもクラウドでは限定的(?) |
| Claude Code CLI(Windows)での開発・テスト | ◎ `dotnet run` で実機確認まで | ○ 同じ | |
| 将来の Android 対応 | △ 不可。Android は別 UI を作る | ○ 公式に Android 対応(Windows・macOS・Linux・iOS・Android・WebAssembly) | 公式の説明では Android の対応は MAUI の対応方針に連動する、とされる。本当に使える時期・条件は着手時に再確認が必要(?) |
| 保守性 | ○ Microsoft が保守。変更が少なく安定 | ○ 活発に開発中。ただし主バージョンの更新が速い(12.x) | 本アプリは1人・1台。更新への追従は小さな負担 |
| 開発コスト | ○ 追加の学習・依存なし | △ NuGet の追加(CLAUDE.md で要承認)、仕様・DECISIONS・CLAUDE.md・README の更新が要る | v1.0 の Overengineering は MAUI/Avalonia への移行を v0.1 で採用しないとしている |
| 仕様との整合 | ◎ v1.0 15.1 のとおり | △ 仕様の変更が要る | |

### 重みをつけると(目安。重みは人間が決める)
- 「いま Windows で確実に使える」を最優先 → WPF
- 「Android 版を早く出したい」「クラウドで UI まで自動で回したい」を最優先 → Avalonia

---

## 3. AI の推奨案(暫定)

**推奨: v0.1 は WPF。ただし次の2つを条件にする。**

1. **ViewModel を net10.0 の別プロジェクトに置く**(Windows 専用アセンブリを参照しない)
   - 理由: WPF でも ViewModel のテストはクラウド(Linux)で回せる。将来 Avalonia に移ることになっても、移す量が画面(XAML)だけで済む
2. **音声は差し替え口(インターフェース)の後ろに置く**
   - 理由: WPF でも Avalonia でも `System.Speech` は Windows 限定。Android 版や別 OS で別実装に替えられる

### 理由
- 本アプリの利用者は1人・1台の Windows PC で、v0.1 の目的は「苦手スペルを克服する練習ができること」
- Avalonia を選ぶ強い理由(Android・Linux の UI テスト)は、いずれも **今は使わない将来の需要**。Android は仕様で v0.1 対象外
- 日本語 IME・キーボード操作は WPF のほうが実績が長く、切り分けの手間が少ない
- 上の2条件で、将来の移行コストはほぼ画面部分だけに抑えられる(推測。Avalonia 側の XAML は WPF と近いが同一ではない)

### 推奨を変える条件
次のどれかが当てはまるなら Avalonia を推奨に切り替える。
- Android 版を、v0.1 の次の段階で作ると決めている
- クラウド(Linux)で UI の自動テストまで回すことを重視する
- Windows 以外(Mac・Linux)でも使いたい

---

## 4. リスク

### WPF を選んだ場合
| リスク | 影響 | 対策 |
|---|---|---|
| Windows 専用 | Android 版は UI の作り直し(Core・Flow・Persistence は再利用) | 条件1・2。スマホ版が決まった時点で技術選定をやり直す |
| UI テストは Windows でしか動かない | クラウドで UI まで確認できない | ViewModel を別プロジェクトにしてテスト。UI は CI の windows-latest と実機で確認 |
| `System.Speech` は互換用 | 将来の機能追加・修正は期待できない | 差し替え口で隔離 |

### Avalonia を選んだ場合
| リスク | 影響 | 対策 |
|---|---|---|
| 日本語 IME・フォーカスの細部 | 入力体験が WPF より劣る可能性 | 先に小さな試作(IME オフ・Enter・フォーカス)を実機で確認してから決める |
| 仕様・文書の変更 | v1.0 15.1・Overengineering・DECISIONS・CLAUDE.md・README を更新 | 正式な決定の記録を先に作る |
| NuGet の追加 | CLAUDE.md で「確認なしの追加は禁止」 | 理由を添えて承認を取る |
| 音声は結局 Windows 専用 | Avalonia にしても Android 用の音声は別実装 | 差し替え口 |
| 主バージョンの更新が速い | 追従コスト | バージョンを固定して更新時だけ確認 |

---

## 5. 過剰設計になっていないか(Overengineering)
- 将来の Android のために今 Avalonia を採ると、v0.1 の完了(Windows で通しの練習)が遅れる
- 逆に WPF を選んでも、条件1・2は軽い(別プロジェクト1つと、音声のインターフェース1つ)ので過剰ではない

---

## 6. 未決事項(人間の判断が要るもの)

| # | 事項 | 選択肢 | AI の暫定案 |
|---|---|---|---|
| Q1 | UI 技術 | A. WPF を FA として再確認 / B. Avalonia に変更 / C. 試作で確かめてから決める | A |
| Q2 | Android 版の計画 | いつ頃・本当に作るか(v0.1 の次か、未定か) | 未定として、必要になったら再選定 |
| Q3 | 既存の `.steering/20261114-wpf-shell` | 暫定案として扱い、第2〜4工程の承認後に見直す / そのまま使う | 暫定案として扱う |
| Q4 | クラウドでの UI テストの重要度 | 重視する / ViewModel のテストで足りる | 足りる |

AI が補う軽微な事項(質問しない): ViewModel の分割の細部、音声インターフェースの形、試作の手順。

---

## 7. 出典(調査で確認したもの)
- このリポジトリ: docs/DECISIONS.md、docs/spec/v1.0_統合設計仕様書.md(2・15章)、.steering/20261113-windows-handoff/handoff.md、`git log`・PR #7〜#13
- Avalonia の対応プラットフォーム: https://docs.avaloniaui.net/docs/supported-platforms
- Avalonia.Headless(NuGet): https://www.nuget.org/packages/avalonia.headless
- Avalonia 11 の IME 対応の告知: https://avaloniaui.net/Blog/welcome-to-the-new-era-of-app-development-introducing-avalonia-v11
- Avalonia 12.1.0 リリースノート: https://newreleases.io/project/github/AvaloniaUI/Avalonia/release/12.1.0
- System.Speech(NuGet): https://www.nuget.org/packages/System.Speech/9.0.23

### 未確認(根拠が足りないもの)
- Avalonia の Linux 上の IME の現状
- Avalonia で Android 向けに英語音声を出す標準的な方法
- Avalonia の Windows 実機での日本語 IME の使い心地(試作しないと分からない)
