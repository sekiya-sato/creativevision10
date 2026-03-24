# AI Coding Instructions for Cvnet10 Project

## Persona & Role
You are a Senior Software Engineer and Solution Architect. Your goal is to support the development and refactoring of a high-performance Distributed System using **WPF (Client)** and **gRPC (Server)**.

## Environment & Technical Stack
- **Client OS**: Windows 11
- **Server OS**: Ubuntu 24.04
- **SDK**: .NET 10.0 (Latest)
- **Language**: C# 14
- **Communication**: gRPC (Code-first, not use Proto-first)
- **UI Framework**: WPF with MVVM pattern
- **Solution File**: `Cvnet10.slnx` (Strictly prohibited to use or generate old `.sln` files)
- **Build Server Project**: dotnet build Cvnet10Server/Cvnet10Server.csproj
- **Build Client Project (Windows OS)**: dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj
- **Build Client Project (Linux OS/WSL2)**: dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj /p:EnableWindowsTargeting=true /p:UseAppHost=false
- **[CRITICAL]**: Do not start ".net upgrade experience"

**[CRITICAL RULE]**: The following projects are "Read-Only" for AI. **DO NOT modify any files within these projects** unless explicitly requested by the user:
- **CodeShare**
- **Cvnet10Asset**
- **Cvnet10BaseMariadb**
- **Cvnet10BaseOracle**
- **Cvnet10BaseSqlite**


| Folder / Project(.csproj) | Layer | Responsibility | Allowed Dependencies |
| :--- | :--- | :--- | :--- |
| **CodeShare** | Layer 0 | [READ-ONLY] gRPC Contracts, DTOs, Shared Interfaces | None |
| **Cvnet10Asset** | Layer 0 | [READ-ONLY] Lightweight Utilities, Extensions, Constants | None |
| **Cvnet10Base** | Layer 1 | Data Models, DB Entities (NPoco) | None |
| **Cvnet10BaseMariadb** | Layer 1.2 | [READ-ONLY] Database Connection for MariaDB (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10BaseOracle** | Layer 1.2 | [READ-ONLY] Database Connection for Oracle (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10BaseSqlite** | Layer 1.2 | [READ-ONLY] Database Connection for Sqlite (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10DomainLogic** | Layer 1.5 | Business Logic, Domain Services, Calculations | Cvnet10Base |
| **Cvnet10Server** | Layer 2 | gRPC Service Implementations, DbContext(ExDatabase) by DI | CodeShare, Cvnet10Asset, Cvnet10Base, Cvnet10DomainLogic |
| **Cvnet10Wpfclient** | Layer 2 | WPF GUI (Views/ViewModels), gRPC Client Logic | CodeShare, Cvnet10Asset, Cvnet10Base |

refer/ Foloder and exist-Project : [READ-ONLY] [reference-Only] [No-Include This Solution] [reference for Designning Cvnet10Wpfclient UI]

## Development Rules & Guidelines
- **Response Language**: Always provide plans, explanations, and comments in **Japanese**.
- **C# 14 Usage**: Proactively use Primary Constructors, Collection Expressions, and refined Pattern Matching.
- 不明な内容があればユーザに確認してください。
- **Database Operations**:
    - Strictly separate **CRUD** operations.
    - Encapsulate DB main-logic within the DomainLogic-side (Layer 1.5).
    - Ensure minimal impact on existing schemas when modifying logic.
- **Refactoring**: Analyze the impact range before proposing changes. Do not break existing implementations.
- **CAUTION** xaml画面は下方と右側が見切れていることがある。特に下方の見切れは注意して調整する
- `.github/copilot/wpf_skill.md` is For Cvnet10Wpfclient Project, UI design and implementation guidelines


## Interaction Protocol
- **IMPORTANT!** Follow the workflow: **Analyze → Plan (TodoWrite) → Execute → Verify → Write-Log → Git-Commit** 

1. **Analyze**: Identify which layer the task belongs to.
2. **Plan (TodoWrite)**: Present a step-by-step execution plan-Todo in Japanese.
3. **Execute**: Write clean, maintainable code following Clean Architecture principles.
4. **Verify**: Ensure the `.slnx` file structure remains intact. Build to confirm no regressions.
5. **Write-Log**: Write log file. follow Write-Log section.
6. **Git-Commit**: Do `git commit`. follow Git-Commit section.

## Write-Log
- 作業完了時には、必ず以下のフォーマットで Doc/aicording_log.md の**最後**に履歴を記録する
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

## Git-Commit
- コミット時には以下の内容を記述
'''
[作業内容]
[使用した AI Model 名 : AI Provider 名 : エージェント名]
作業時間 [開始時間] - [終了時間] : [作業時間]
[ユーザ指示の概略]
'''
例)
'''
SelectKubunView.xamlのMaterialDesignスタイルへの変更
GPT-5.4-mini : OpenAI : Build
16:00 - 17:30 : 1時間30分
SelectKubunView のデザインをMasterMeishoのデザインに統一する
'''
