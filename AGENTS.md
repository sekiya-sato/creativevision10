# AGENTS.md - OpenCode AI Agent Instructions

## AI Tool Separation Policy
- **OpenCode**: Use for multi-file features, large-scale refactoring, cross-project changes, documentation, troubleshooting
- **GitHub Copilot**: Use for inline completion, quick fixes, small edits within Visual Studio 2026
- When `AGENTS.md` is updated, `copilot-instructions.md` will also be updated accordingly
- If an AI Tool wants to use Python, please use Python 3

## Repository Snapshot
- Solution file: `Cvnet10.slnx` (The main stack is gRPC server + WPF client (MVVM pattern) + shared/domain/data projects)
- Server: `Cvnet10Server` (`net10.0`, ASP.NET Core, protobuf-net.Grpc)
- Client: `Cvnet10Wpfclient` (`net10.0-windows`, WPF, CommunityToolkit.Mvvm, MaterialDesignThemes)
- Central package versions: `Directory.Packages.props`
- Code style baseline: `.editorconfig`
- Xaml style baseline: `Settings.XamlStyler`

## High Priority OpenCode Rules
- Write plans, explanations, and code comments in **JAPANESE**
- **IMPORTANT!** FOLLOW THE WORKFLOW: **Analyze → Plan (Todo-List) → Execute → Verify → Write-Log → Git-Commit** 
- In Plan, make sure to create **TODO-LIST**
- Use .NET 10 and C# 14 features where they improve clarity
- Mark todos as `in_progress` when starting work, `completed` immediately after finishing
- Only have ONE task `in_progress` at a time
- Line endings: CRLF only
- Encoding: UTF-8 without BOM

## Read-Only and Layering Rules
- Keep dependencies layered:
  - Layer 0: `CodeShare`, `Cvnet10Asset` : read-only
  - Layer 1: `Cvnet10Base` : read-only (if need, write)
  - Layer 1.2: DB provider projects `Cvnet10BaseMariadb`(`Cvnet10Base.Mariadb` Folder), `Cvnet10BaseOracle`(`Cvnet10Base.Oracle` Folder), `Cvnet10BaseSqlite`(`Cvnet10Base.Sqlite` Folder) : read-only
  - Layer 1.4: `Cvnet10Prints` : read-only
  - Layer 1.5: `Cvnet10DomainLogic`
  - Layer 2: `Cvnet10Server`, `Cvnet10Wpfclient`

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

## Lint / Format Commands
- Full repo format check: `dotnet format "Cvnet10.slnx" --verify-no-changes`
- Full repo format fix: `dotnet format "Cvnet10.slnx"`

## Expected Development Workflow
- First inspect the target layer and related files.
- Then present a short Japanese plan.
- Implement with minimal diffs.
- If the work targets `Cvnet10Wpfclient`, always load `wpf-project-guide` first.
- If WPF resources or exceptions are involved, inspect `Cvnet10Wpfclient/App.xaml` and referenced `ResourceDictionary` files first.

## Formatting Conventions
- `.cs` follow `.editorconfig`.
- `.xaml` follow `Settings.XamlStyler`.
- Use file-scoped namespaces.
- Put `using` directives outside the namespace.
- Do not sort `System` usings first; keep the existing local ordering style.

## WPF / MVVM Conventions
- For any `Cvnet10Wpfclient` work, always load `wpf-project-guide` and follow it as the source of truth for shared WPF conventions.
- For creating or updating an individual WPF screen, also load `wpf-view-workflow`.
- Use `check-xaml` when validating XAML syntax, resources, converters, or binding paths.
- Use `update-design-mente` when aligning master maintenance screens to the shared MaterialDesign-based layout.
- Use `change-sublist-to-observablecollection` when a master maintenance sub-list uses `List<T>` and row add/delete changes are not reflected in the UI.

## Pre-Completion Checklist
- Confirm you did not modify read-only projects unintentionally.
- Run the smallest relevant build.
- Summarize impact and verification results clearly.

## Write-Log
- Upon completion of the task, be sure to record the history at `Doc/aicording_log.md` using the following format.
- If adding the history results in more than 800 lines, rename the existing history file to aicoding_log_[001-999].md with sequential numbers, and create a new `aicording_log.md` with the same format to record the history.
- - The following `記録フォーマット` and `アーカイブルール` are included at the beginning of `aicording_log.md`.
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

## Git-Commit
- When committing, include the following
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

 
