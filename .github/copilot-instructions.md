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
- **Central package versions**: `Directory.Packages.props`
- **Code style baseline**: `.editorconfig`
- **XAML style baseline**: `Settings.XamlStyler`
- **Restore All Projects**: `dotnet restore "Cvnet10.slnx"`
- **Build Solution**: `dotnet build "Cvnet10.slnx"`
- **Build Server Project**: `dotnet build "Cvnet10Server/Cvnet10Server.csproj"`
- **Build Client Project (Linux OS/WSL2)**: `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false`
- **Format Check (Solution)**: `dotnet format "Cvnet10.slnx" --verify-no-changes`
- **[CRITICAL]**: Do not start ".net upgrade experience"

**[CRITICAL RULE]**: Keep dependencies layered and treat the following projects as read-only unless explicitly required:
- **CodeShare**
- **Cvnet10Asset**
- **Cvnet10Base** (read-only by default; modify only when clearly necessary)
- **Cvnet10BaseMariadb**
- **Cvnet10BaseOracle**
- **Cvnet10BaseSqlite**
- **Cvnet10Prints**


| Folder / Project(.csproj) | Layer | Responsibility | Allowed Dependencies |
| :--- | :--- | :--- | :--- |
| **CodeShare** | Layer 0 | [READ-ONLY] gRPC Contracts, DTOs, Shared Interfaces | None |
| **Cvnet10Asset** | Layer 0 | [READ-ONLY] Lightweight Utilities, Extensions, Constants | None |
| **Cvnet10Base** | Layer 1 | Data Models, DB Entities (NPoco) | None |
| **Cvnet10BaseMariadb** | Layer 1.2 | [READ-ONLY] Database Connection for MariaDB (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10BaseOracle** | Layer 1.2 | [READ-ONLY] Database Connection for Oracle (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10BaseSqlite** | Layer 1.2 | [READ-ONLY] Database Connection for Sqlite (Enhanced NPoco Database Class) | Cvnet10Base |
| **Cvnet10Prints** | Layer 1.4 | Print Logic | None |
| **Cvnet10DomainLogic** | Layer 1.5 | Business Logic, Domain Services, Calculations | Cvnet10Base |
| **Cvnet10Server** | Layer 2 | gRPC Service Implementations, DbContext(ExDatabase) by DI | CodeShare, Cvnet10Asset, Cvnet10Base, Cvnet10DomainLogic |
| **Cvnet10Wpfclient** | Layer 2 | WPF GUI (Views/ViewModels), gRPC Client Logic | CodeShare, Cvnet10Asset, Cvnet10Base |

refer/ Foloder and exist-Project : [READ-ONLY] [reference-Only] [No-Include This Solution] [reference for Designning Cvnet10Wpfclient UI]

## Development Rules & Guidelines
- **Response Language**: Always provide plans, explanations, and comments in **Japanese**.
- **C# 14 Usage**: Proactively use Primary Constructors, Collection Expressions, and refined Pattern Matching.
- **Implementation Style**: First inspect the target layer and related files, then implement with minimal diffs.
- **Formatting**: `.cs` follow `.editorconfig`, `.xaml` follow `Settings.XamlStyler`, use file-scoped namespaces, keep `using` directives outside namespaces, and do not sort `System` usings first if the local style differs.
- Ask the user only when required information is genuinely missing or ambiguous.
- **Database Operations**:
    - Strictly separate **CRUD** operations.
    - Encapsulate DB main-logic within the DomainLogic-side (Layer 1.5).
    - Ensure minimal impact on existing schemas when modifying logic.
- **Refactoring**: Analyze the impact range before proposing changes. Do not break existing implementations.
- **CAUTION**: WPF screens can be clipped on the bottom and right edges. Pay special attention to bottom-edge clipping.
- `.github/copilot/wpf_skill.md` is For Cvnet10Wpfclient Project, UI design and implementation guidelines
- When working on `Cvnet10Wpfclient`, first review `.github/copilot/wpf_skill.md`. If WPF resources or exceptions are involved, inspect `Cvnet10Wpfclient/App.xaml` and the referenced `ResourceDictionary` files first.


## Interaction Protocol
- **IMPORTANT!** Follow the workflow: **Analyze → Plan (TodoWrite) → Execute → Verify → Write-Log → Git-Commit** 

1. **Analyze**: Identify which layer the task belongs to.
2. **Plan (TodoWrite)**: Present a short Japanese plan and create a Todo list. Keep only one task `in_progress` at a time.
3. **Execute**: Write clean, maintainable code following Clean Architecture principles.
4. **Verify**: Ensure the `.slnx` file structure remains intact. Run the smallest relevant build and summarize impact and verification results clearly.
5. **Write-Log**: Write log file. follow Write-Log section.
6. **Git-Commit**: When committing, follow Git-Commit section.

## Write-Log
- When work is complete, always append an entry to the **end** of `Doc/aicording_log.md` using the format below.
- If adding a new entry would exceed 800 lines, rename the existing history file to `aicoding_log_[001-999].md` with the next sequential number, then create a new `aicording_log.md` and continue recording with the same format.
'''
## [YYYY-MM-DD] hh:mm Work Title
### Agent
- [AI Model Name Used : AI Provider Name]
### Editor
- [Editor used: if unknown, use "VS2026"; examples: "VS2026", "VSCode", "OpenCode", "GitHubCopilot-Cli"] 
### Objective
- User request: [details]
### Work Performed
- [Project Name]/[File Name]: [summary of changes]
### Technical Decision Why
- [Example: Added the new feature while preserving the existing flag definitions to avoid gaps in Protobuf Order values]
### Impact Scope (Optional)
- For large changes, describe the impact scope. Omit this section when only the modified files are affected.
### Verification
- [Record the build result. For cross-platform cases, this may be omitted if build errors are expected]

---
'''

## Git-Commit
- When committing, include the following:
'''
[Work summary]
[AI Model Name Used : AI Provider Name : Agent Name]
Work time [Start Time] - [End Time] : [Duration] (**record in JST**)
[Brief summary of the user request]
'''
Example)
'''
Update SelectKubunView.xaml to the MaterialDesign style
GPT-5.4-mini : OpenAI : Build
16:00 - 17:30 : 1 hour 30 minutes
Unify the SelectKubunView design with the MasterMeisho design
'''
