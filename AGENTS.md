# AGENTS.md - OpenCode AI Agent Instructions

## Purpose
- **This file is specifically designed for OpenCode AI agents** working in `/mnt/gitroot/new2022/cvnet10`.
- For GitHub Copilot, see `.github/copilot-instructions.md` instead.
- Prefer small, reviewable changes that match existing C# 14 / .NET 10 conventions.
- The main stack is gRPC server + WPF client + shared/domain/data projects.

## AI Tool Separation Policy
- **OpenCode**: Use for multi-file features, large-scale refactoring, cross-project changes, documentation, troubleshooting
- **GitHub Copilot**: Use for inline completion, quick fixes, small edits within Visual Studio 2026
- Both tools share the same logging format in `doc/aicording_log.md`

## Repository Snapshot
- Solution file: `Cvnet10.slnx`.
- Server: `Cvnet10Server` (`net10.0`, ASP.NET Core, protobuf-net.Grpc).
- Client: `Cvnet10Wpfclient` (`net10.0-windows`, WPF, CommunityToolkit.Mvvm, MaterialDesignThemes).
- Central package versions: `Directory.Packages.props`.
- Code style baseline: `.editorconfig`.
- Xaml style baseline: `Settings.XamlStyler`.

## Rule Sources
- Copilot instructions exist in `.github/copilot-instructions.md` and must be followed.
- Additional agent guidance exists in `.github/copilot/Orchestrator.agent.md`, `.github/copilot/Planner.agent.md`, `.github/copilot/Impl.agent.md`, `.github/copilot/Reviewer.agent.md`, and `.github/copilot/wpf_skill.md`.

## High Priority Copilot Rules
- Write plans, explanations, and code comments in Japanese.
- Follow the workflow: Analyze -> Plan -> Execute -> Verify.
- Use .NET 10 and C# 14 features where they improve clarity.

## High Priority OpenCode Rules
- **Always use TodoWrite tool** to plan and track multi-step tasks (3+ steps or complex tasks)
- Write plans, explanations, and code comments in **Japanese**
- Follow the workflow: **Analyze → Plan (TodoWrite) → Execute → Verify → Log**
- Use .NET 10 and C# 14 features where they improve clarity
- Mark todos as `in_progress` when starting work, `completed` immediately after finishing
- Only have ONE task `in_progress` at a time

## Read-Only and Layering Rules
- Keep dependencies layered:
  - Layer 0: `CodeShare`, `Cvnet10Asset` : read-only
  - Layer 1: `Cvnet10Base` : read-only (if need, write)
  - Layer 1.2: DB provider projects : read-only
  - Layer 1.5: `Cvnet10DomainLogic`
  - Layer 2: `Cvnet10Server`, `Cvnet10Wpfclient`
- Do not move business logic into the WPF client when it belongs in server/domain layers.
- **[CRITICAL RULE]**: The following projects are "Read-Only" for AI. **DO NOT modify any files within these projects** unless explicitly requested by the user:
  - **CodeShare**
  - **Cvnet10Asset**
  - **Cvnet10Base**
  - **Cvnet10BaseMariadb**
  - **Cvnet10BaseOracle**
  - **Cvnet10BaseSqlite**

## Restore / Build Commands
- Restore all projects: `dotnet restore "Cvnet10.slnx"`
- Build solution: `dotnet build "Cvnet10.slnx"`
- Build server only: `dotnet build "Cvnet10Server/Cvnet10Server.csproj"`
- Build WPF client on Windows: `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj"`
- Build WPF client on Linux/WSL2: `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false`

## Lint / Format Commands
- Full repo format check: `dotnet format "Cvnet10.slnx" --verify-no-changes`
- Full repo format fix: `dotnet format "Cvnet10.slnx"`
- Project format check: `dotnet format "Cvnet10Server/Cvnet10Server.csproj" --verify-no-changes`
- WPF format check: `dotnet format "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" --verify-no-changes`

## Expected Development Workflow
- First inspect the target layer and related files.
- Then present a short Japanese plan.
- Implement with minimal diffs.
- If WPF resources or exceptions are involved, inspect `Cvnet10Wpfclient/App.xaml` and referenced `ResourceDictionary` files first.

## Formatting Conventions
- Use UTF-8.
- Use CRLF line endings.
- Use tabs for indentation in `.cs` and `.xaml` files; tab width is 4.
- Insert a final newline at EOF.
- Use file-scoped namespaces.
- Put `using` directives outside the namespace.
- Do not sort `System` usings first; keep the existing local ordering style.
- Opening braces stay on the same line as declarations/control statements.

## WPF / MVVM Conventions
- ViewModels should use `ObservableObject`, `ObservableRecipient`, or the existing base ViewModel types.
- Use `[ObservableProperty]` for state and `[RelayCommand]` / async relay commands for commands.
- Keep code-behind minimal: initialization and UI-specific event delegation only.
- For business/master windows, use `Cvnet10Wpfclient.Helpers.BaseWindow` rather than raw `Window`.
- `BaseWindow` already triggers `InitCommand`, handles Escape close behavior, and runs `*CancelCommand` on close.
- Therefore, do not add a `ContentRendered` trigger for `InitCommand` when the view derives from `BaseWindow`.
- Check `Cvnet10Wpfclient/App.xaml` merged dictionaries before editing resources.
- Existing merged dictionaries include `Resources/UIColors.xaml`, `Resources/UICommon.xaml`, and `Resources/UIMainWindow.xaml`.
- Use `DynamicResource` for theme-aware WPF colors and shared brushes.
- When asked to add a new `*View.xaml`, also add the corresponding `*ViewModel.cs` and wire menu entry changes in `Cvnet10Wpfclient/Models/MenuData.cs` if the feature should be launchable.

## Pre-Completion Checklist
- Confirm you did not modify read-only projects unintentionally.
- Confirm `Cvnet10.slnx` still exists and was not replaced with `.sln`.
- Run the smallest relevant build.
- Run targeted tests when possible.
- If you changed WPF resources, verify `App.xaml` references still resolve.
- **TodoWrite tool で全タスクが completed になっているか確認**
- **大規模変更の場合、影響範囲を doc/ に記録したか確認**
- Summarize impact and verification results clearly.

## After-Completion
- 作業完了時には、必ず以下のフォーマットで doc/aicording_log.md の**最後**に履歴を記録する
- 履歴を追加すると400行をこえる場合、既存の履歴ファイルをaicoding_log_[001-999].mdとして連番でリネーム保存し、新規に同様の書式で aicording_log.md を作成し履歴を記録する。
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
### 確認
- [Buildした結果を確認。クロスプラットフォームの場合はBuild Error がでる可能性があるので省略可]

---
'''

 
