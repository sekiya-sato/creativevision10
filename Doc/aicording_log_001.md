# AI Coding Log

このファイルは、Cvnet10プロジェクトにおけるAI支援開発の作業履歴を記録します。

## 使用するAIツール
- **GitHub Copilot**: インライン補完、クイックフィックス、小規模編集（VS2026統合）
- **OpenCode**: 大規模機能実装、複数ファイル編集、ドキュメント作成（CLI）

## 記録フォーマット
```markdown
## [YYYY-MM-DD] hh:mm 作業タイトル
### Agent
- [使用した AI Model 名 : AI Provider 名]
  例: claude-sonnet-4.5 : GitHub-Copilot
      gpt-5.4 : OpenAI
### Editor
- [使用したエディタ]
  例: OpenCode, VS2026, VSCode, GitHubCopilot-Cli
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [プロジェクト名]/[ファイル名]: [変更内容の要約]
### 技術決定 Why
- [技術的判断の理由]
### 確認
- [Build結果やテスト結果]
```

## アーカイブルール
- 400行を超える場合、既存履歴を `aicording_log_[001-999].md` として連番保存
- 新規に `aicording_log.md` を作成して記録を継続

---

## [2026-03-18] 16:18 出荷売上入力画面作成
### Agent
- [使用した AI Model 名 : OpenAI gpt-5.2-codex]
### Editor
- [使用したエディタ: OpenCode]
### 目的
- ユーザーからの要望：指示書に従い出荷・売上入力画面を作成し、売上日表示に対応
### 実施内容
- Cvnet10Wpfclient/Views/06Uriage/ShukkaUriageInputView.xaml: ヘッダ一覧/明細タブのUI構成と検索・サマリ領域を追加
- Cvnet10Wpfclient/ViewModels/06Uriage/ShukkaUriageInputViewModel.cs: BaseMenteViewModel化と明細再計算/検索/一覧コマンドを実装
### 技術決定 Why
- 明細編集による合計値更新を確実にするため、明細コレクションの変更イベントで再計算と同期を実施
### 確認
- 未実施（未ビルド）

---

## [2026-03-11] 商品メンテ画面 - 日付フィールドを DatePicker に変更

### Agent
- Claude Sonnet 4.6 : GitHub-Copilot

### Editor
- VS2026

### 目的
- ユーザーからの要望：`MasterShohinMenteView.xaml` の「出荷日」「納品日」「店舗投入日」の3項目を `TextBox` から `DatePicker` へ変更する

### 実施内容
- `Cvnet10Wpfclient/Views/01Master/MasterShohinMenteView.xaml`:
  - Row12「出荷日」: `TextBox (Text=DayShukka)` → `DatePicker (SelectedDate=DayShukka)`
  - Row13「納品日」: `TextBox (Text=DayNohin)` → `DatePicker (SelectedDate=DayNohin)`
  - Row14「店舗投入日」: `TextBox (Text=DayTento)` → `DatePicker (SelectedDate=DayTento)`

### 技術決定 Why
- `DateYmd8Converter` (`yyyyMMdd` 形式の string ↔ `DateTime?` 相互変換) が `App.xaml` でグローバル登録済みのため、View 側での追加宣言なしに `{StaticResource DateYmd8Converter}` を使用
- `Style="{StaticResource MaterialDesignFloatingHintDatePicker}"` を適用し、既存の Material Design デザイン統一性を維持
- `IsTodayHighlighted="False"` はサンプルコードの指示に準拠
- `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` により ViewModel との双方向バインディングを維持

### 確認
- Build OK

---
## [2026-03-18] 16:40 出荷売上入力画面修正（再作成）
### Agent
- [使用した AI Model 名 : OpenAI gpt-5.2-codex]
### Editor
- [使用したエディタ: OpenCode]
### 目的
- ユーザーからの要望：精度向上のため画面作成をやり直し
### 実施内容
- Cvnet10Wpfclient/ViewModels/06Uriage/ShukkaUriageInputViewModel.cs: コンストラクタ追加でイベント登録を修正
### 技術決定 Why
- 部分メソッドの重複エラーを回避し、明細コレクション変更イベントを正常に処理するため
### 確認
- 未実施（NuGetパス問題でビルド不可、環境原因）
## [2026-03-11] 得意先マスタメンテ画面の新規作成

### Agent
- claude-sonnet-4.6 : OpenCode

