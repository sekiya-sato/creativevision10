# AI Coding Log

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
