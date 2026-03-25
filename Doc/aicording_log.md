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

## [2026-03-25] 14:49 keep-mcp の OpenCode グローバル追加
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`https://github.com/feuerdev/keep-mcp` を確認し、OpenCode のグローバル環境に Google Keep 用 MCP サーバーを追加する
### 実施内容
- `~/.config/opencode/opencode.jsonc`: `keep-mcp` をローカル MCP サーバーとして追加し、専用ラッパースクリプト経由で起動する構成にした
- `~/.config/opencode/bin/keep-mcp-opencode`: `~/.config/opencode/keep-mcp.env` を読み込み、必須資格情報を検証したうえで `~/.local/share/keep-mcp/.venv/bin/python -m server` を起動するスクリプトを追加した
- `~/.config/opencode/keep-mcp.env.example`: `GOOGLE_EMAIL` / `GOOGLE_MASTER_TOKEN` / `UNSAFE_MODE` の設定ひな形を追加した
- `~/.local/share/keep-mcp/.venv`: Python 仮想環境を作成し、`keep-mcp==0.3.1` をインストールした
### 技術決定 Why
- `pipx` と `uv` が未導入だったため、システム Python を汚さないようユーザー配下の仮想環境で `keep-mcp` を隔離インストールした
- Google Keep の資格情報を OpenCode 設定本体へ直書きしないため、外部 env ファイルを読むラッパースクリプト方式を採用した
### 確認
- `~/.local/share/keep-mcp/.venv/bin/python -m server --help` でモジュール起動が可能なことを確認
- `~/.config/opencode/bin/keep-mcp-opencode` 実行時に、資格情報未設定の場合は案内付きエラーで停止することを確認
- `opencode mcp list` で `keep-mcp` エントリが認識されることを確認（現時点では資格情報未設定のため `failed` 表示）

---

## [2026-03-25] 13:31 ViewServices参照の削除とHelpers統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Cvnet10Wpfclient` 配下の `using Cvnet10Wpfclient.ViewServices;` を削除し、XAML内の `ViewServices` 参照は不要なら削除、使用中なら `Cvnet10Wpfclient.Helpers` へ切り替える
### 実施内容
- Cvnet10Wpfclient/ViewModels 配下の各ViewModel: 未使用になっていた `using Cvnet10Wpfclient.ViewServices;` を一括削除
- Cvnet10Wpfclient/Helpers/MessageBoxView.xaml: `clr-namespace:Cvnet10Wpfclient.ViewServices` の `xmlns:local` 宣言を削除
- Cvnet10Wpfclient/Cvnet10Wpfclient.csproj: 残存していた `ViewServices\` フォルダー定義を削除
### 技術決定 Why
- `Cvnet10Wpfclient.ViewServices` 名前空間の実体が既に存在せず、XAML側でも当該名前空間の型利用がなかったため、要素参照の置換ではなく不要宣言の削除を優先した
- ユーザー指定の `CvcnetWpfclinet.Helpers` は実在せず、既存コードベースで一貫して使われている `Cvnet10Wpfclient.Helpers` を正とみなして整合性を維持した
### 確認
- `grep` にて `Cvnet10Wpfclient` 配下の `ViewServices` 参照が解消されたことを確認
- `dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false` 成功（0 warnings, 0 errors）

---