### Editor
- OpenCode

### 目的
- ユーザーからの要望：`MasterTokui` テーブルのメンテナンス画面を新規作成する。`MasterShohinMenteView.xaml` / `MasterShohinMenteViewModel.cs` の構成を参考にする。

### 実施内容
- `Cvnet10Wpfclient/Views/01Master/MasterTokuiMenteView.xaml`: 新規作成。タブ構成（基本情報 / 支払情報 / 名称リスト / 詳細内容）で `MasterTokui` の全フィールドを編集可能にした
- `Cvnet10Wpfclient/Views/01Master/MasterTokuiMenteView.xaml.cs`: 新規作成。`BaseWindow` を継承し `InitializeComponent()` のみの最小 code-behind
- `Cvnet10Wpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs`: 新規作成。`BaseMenteViewModel<MasterTokui>` を継承。`DoSelectShain` / `DoSelectPayMethod` / `DoSelectPaysaki` コマンドを実装
- `Cvnet10Wpfclient/Models/MenuData.cs`: `■ Master` セクションの「社員マスタ」直後に「得意先マスタメンテ」エントリを追加

### 技術決定 Why
- `MasterTokui` は `MasterTorihiki` → `BaseDbHasAddress` の継承階層を持つため、住所・連絡先フィールドは `BaseDbHasAddress` 由来のものを直接バインドした
- 外部キー選択（担当者・入金方法・請求先）は既存の `SelectWinView` + `SelectWinViewModel.SetParam()` パターンを踏襲
- 入金方法の `Kubun='PAY'` は `'BMN'`・`'BRD'`・`'MKR'` 等と同じ命名規則に沿った値を採用。DBの `MasterMeisho` テーブルへの登録前提
- SDK スタイル `.csproj` のため新規ファイルの手動登録は不要

### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行
- MSB3374 (`Up2Date` ファイルへのアクセス拒否) のみ発生。これは WSL2 上の NTFS パーミッション問題であり環境起因。コード起因のエラー・警告はゼロ

---

## [2026-03-11] 仕入先マスタメンテ画面の新規作成

### Agent
- gpt-5.4 : OpenAI

### Editor
- OpenCode

### 目的
- ユーザーからの要望：`Cvnet10Wpfclient.csproj` の `MasterShiire` テーブル向けメンテ画面として、`MasterShiireMenteView.xaml`、`MasterShiireMenteView.xaml.cs`、`MasterShiireMenteViewModel.cs` を新規作成する。参考は `MasterTokuiMenteView.xaml` / `MasterTokuiMenteViewModel.cs`。

### 実施内容
- `Cvnet10Wpfclient/Views/01Master/MasterShiireMenteView.xaml`: 新規作成。`MasterShiire` 向けに一覧、基本情報、支払情報、名称リスト、詳細内容タブを実装
- `Cvnet10Wpfclient/Views/01Master/MasterShiireMenteView.xaml.cs`: 新規作成。`BaseWindow` 継承の最小 code-behind を実装
- `Cvnet10Wpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs`: 新規作成。`BaseMenteViewModel<MasterShiire>` を継承し、初期一覧、確認メッセージ、完了メッセージ、担当者・支払方法・支払先選択コマンドを実装

### 技術決定 Why
- `MasterShiire` は `MasterTorihiki` 継承で `MasterTokui` より項目数が少ないため、得意先専用の `TenType` / `IsZaiko` / `Mail` は除外し、共通取引先項目に絞って画面を構成した
- `MasterShiire` の `Id_Paysaki` / `VPaysaki` は共通基底に存在するため、支払先選択は `MasterShiire` 自テーブル検索で統一した
- SDK スタイルの WPF プロジェクトのため、`.csproj` への明示的な新規ファイル登録は不要と判断した

### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行し、Build OK（0 warning / 0 error）を確認

---

