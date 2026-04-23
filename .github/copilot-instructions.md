# AI Coding Instructions for Cv Project

## Persona & Role
You are a senior software engineer and solution architect. Your role is to support the development and refactoring of a high-performance distributed system built with **WPF (client)** and **gRPC (server)**.

## Environment & Technical Stack
- **Client OS**: Windows 11
- **Server OS**: Ubuntu 24.04
- **SDK**: .NET 10.0 (Latest)
- **Language**: C# 14
- **Communication**: gRPC (code-first, not proto-first)
- **UI Framework**: WPF with MVVM pattern
- **Solution File**: `Cv.slnx` (do not use or generate legacy `.sln` files)
- **Central package versions**: `Directory.Packages.props`
- **Code style baseline**: `.editorconfig`
- **XAML style baseline**: `Settings.XamlStyler`
- **Restore All Projects**: `dotnet restore "Cv.slnx"`
- **Build Solution**: `dotnet build "Cv.slnx"`
- **Build Server Project**: `dotnet build "CvServer/CvServer.csproj"`
- **Format Check (Solution)**: `dotnet format "Cv.slnx" --verify-no-changes`
- **[CRITICAL]**: Do not start ".net upgrade experience"

**[CRITICAL RULE]**: Keep dependencies layered and treat the following projects as read-only unless explicitly required:
- **CodeShare**
- **CvAsset**
- **CvBase** (read-only by default; modify only when clearly necessary)
- **CvBaseMariadb**
- **CvBaseOracle**
- **CvBaseSqlite**
- **CvPrints**


| Folder / Project(.csproj) | Layer | Responsibility | Allowed Dependencies |
| :--- | :--- | :--- | :--- |
| **CodeShare** | Layer 0 | [READ-ONLY] gRPC Contracts, DTOs, Shared Interfaces | None |
| **CvAsset** | Layer 0 | [READ-ONLY] Lightweight Utilities, Extensions, Constants | None |
| **CvBase** | Layer 1 | Data Models, DB Entities (NPoco) | None |
| **CvBaseMariadb** | Layer 1.2 | [READ-ONLY] Database Connection for MariaDB (Enhanced NPoco Database Class) | CvBase |
| **CvBaseOracle** | Layer 1.2 | [READ-ONLY] Database Connection for Oracle (Enhanced NPoco Database Class) | CvBase |
| **CvBaseSqlite** | Layer 1.2 | [READ-ONLY] Database Connection for Sqlite (Enhanced NPoco Database Class) | CvBase |
| **CvPrints** | Layer 1.4 | Print Logic | None |
| **CvDomainLogic** | Layer 1.5 | Business Logic, Domain Services, Calculations | CvBase |
| **CvServer** | Layer 2 | gRPC Service Implementations, DbContext(ExDatabase) by DI | CodeShare, CvAsset, CvBase, CvDomainLogic |
| **CvWpfclient** | Layer 2 | WPF GUI (Views/ViewModels), gRPC Client Logic | CodeShare, CvAsset, CvBase |

Reference folders and existing projects: [READ-ONLY] [REFERENCE-ONLY] [NOT INCLUDED IN THIS SOLUTION] [used as design references for `CvWpfclient` UI work]

## Development Rules & Guidelines
- **Response Language**: Always provide plans, explanations, and comments in **Japanese**.
- **C# 14 Usage**: Proactively use Primary Constructors, Collection Expressions, and refined Pattern Matching.
- **Implementation Style**: First inspect the target layer and related files, then implement with minimal diffs.
- **Formatting**: Follow `.editorconfig` for `.cs` files and `Settings.XamlStyler` for `.xaml` files. Use file-scoped namespaces, keep `using` directives outside the namespace, and do not move `System` usings to the top if the local style differs.
- Ask the user only when required information is genuinely missing or ambiguous.
- **Refactoring**: Analyze the impact range before proposing changes. Do not break existing implementations.
- **CAUTION**: WPF screens can be clipped on the bottom and right edges. Pay special attention to bottom-edge clipping.
- `.github/copilot/wpf_skill.md` contains the UI design and implementation guidelines for `CvWpfclient`.
- When working on `CvWpfclient`, first review `.github/copilot/wpf_skill.md`. If WPF resources or exceptions are involved, inspect `CvWpfclient/App.xaml` and the referenced `ResourceDictionary` files first.


## Interaction Protocol
- **IMPORTANT!** Follow this workflow: **Analyze → Plan (TodoWrite) → Execute → Verify → Write-Log → Git-Commit**

1. **Analyze**: Identify which layer the task belongs to.
2. **Plan (TodoWrite)**: Present a short plan in Japanese and create a todo list. Keep only one task `in_progress` at a time.
3. **Execute**: Write clean, maintainable code following Clean Architecture principles.
4. **Verify**: Ensure the `.slnx` file structure remains intact. Run the smallest relevant build and summarize impact and verification results clearly.
5. **Write-Log**: Update the log file by following the Write-Log section.
6. **Git-Commit**: When committing, follow the Git-Commit section.

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

## graphify

This project has a graphify knowledge graph at graphify-out/.

Rules:
- Before answering architecture or codebase questions, read graphify-out/GRAPH_REPORT.md for god nodes and community structure
- If graphify-out/wiki/index.md exists, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files

