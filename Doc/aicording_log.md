# AI Coding Log

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
- `doc/Cvnet10Wpfclient_View_ViewModel_FolderPlan.md`: 命名ルール、ローマ字統一方針、追加フォルダ一覧を新規作成
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