## [2026-03-17] vmcreate手順書の作成対象リスト未完成部補完
### Agent
- gpt-5.3-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Doc/instruction_20260317_10_vmcreate.md` の作成対象リスト未完成部分を `Cvnet10Wpfclient/Models/plan-name.txt` を元に第一候補で完成させる
### 実施内容
- `Doc/instruction_20260317_10_vmcreate.md`: `03Hachu` の不足3項目を追加し、`04Juchu` / `05Shiire` / `06Uriage` / `07Haibun` / `08Zaiko` を第一候補名で新規補完
- `Doc/instruction_20260317_10_vmcreate.md`: 空フォルダ名・空項目（``）のプレースホルダをすべて解消
### 技術決定 Why
- 命名の揺れを避けるため、`plan-name.txt` の各メニューに対して先頭（第一候補）の View 名を採用して一覧を確定した
### 確認
- ドキュメント更新のみのため Build は未実施

---

## [2026-03-17] Views/ViewModels 業務カテゴリフォルダの追加と文書化
### Agent
- gpt-5.3-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient.csproj` 配下の `Views` / `ViewModels` に業務カテゴリフォルダ案を作成し、ローマ字綴り（`Hatchu` / `Juchu` / `Butsuryu`）を反映した案を文書化する
### 実施内容
- `Cvnet10Wpfclient/Views/`: `02Yosan` から `41Butsuryu` までの 15 フォルダを新規追加し、各フォルダに `.gitkeep` を配置
- `Cvnet10Wpfclient/ViewModels/`: `Views` 側と同名の 15 フォルダを新規追加し、各フォルダに `.gitkeep` を配置
- `Doc/Cvnet10Wpfclient_View_ViewModel_FolderPlan.md`: 命名ルール、ローマ字統一方針、追加フォルダ一覧を新規作成
### 技術決定 Why
- `Views` と `ViewModels` を同名フォルダで対にすることで、画面と ViewModel の対応関係を探索しやすくし、運用時の迷いを減らすため
- 空フォルダは Git 管理対象外のため、初期構成を確実に共有できるよう `.gitkeep` を配置した
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行し、Build OK（0 warning / 0 error）を確認
### 最終フォルダ構成 (Views / ViewModels)
```
- 00System - システム関連
- 01Master - マスタ関連
- 02Yosan - 予算関連
- 03Hatchu - 発注関連
- 04Juchu - 受注/展示会関連
- 05Shiire - 仕入関連
- 06Uriage - 売上関連
- 07Haibun - 配分関連
- 08Zaiko - 在庫管理関連
- 20UriageAnalysis - 売上分析関連
- 21OroshiAnalysis - 卸・販売員・経営分析関連
- 22CPA - C.P.A関連
- 30HHT - HHT/POS連携関連
- 31Getsuji - 月次/更新処理関連
- 32LoyalCustomer - Loyal Customer関連
- 40Tenpo - 店舗関連
- 41Butsuryu - 物流関連
```

---

## [2026-03-17] vmcreate指示に基づくView/ViewModel一括生成とメニュー接続
### Agent
- gpt-5.3-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Doc/instruction_20260317_10_vmcreate.md` に従って View/XAML・code-behind・ViewModel を作成し、`MenuData.cs` を接続し、最後にドキュメント更新する
### 実施内容
- `Cvnet10Wpfclient/Views/`: 指示リスト（`00System`〜`41Logistics`）に対応する初期 `*.xaml` / `*.xaml.cs` を一括作成
- `Cvnet10Wpfclient/ViewModels/`: 指示リストに対応する初期 `*ViewModel.cs`（`Helpers.BaseViewModel` 継承）を一括作成
- `Cvnet10Wpfclient/Models/MenuData.cs`: `CreateDefault()` 内の対象 `typeof(object)` を生成した View 型へ置換し、各行末に `// [View名]` コメントを追加
- `Doc/instruction_20260317_10_vmcreate.md`: 実体と齟齬が出ないよう `03Hatchu` 表記、`売上週報･月報`、`ポイントマスタ（ベース）（管理者用)` を追記・修正
- `Cvnet10Wpfclient/Views/32LoyalCustomer/PointMasterBaseView.xaml`: 指示修正に伴い不要となった旧名称ファイルを削除
- `Cvnet10Wpfclient/Views/32LoyalCustomer/PointMasterBaseView.xaml.cs`: 指示修正に伴い不要となった旧名称ファイルを削除
- `Cvnet10Wpfclient/ViewModels/32LoyalCustomer/PointMasterBaseViewModel.cs`: 指示修正に伴い不要となった旧名称ファイルを削除
### 技術決定 Why
- 大量の雛形作成は手作業より生成スクリプトの方が命名・namespace・DataContext の表記揺れを防ぎ、差分品質を一定化できるため
- 既存プロジェクト構成に合わせ、`03Hachu` は実フォルダ名の `03Hatchu` に統一して参照不整合を防止した
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行し、Build OK（0 warning / 0 error）を確認

