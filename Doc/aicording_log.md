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

## [2026-03-27] 10:05 WPFスキル分離と参照整理
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`AGENTS.md` からWPF作成・改修ルールを分離し、Cvnet10Wpfclient全体向けスキルと個別View/ViewModel向けスキルに整理したうえで、`AGENTS.md` から必須参照させる
### 実施内容
- `.agents/skills/wpf-project-guide/SKILL.md`: `Cvnet10Wpfclient` 全体の共通規約、ResourceDictionary確認、`BaseWindow` 運用、検証方針を新規追加した
- `.agents/skills/wpf-view-workflow/SKILL.md`: 個別画面の新規作成・既存改修・`MenuData.cs` 連携・検証手順を新規追加した
- `AGENTS.md`: WPF詳細規約を縮約し、`wpf-project-guide` と `wpf-view-workflow` の必須参照、および既存WPF系スキルの使い分けに更新した
- `.agents/skills/check-xaml/SKILL.md`: 検証専用スキルである位置付けを追記した
- `.agents/skills/update-design-mente/SKILL.md`: 新しいWPF共通スキル群を前提とする説明を追記した
- `.agents/skills/change-sublist-to-observablecollection/SKILL.md`: 新しいWPF共通スキル群を前提とする説明を追記した
### 技術決定 Why
- `Cvnet10Wpfclient` 全体の前提知識と、個別画面の作成・改修手順は責務が異なるため、共通ガイドと画面ワークフローへ分離した
- 既存のWPF系スキルは用途特化のまま残し、新規2スキルを上位ガイドとして位置付けることで重複と参照迷いを減らした
### 確認
- 変更後ファイルを読み返し、`AGENTS.md` から `wpf-project-guide` / `wpf-view-workflow` の必須参照になっていることを確認
- ドキュメントとスキル定義のみの変更のため、ビルドは未実行

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

## [2026-03-27] 10:26 copilot-instructions整合
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`AGENTS.md` の内容に合わせて `copilot-instructions.md` を最小限で修正する
### 実施内容
- `.github/copilot-instructions.md`: リポジトリ情報、復元・ビルド・フォーマット確認コマンド、レイヤー/読み取り専用ルール、最小差分方針、WPF参照ルール、Write-Log/Git-Commit 記述を `AGENTS.md` に合わせて最小限で補正した
### 技術決定 Why
- 既存の英語ベース構成は維持しつつ、`AGENTS.md` と不整合だった閾値、手順、参照先、運用ルールのみを補正して差分を最小化した
### 確認
- 変更後の `.github/copilot-instructions.md` を読み返し、`AGENTS.md` との差分が主に不足事項の補完に留まっていることを確認
- ドキュメント変更のみのため、ビルドは未実行

## [2026-03-27] 10:32 copilot-instructions英語統一
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`.github/copilot-instructions.md` の記述を英語に統一し、作業後に変更全体をコミットする
### 実施内容
- `.github/copilot-instructions.md`: コードブロック外の日本語記述を英語へ置換し、英語化の過程で残っていた日本語見出しを解消した
### 技術決定 Why
- ユーザー指定どおりコードブロックは変更対象にせず、通常本文だけを英語化して既存テンプレート構造への影響を抑えた
### 確認
- `grep` により `.github/copilot-instructions.md` のコードブロック外に日本語が残っていないことを確認
- ドキュメント変更のみのため、ビルドは未実行

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
