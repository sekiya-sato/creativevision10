# AGENTS.md - Cvnet10 Project Guidelines

## 1. Build & Test Commands

### Build
```cmd /c 
# Build entire solution
dotnet build Cvnet10.slnx

# Build specific project
dotnet build Cvnet10Server/Cvnet10Server.csproj
dotnet build Cvnet10Wpfclient/Cvnet10Wpfclient.csproj
```

### Test
```cmd /c 
# Run all tests
dotnet test

# Run specific test project
dotnet test Tests.Cvnet10Server/Tests.Cvnet10Server.csproj

# Run single test (MSTest)
dotnet test --filter "FullyQualifiedName~Namespace.TestClassName.TestMethodName"
dotnet test --filter "FullyQualifiedName~TestClassName"

# Run with verbose output
dotnet test --verbosity detailed
```

---

## 2. Solution & Project Structure

- **Solution File:** `Cvnet10.slnx` (NEVER use or generate `.sln` files)
- **SDK:** .NET 10.0
- **Language:** C# 14

### Project Layers

| Project | Layer | Description |
|---------|-------|-------------|
| CodeShare | L0 | [READ-ONLY] gRPC Contracts, DTOs |
| Cvnet10Asset | L0 | [READ-ONLY] Utilities, Constants |
| Cvnet10Base | L1 | Data Models, DB Entities |
| Cvnet10BaseSqlite/Oracle/MariaDB | L1.2 | [READ-ONLY] DB Connections |
| Cvnet10DomainLogic | L1.5 | Business Logic, Domain Services |
| Cvnet10Server | L2 | gRPC Server Implementation |
| Cvnet10Wpfclient | L2 | WPF Client (Views/ViewModels) |
| Tests.* | - | MSTest Test Projects |

**[CRITICAL]** Do NOT modify Layer 0 projects (CodeShare, Cvnet10Asset, Cvnet10Base, Cvnet10Base*) unless explicitly requested by user.

---

## 3. Code Style Guidelines

### Language Features
- Use **C# 14** features: Primary Constructors, Collection Expressions `[]`, Pattern Matching
- Use `record` for immutable DTOs, `sealed class` for classes not designed for inheritance

### JSON Serialization
- **[CRITICAL]** Use `Newtonsoft.Json` ONLY - NEVER use `System.Text.Json`

### WPF/MVVM (Cvnet10Wpfclient)
- Use `CommunityToolkit.Mvvm`
- ViewModel: inherit from `ObservableObject`
- Properties: `[ObservableProperty]` attribute
- Commands: `[RelayCommand]` or `IAsyncRelayCommand`
- Bindings: Use `Mode=TwoWay` explicitly when needed
- Window: Use `helpers:BaseWindow` for business screens (NOT raw `Window`)

### Database (NPoco)
- Use `NPoco` for lightweight ORM
- Encapsulate DB logic in Cvnet10DomainLogic (Layer 1.5)
- Separate CRUD operations clearly

### gRPC
- Use `protobuf-net.Grpc` (code-first, NOT proto-first)
- Define contracts in CodeShare project

### Error Handling
- Use appropriate exception types: `ArgumentNullException.ThrowIfNull()`
- Never swallow exceptions silently - log them
- Prefer validation over exception handling for flow control

### Naming Conventions
- Classes/Methods/Properties: `PascalCase`
- Fields/Local variables: `camelCase`
- Interfaces: `I` prefix (e.g., `ICvnetCore`)
- ViewModels: `XxxViewModel`
- Views: `XxxView`

---

## 4. Dependencies & Libraries

| Purpose | Library |
|---------|---------|
| Communication | protobuf-net.Grpc |
| WPF/MVVM | CommunityToolkit.Mvvm |
| Database | NPoco |
| JSON | Newtonsoft.Json |
| Auth | Microsoft.AspNetCore.Authentication.JwtBearer |
| Logging | NLog.Web.AspNetCore |

---

## 5. Development Workflow

1. **Analyze**: Identify which layer the task belongs to
2. **Plan**: Present step-by-step execution plan in Japanese
3. **Execute**: Write clean code following Clean Architecture
4. **Verify**: Build and test to confirm no regressions

### Important Rules
- Response Language: Japanese for plans/explanations/comments
- Do NOT start ".NET Upgrade Experience"
- Maintain `.slnx` file structure
- Analyze impact range before proposing refactoring
- Must Use Windows line endings style (CR+LF) `*.cs`  `*.xaml`

---

## 6. Testing

- **Framework:** MSTest
- **Test Location:** `Tests.Cvnet10Server/` directory
- **Run single test:**
  ```cmd /c
  dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
  ```