---

## [2026-03-18] Views/ViewModels の .gitkeep 削除
### Agent
- gpt-5.2-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：各 folder 内の不要な `.gitkeep` を削除する
### 実施内容
- `Cvnet10Wpfclient/Views/02Yosan/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/03Hatchu/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/04Juchu/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/05Shiire/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/06Uriage/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/07Haibun/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/08Zaiko/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/20UriageAnalysis/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/21OroshiAnalysis/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/22CPA/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/30HHT/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/31Monthly/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/32LoyalCustomer/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/40Shop/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/Views/41Logistics/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/02Yosan/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/03Hatchu/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/04Juchu/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/05Shiire/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/06Uriage/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/07Haibun/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/08Zaiko/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/20UriageAnalysis/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/21OroshiAnalysis/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/22CPA/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/30HHT/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/31Monthly/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/32LoyalCustomer/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/40Shop/.gitkeep`: 不要ファイルの削除
- `Cvnet10Wpfclient/ViewModels/41Logistics/.gitkeep`: 不要ファイルの削除
### 技術決定 Why
- 空フォルダ維持の目的がなくなったため、不要な管理ファイルを削除してツリーを簡素化した
### 確認
- 変更はファイル削除のみのため Build は未実施


## [2026-03-18] RangeInputParamView 範囲選択画面の追加
### Agent
- gpt-5.2-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Doc/instruction_20260318_14_RangeInputParamView.txt` に従い、入力画面ヘッダ一覧用の範囲選択画面を作成する
### 実施内容
- `Cvnet10Wpfclient/Views/Sub/RangeInputParamView.xaml`: 伝票No/日付/仕入先CD/入庫先CD/商品CD/件数の範囲入力レイアウトを追加
- `Cvnet10Wpfclient/Views/Sub/RangeInputParamView.xaml.cs`: `BaseWindow` 継承の最小 code-behind を追加
- `Cvnet10Wpfclient/ViewModels/Sub/RangeInputParamViewModel.cs`: `SelectInputParameter` を保持し OK でダイアログ結果を返す VM を追加
- `Cvnet10Wpfclient/ViewModels/Sub/SelectInputParameter.cs`: 入力条件パラメータクラスを追加
### 技術決定 Why
- 既存の範囲指定ダイアログ構成に合わせ、入力ヘッダ用の from/to 条件を明示したため
### 確認
- 追加実装のみのため Build は未実施

---

## [2026-03-18] 受注/売上入力向け指示書のMarkdown完成
### Agent
- gpt-5.2-codex : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Doc/instruction_20260317_17_HachuInputView.txt` を受注入力・売上入力のような複雑画面作成のための Markdown 仕様で完成させる
### 実施内容
- `Doc/instruction_20260317_17_HachuInputView.txt`: 画面構成、UI要素、操作、ViewModel設計、XAML方針、明細行VM、画面フロー、注意点を追加し複雑画面向け仕様として整備
### 技術決定 Why
- 受注/売上のようにヘッダー・明細・サマリが同居する画面は UI/VM 仕様を明確に分けて記述した方が再利用性と実装一貫性が高いため
### 確認
- ドキュメント更新のみのため Build は未実施

---

## [2026-03-19] 08:50 Microsoft Learn MCP サーバ追加
### Agent
- [使用した AI Model 名 : openai/gpt-5.4 : OpenAI]
### Editor
- [使用したエディタ: OpenCode]
### 目的
- ユーザーからの要望：`https://learn.microsoft.com/api/mcp` を利用する MCP サーバ設定を追加する
### 実施内容
- `/.mcp.json`: `microsoft-learn` の remote MCP サーバ定義を追加
### 技術決定 Why
- 認証不要で公式 Microsoft Learn ドキュメントへ接続できる標準的な workspace 設定として、ルートの `.mcp.json` を採用した
### 確認
- 設定ファイル追加のみのため Build は未実施

---

