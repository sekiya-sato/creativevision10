# AGENTS.md - OpenCode AI Agent Instructions

## Tooling & Environment
- **Roles**: OpenCode (Complex/Multi-file/Docs), Copilot (Inline/Small edits).
- **Stack**: .NET 10, C# 14, gRPC (protobuf-net.Grpc), WPF (MVVM, CommunityToolkit).
- **Files**: Solution `Cvnet10.slnx`. Use UTF-8 (No BOM) & CRLF.
- **Python**: Use Python 3 if needed.

## Priority Workflow (IMPORTANT)
**Analyze → Plan (TODO-LIST) → Execute → Verify → Write-Log → Git-Commit**
- Language: Plans, explanations, and comments must be in **JAPANESE**.
- Task Mgmt: Only ONE `in_progress` task at a time.
- Preparation: Use `git stash` before work; create a memo in `.sisyphus/` for complex tasks.
- Search: Use `grep -r` for Japanese terms.

## Architecture
- **Read-Only**: Layer 0 (`CodeShare`/`Cvnet10Asset`), Layer 1 (`Cvnet10Base`), Layer 1.2 (DB), Layer 1.4 (Prints)  Write if necessary.
- **Server Layering**: (0) -> (1-1.4) -> `Cvnet10DomainLogic` (1.5) -> `Cvnet10Server` (2).
- **Client Layering**: (0) -> (1) -> `Cvnet10Wpfclient`(2).

## Build Commands **IMPORTANT**
Condition:
- If `NAME=DESKTOP-LV37IKB` or `NAME=HOME20230223` -> Use ### Build Rule1
- Else -> Use ### Build Rule2

### Build Rule1
- Restore all projects: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet restore Cvnet10.slnx"`
- Build solution: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10.slnx"`
- Build server only: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Server/Cvnet10Server.csproj"`
- Build WPF client: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"`

### Build Rule2
- Restore all projects: `dotnet restore "Cvnet10.slnx"`
- Build solution: `dotnet build "Cvnet10.slnx"`
- Build server only: `dotnet build "Cvnet10Server/Cvnet10Server.csproj"`
- Build WPF client: `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false`

## Coding & WPF Standards
- **Style**: `.editorconfig` (CS), `Settings.XamlStyler` (XAML). File-scoped namespaces.
- Use **UTF-8**
- **WPF Work**: Load `wpf-project-guide`. Inspect `App.xaml` & `ResourceDictionary` first for UI issues.
- **Tools**: Use `check-xaml`, `update-design-mente`, `change-sublist-to-observablecollection` appropriately.

## Post-Task Requirements (Log & Commit)
- **Log**: Append to `Doc/aicording_log.md`. Archive to `aicoding_log_[NNN].md` if > 800 lines.
- **Log Format**: `## [Date] Time Title`, `Agent`, `Editor`, `Purpose`, `Details`, `Reasoning(Why)`, `Verification`.
- **Commit Message**:

### Write-Log
- **Log**: Append to `Doc/aicording_log.md`. Archive to `aicoding_log_[NNN].md` if > 800 lines.
- **Log Format** and **Archiving Rule**:
'''
## [YYYY-MM-DD] hh:mm 作業タイトル
### Agent
- [使用した AI Model 名 : AI Provider 名]
### Editor
- [使用したエディタ: 不明な場合は"VS2026", 例 "VS2026", "VSCode", "OpenCode", "GitHubCopilot-Cli"] 
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [プロジェクト名]/[ファイル名]: [変更内容の要約]
### 技術決定 Why
- [例: ProtobufのOrder欠番を避けるため、既存のFlag定義を維持しつつ新機能を追加した]
### 影響範囲 (省略可)
- 大規模変更の場合は影響範囲を明記。修正したファイルのみの場合は省略
### 確認
- [Buildした結果を確認。クロスプラットフォームの場合はBuild Error がでる可能性があるので省略可]

---
'''

### Git-Commit
- **Commit Message Format**:
'''
[作業内容]
[使用した AI Model 名 : AI Provider 名 : エージェント名]
作業時間 [開始時間] - [終了時間] : [作業時間] (**日本時間JSTで記録すること**)
[ユーザ指示の概略]
'''
例)
'''
SelectKubunView.xamlのMaterialDesignスタイルへの変更
GPT-5.4-mini : OpenAI : Build
16:00 - 17:30 : 1時間30分
SelectKubunView のデザインをMasterMeishoのデザインに統一する
'''

