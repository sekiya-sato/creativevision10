# AGENTS.md - OpenCode AI Agent Instructions

## Tooling & Environment
- **Roles**: OpenCode (Complex), Copilot (Inline/Small edits), Codex(General).
- **Stack**: .NET 10, C# 14, gRPC (protobuf-net.Grpc), WPF (MVVM, CommunityToolkit).
- **Files**: Solution `creativevision10.slnx`.
- - **Line Endings**: Every edited or created file **MUST** use **CR+LF (`\r\n`)** as the line ending. Do not mix or use LF/CR.

## Priority Workflow (IMPORTANT)
**Analyze → Plan (TODO-LIST) → Execute → Verify → Write-Log → Git-Commit**
- Language: Plans, explanations, and comments must be in **JAPANESE**.
- Task Mgmt: Only ONE `in_progress` task at a time.
- Preparation: Use `git stash` before work; create a memo in `.omo/` for complex tasks.
- Search: Use `grep -r` for Japanese terms.
- Database: Use `sqlite3` for `server-user163.db` files; avoid direct file edits.

## SkillOpt-Based Skill Maintenance
- **Evidence-Driven Updates**: Treat `.agents/skills/*/SKILL.md` as the trainable state. Improve it via actual execution evidence (requests, skills, touched files, tool outputs, results, failure modes) rather than broad prompt rewrites.
- **Separate Reflection**: Fix recurring failures while strictly preserving successful procedures.
- **Bounded Edits & Splitting**: Limit changes to minimal add/delete/replace actions. Separate generic workflows from feature-specific troubleshooting based on reuse boundaries.
- **Held-Out Validation**: Gate all edits using unseen test cases. Reject changes that fix the target case but cause regressions in other representative cases.
- **Isolate Scratches**: Keep rejection reasons and analysis notes in `.omo/`. Deploy only the compact final `SKILL.md` unless research notes are explicitly requested.
- **No Auto-Tooling**: Do not automatically install or run external SkillOpt tools. Apply the SkillOpt method as a disciplined, local workflow.

## Architecture
- **Read-Only**: Layer 0 (`CodeShare`/`CvAsset`), Layer 1 (`CvBase`), Layer 1.2 (DB), Layer 1.4 (Prints)  Write if necessary.
- **Server Layering**: (0) -> (1-1.4) -> `CvDomainLogic` (1.5) -> `CvServer` (2).
- **Client Layering**: (0) -> (1) -> `CvWpfclient`(2).

## Build Rule (WSL2) **IMPORTANT**
- Build solution: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx"`
- Build server only: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"`
- Build WPF client: `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"`

## Coding & WPF Standards
- **Style**: `.editorconfig` (CS), `Settings.XamlStyler` (XAML). File-scoped namespaces.
- Use **UTF-8** (qfm files is SJIS)
- **WPF Work**: Load `wpf-project-guide`. Inspect `App.xaml` & `ResourceDictionary` first for UI issues.
- **Tools**: Use `check-xaml`, `update-design-mente`, `change-sublist-to-observablecollection` appropriately.
- Avoid excessive dependency injection.
- Don’t add test programs unless explicitly asked.

## Post-Task Requirements (Log & Commit)
- **Log**: Append to `Doc/aicoding_log.md`. Always append to the end. Archive to `aicoding_log_[NNN].md` if > 800 lines.
- **Log Format**: Folow "Log-Format" section below.**Append to the end, Newest is last.**
- **Commit Format**: Folow "Commit-Format" section below.

### Log-Format
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

### Commit-Format
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

## graphify

This project has a graphify knowledge graph at graphify-out/.

Rules:
- Before answering architecture or codebase questions, read graphify-out/GRAPH_REPORT.md for god nodes and community structure
- For architecture, relationship, impact-scope, or dependency questions, always start by reading `graphify-out/GRAPH_REPORT.md` before using other search methods
- If graphify-out/wiki/index.md exists, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files