## [2026-03-19] 09:54 OpenCode導入計画の実施（標準構成）
### Agent
- [claude-sonnet-4.5 : GitHub-Copilot]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：OpenCodeの導入計画を策定し、標準構成で実施する。GitHub Copilotと併用する形で、使い分けルールとドキュメントを整備する。
### 実施内容
- `AGENTS.md`: OpenCode専用に改訂。Purpose/AI Tool Separation Policy/High Priority OpenCode Rules/Read-Only強化/Pre-Completion Checklistを更新
- `Doc/opencode_usage_guide.md`: 新規作成。OpenCodeの起動方法、基本的な使い方、プロンプト例、FAQ、トラブルシューティングを記載
- `Doc/ai_tool_selection_guide.md`: 新規作成。GitHub CopilotとOpenCodeの使い分け基準、シーン別推奨ツール一覧、判断フローチャート、実践シナリオを記載
- `Doc/aicording_log.md`: ヘッダにフォーマット説明とアーカイブルールを追加。使用するAIツール（GitHub Copilot/OpenCode）の記載方法を明確化
- `Doc/opencode_prompts.md`: 新規作成。頻出タスク向けプロンプト集（新規画面作成/リファクタリング/ドキュメント/コードレビュー/トラブルシューティング/テスト/DB/gRPC）を整備
### 技術決定 Why
- AGENTS.mdをOpenCode専用とすることで、GitHub Copilotの`.github/copilot-instructions.md`と明確に分離し、各ツールに最適化されたルールセットを提供
- 使い分けガイドを作成することで、開発者が状況に応じて適切なツールを選択でき、開発効率を最大化できる
- プロンプトライブラリを整備することで、OpenCode利用時のベストプラクティスを共有し、品質の均一化を図る
- 作業履歴フォーマットを統一することで、どちらのツールを使用したかを明確に追跡可能にした
### 確認
- ドキュメント追加のみのため、コード起因のBuildエラーは発生せず（NuGetパス問題は環境起因で既知の問題）
- 作成ファイル: AGENTS.md（改訂）、doc/opencode_usage_guide.md、doc/ai_tool_selection_guide.md、doc/aicording_log.md（ヘッダ追加）、doc/opencode_prompts.md

---

## [2026-03-19] 11:57 受注入力画面の明細グリッド再設計
### Agent
- [claude-sonnet-4.5 : GitHub-Copilot]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：`refer/cvnetclient/CvnetClient/Views/v991Inp/SubDlgInp12View.xaml` を参照し、`JuchuInputViewModel` に対応する `Views/04Juchu/JuchuInputView.xaml` の画面を構築する。現行の画面は `<Grid></Grid>` のみに一旦クリアし、再設計する。ViewModelに適切にバインド。ヘッダは `Tran12Jyuchu` の各項目、明細は `Tran12Jyuchu.Jmeisai` の `List<Tran99Meisai>?` を使用。変更するのは `JuchuInputView.xaml` のみに限定。
### 実施内容
- `Cvnet10Wpfclient/Views/04Juchu/JuchuInputView.xaml`: 明細DataGridを参照ファイルのレイアウトに基づき再設計。商品CD/色/サイズ/数量/単価/上代/納品日・備考を DataGridTemplateColumn で実装。Tran99Meisai の各プロパティ（Code_Shohin/Mei_Shohin/Code_Col/Mei_Col/Code_Siz/Mei_Siz/Su/Tanka/Kingaku/Jodai/JodaiAmount/NouhinDay/Biko）を適切にバインド。各列にTextChangedイベントでViewModelコマンド（LookupShohinByCodeCommand/GridSizeChangedCommand/QuantityChangedCommand/UnitPriceChangedCommand/JodaiChangedCommand）を接続。
### 技術決定 Why
- 参照ファイル（SubDlgInp12View.xaml）の2行構成DataGridTemplateColumnパターンを踏襲することで、商品CD/商品名、色/色名、サイズ/サイズ名を1列にまとめ、視認性と入力効率を向上
- Tran99Meisaiの各プロパティは既にObservableObjectを継承しているため、Mode=TwoWay/UpdateSourceTrigger=PropertyChangedで自動的に双方向バインディングが機能する
- ViewModelの既存コマンド構造に合わせ、参照ファイルのコマンド名をCurrentEditのJmeisaiリストにバインドする形で統一した
### 確認
- XAMLファイル更新のみのため、Buildは未実施（環境起因のNuGetパス問題があるため）

---

