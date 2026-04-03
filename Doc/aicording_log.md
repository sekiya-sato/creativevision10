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
- 800行を超える場合、既存履歴を `aicording_log_[001-999].md` として連番保存
- 新規に `aicording_log.md` を作成して記録を継続

---

## [2026-04-03] 16:44 SelectServerTableViewの取得件数対応と汎用メンテ強化
### Agent
- gpt-5.4-mini : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`SelectServerTableView` を readonly 化し、取得件数を指定して `SysGeneralMenteView` を起動できるようにする
### 実施内容
- Cvnet10Wpfclient/Views/Sub/SelectServerTableView.xaml: 一覧を readonly 化し、取得件数入力欄を追加
- Cvnet10Wpfclient/ViewModels/Sub/SelectServerTableViewModel.cs: `SelectedRowCount` を追加して既定値を 200 に設定
- Cvnet10Wpfclient/ViewModels/MainMenuViewModel.cs: 選択テーブル名と取得件数を `AddInfo` で `SysGeneralMenteViewModel` に引き渡すよう調整
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs: `AddInfo` の `テーブル名|取得件数` 形式を解釈し、`MasterMeisho` 依存を除去して汎用一覧に対応
### 技術決定 Why
- 既存の画面遷移と `AddInfo` を活用して連携点を最小化しつつ、取得件数は `QueryListParam.MaxCount` を使うことで既存の検索基盤に自然に統合した
- 編集行のタイトル生成を固定項目依存から外し、任意テーブルでも破綻しないようにした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"` → ビルド成功（警告0、エラー0）

---

## [2026-04-03] 14:45 SysGeneralMenteView起動前のテーブル選択導線追加
### Agent
- gpt-5.3-codex : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`SysGeneralMenteView` の前に `SelectServerTableView` を呼び出し、選択したテーブル名を元に `SysGeneralMenteView` を実行できるようにする
### 実施内容
- Cvnet10Wpfclient/ViewModels/MainMenuViewModel.cs: `SysGeneralMenteView` 起動時のみ `SelectServerTableView` を先に表示し、選択テーブル名を `AddInfo` で引き渡す処理を追加
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs: `AddInfo` のテーブル名から `BaseDbClass` 派生型を解決し、対象型を動的に切り替えて一覧取得/追加/更新/削除が動くよう汎用化
- Doc/aicording_log_001.md: 既存ログを800行超過ルールに従ってアーカイブ
- Doc/aicording_log.md: 新規ログファイルを作成し本作業を記録
### 技術決定 Why
- 既存メニュー基盤（`MenuData` + `MainMenuViewModel.DoMenu`）を保ちつつ、`SysGeneralMenteView` だけに前段ダイアログを差し込むことで他画面への影響を最小化した
- 汎用メンテ対象はテーブル名から `TableNameAttribute` を逆引きして型解決し、既存の編集UI構造を維持したまま対象テーブルを切り替えられる設計にした
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"` → ビルド成功（エラー0、警告0）

---
