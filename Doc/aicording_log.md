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

## [2026-04-04] 18:39 publish-velopack.bat を Windows 11 の cmd.exe で実行可能に修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` 配下の `publish-velopack.bat` が Windows 11 の `cmd.exe` で正常動作するよう修正する
### 実施内容
- Cvnet10Wpfclient/publish-velopack.bat: `for /f` 内のインライン PowerShell を廃止し、補助スクリプト呼び出しへ変更
- Cvnet10Wpfclient/publish-velopack.bat: エラーメッセージと TODO コメントを ASCII ベースへ変更し、`cmd.exe` の文字コード解釈で構文が壊れにくい形へ整理
- Cvnet10Wpfclient/publish-velopack.version.ps1: `appsettings.json` から `Application.Version` を正規表現で抽出する補助スクリプトを追加
### 技術決定 Why
- `for /f (...) do` の中で PowerShell の丸括弧を含むインライン式を使うと `cmd.exe` 側で `FOR` 構文が壊れるため、`-File` 呼び出しへ分離して解釈系を分けた
- `appsettings.json` に `/* ... */` コメントが含まれており `ConvertFrom-Json` が安定しないため、コメント付きでも取得できる文字列抽出に切り替えた
- 実バッチだけ失敗して最小テストが通る状態だったため、非 ASCII 文字列も除去して `cmd.exe` 依存の文字コード要因を避けた
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\documents\new2022\cv10\Cvnet10Wpfclient\publish-velopack.bat"` → `dotnet publish` と `vpk pack` が完走し、`[INFO] Velopack finished task for creating package. Version=1.0.0` を確認

---

## [2026-04-04] 18:08 Cvnet10WpfclientのVelopack導入と配布手順整備
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` に Velopack を導入し、ClickOnce 相当の更新処理と配布手順へ置き換える
### 実施内容
- Directory.Packages.props: `Velopack` の中央管理パッケージを追加
- Cvnet10Wpfclient/Cvnet10Wpfclient.csproj: `Velopack` 参照、WPF の `Main` 起動設定、ClickOnce 発行ターゲット削除を反映
- Cvnet10Wpfclient/App.xaml.cs: `VelopackApp.Build().Run()` を先頭で実行する `Main` エントリーポイントを追加
- Cvnet10Wpfclient/Services/UpdateService.cs: ClickOnce 風の更新処理を `UpdateManager` ベースへ置換
- Cvnet10Wpfclient/ViewModels/00System/SysUpgradeViewModel.cs: Velopack 更新確認文言へ調整し、設定から `FeedUrl` を読む形へ変更
- Cvnet10Wpfclient/ViewModels/SampleViewModel.cs: ClickOnce テスト表示を Velopack 設定表示/実行情報表示へ置換
- Cvnet10Wpfclient/Views/SampleView.xaml: サンプル画面のボタン文言とバインディングを Velopack 用に変更
- Cvnet10Wpfclient/appsettings.json: `Application.Version` を追加
- Cvnet10Wpfclient/appsettings.Production.json: `Update:FeedUrl` と `Channel` の本番設定雛形を追加
- Cvnet10Wpfclient/pre-publish-backup.bat: 廃止のため削除
- Cvnet10Wpfclient/publish-velopack.bat: `appsettings.json` の版数を読み取って `dotnet publish` と `vpk pack` を実行する配布バッチを追加
- Doc/velopack_release_manual.md: 版数更新から Velopack 配布までの手順書を追加
### 技術決定 Why
- WPF の `Main` を明示して `VelopackApp.Build().Run()` を最初に実行することで、更新適用時の起動経路を Velopack 推奨形へ寄せた
- 版数源を `appsettings.json` に一本化し、実行表示と配布版数の不整合を減らした
- 配布先URLは `appsettings.Production.json` に分離し、環境ごとの差し替えをしやすくした
### 確認
- `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` → ビルド成功（警告0、エラー0）

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

## [2026-04-03] 15:30 SysGeneralMenteViewModel で SerializedColumn 付き項目を JSON 編集可能にする
### Agent
- claude-opus-4.6 : GitHub-Copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SysGeneralMenteViewModel の `GetEditableProperties()` で読み飛ばしている `List<MasterGeneralMeisho>` や `BaseDetailClass` を JSON serialize して修正可能な項目にする
### 実施内容
- Cvnet10Wpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs:
  - `SysGeneralEditCell` に `IsJsonColumn` プロパティを追加
  - `IsSupportedProperty()` に `[SerializedColumn]` 属性チェックを追加し、JSON 格納型も編集対象に含める
  - `IsJsonSerializedProperty()` ヘルパーを追加（`[SerializedColumn]` かつ非 primitive 型を判定）
  - `CreateRow()` で JSON 列には `ToJsonText()` を使用し `IsJsonColumn` フラグを設定
  - `ToItem()` で JSON 列には `ConvertFromJsonText()` で逆変換
  - `ToJsonText()`: `JsonConvert.SerializeObject` で整形済み JSON 文字列を生成
  - `ConvertFromJsonText()`: `JsonConvert.DeserializeObject` で JSON 文字列から型復元
### 技術決定 Why
- `IsSupportedType` の primitive ホワイトリストは変更せず、`IsSupportedProperty` のレベルで `[SerializedColumn]` を先にチェックすることで既存の primitive 列への影響をゼロにした
- NPoco の `[SerializedColumn]` 属性は DB に JSON 格納されることを意味するため、同じ JSON 形式でユーザーに編集させるのが自然
- `Newtonsoft.Json` は既に using 済みで `JsonConvert` が使えるため新規依存なし
### 確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj` → ビルド成功（エラー0、警告0）

---