## [2026-03-19] 12:30 XAMLエラーチェック用OpenCodeスキル作成
### Agent
- [claude-sonnet-4.5 : GitHub-Copilot]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：WPF/XAMLファイルの品質チェックを実行するOpenCodeスキルを作成。構文エラー、名前空間宣言、リソース参照、コンバーター参照をチェックし、バインディングパス検証はユーザーの指示でスキップ可能なオプションとする。
### 実施内容
- `.agents/skills/check-xaml/SKILL.md`: スキルの詳細仕様を作成。構文エラー、名前空間宣言、リソース参照、コンバーター参照、バインディングパス検証（オプション）の各チェック項目、実行手順、プロジェクト固有設定（Cvnet10の命名規則/ディレクトリ構造）、実装例を記載。
- `.agents/skills/check-xaml/README.md`: スキルの概要、インストール方法、使い方、出力例、チェック項目の詳細、制限事項を記載。
### 技術決定 Why
- バインディングパス検証をオプションとすることで、動的DataContextや複雑なバインディング構造を持つ画面でも柔軟に対応可能にした
- Cvnet10プロジェクト固有の命名規則（*View.xaml/*ViewModel.cs）とディレクトリ構造（Views/04Juchu → ViewModels/04Juchu）に最適化し、ViewModelの自動推測精度を向上
- App.xamlのMergedDictionaries（Resources/UIColors.xaml/UICommon.xaml/UIMainWindow.xaml）をリソース参照チェック対象に含めることで、プロジェクト全体のリソース整合性を確保
- 既存のfind-skillsスキルと同様のSKILL.md + README.md構成を採用し、OpenCodeが自動認識できる標準フォーマットを維持
### 確認
- スキル定義ファイル作成のみのため、Build は未実施

---

## [2026-03-19] 13:05 XAMLチェックスキル報告フォーマット改善（v1.1.0）
### Agent
- [claude-sonnet-4.5 : GitHub-Copilot]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：正常な項目については詳細リストを省略し、サマリーと主要項目のみ報告。エラー・警告は詳細報告を維持する。
### 実施内容
- `.agents/skills/check-xaml/SKILL.md`: ステップ7「結果の報告」セクションを改訂。正常項目はサマリーのみ、エラー・警告は詳細報告の原則を明記。報告例を簡潔版に更新。FAQ に「報告される情報が多すぎませんか？」を追加。更新履歴に v1.1.0 を追記。
- `.agents/skills/check-xaml/README.md`: 出力例を簡潔版に更新。正常項目のリスト列挙を削除し、サマリー表示に変更。更新履歴に v1.1.0 を追記。
### 技術決定 Why
- バインディングパス検証で50個以上の正常項目をすべて列挙すると、報告が冗長になり問題箇所が埋もれてしまうため、正常項目はサマリー（「すべて検証OK (50個)」など）のみ表示
- エラー・警告は詳細に報告することで、修正すべき箇所を明確にし、開発者の作業効率を向上
- 「📋 チェック詳細」セクションで統計サマリーを提供することで、全体像の把握と個別問題の特定を両立
### 確認
- スキル定義ファイル更新のみのため、Build は未実施

---

## [2026-03-22] 15:30 テストプロジェクトを Microsoft.Testing.Platform へ移行
### Agent
- [big-pickle : OpenCode]
### Editor
- [OpenCode]
### 目的
- ユーザーからの要望：テストプロジェクト（TestLogin, Tests.Cvnet10Server）を Microsoft.Testing.Platform に移行する
### 実施内容
- `Directory.Packages.props`: Microsoft.NET.Test.Sdk を 18.3.0、MSTest を 4.1.0 に更新。Microsoft.Extensions.Configuration 系パッケージを追加
- `TestLogin/TestLogin.csproj`: `EnableMSTestRunner` を削除し、OutputType=Exe を維持（Program.cs が必要）
- `Tests.Cvnet10Server/Tests.Cvnet10Server.csproj`: `ManagePackageVersionsCentrally=false` を削除し、Central Package Management へ完全移行
### 技術決定 Why
- Microsoft.Testing.Platform は MSTest 4.1.0 の一部として提供されるため、追加パッケージ不要
- Central Package Management への完全移行で버전 일관성 보장
- TestLogin は Program.cs により Exe として実行されるため EnableMSTestRunner 不要
### 確認
- Build OK（TestLogin, Tests.Cvnet10Server 双方とも 0 warning / 0 error）
- Remote push: github.com:sekiya-sato/cvnet10.git (master)

---
