---
name: fix-cvserver-sqlite-lifetime
description: Investigate and fix cv10 CvServer/CvBaseSqlite database lifetime and SQLite shutdown cleanup issues. Use when work mentions ExDatabase lifetime, AddScoped/AddSingleton, SchedulerService DB access, CloneDb removal, ClearPools, WAL/SHM sidecar cleanup, ApplicationStopping, or Tests/TestServer runtime verification.
---

# Fix CvServer SQLite Lifetime

## Overview

このスキルは `C:\gitroot\new2022\cv10` の `CvServer` / `CvBaseSqlite` 周辺で、`ExDatabase` の DI lifetime、`SchedulerService` の DB 利用、SQLite の WAL/SHM cleanup を変更するときの調査・実装・検証手順です。

## 基本方針

- まず影響度調査を行い、実装計画を出してから変更する。
- architecture / dependency / impact-scope を扱うため、最初に `graphify-out/GRAPH_REPORT.md` を読む。built commit が HEAD とずれている場合は参考扱いにし、実ファイル確認を優先する。
- `Program.cs`、`SchedulerService.cs`、`Tests/TestServer` の直接構築経路を一緒に確認する。DI 登録だけを変えて完了扱いにしない。
- `.omo` は調査メモ置き場、`Doc/aicoding_log.md` は正式ログ。`.omo` と `.sisyphus` は commit 対象にしない。
- ユーザーが `commitまで` または AGENTS.md の通常 workflow を求めている場合は、検証・ログ・commit まで行う。

## 調査手順

```powershell
git status --short
git rev-parse HEAD
Get-Content graphify-out\GRAPH_REPORT.md -TotalCount 160
rg -n "ExDatabase|AddScoped|AddSingleton|SchedulerService|CloneDb|ClearPools|ApplicationStopping|wal_checkpoint|journal_mode|Tests/TestServer" CvServer CvBaseSqlite Tests
```

確認する観点:

- `ExDatabase` が singleton 前提で保持されていないか。
- `SchedulerService` など singleton worker が scoped DB を直接保持していないか。
- startup / shutdown の DB 作業が root provider の scoped service を直接握っていないか。
- `Tests/TestServer` が production DI 変更に追従しているか。
- SQLite sidecar cleanup が pooled connection の影響を受けない順序になっているか。

## ExDatabase を scoped 化する場合

- `Program.cs` の DI 登録を `AddScoped` へ変えるだけで止めない。
- singleton service から DB が必要な場合は `IServiceScopeFactory` で job / operation ごとに scope を作る。
- `SchedulerService` では `CloneDb` による長寿命 DB 複製を避け、実行時に scoped `ExDatabase` を解決する。
- startup / shutdown で DB を使う処理は明示 scope に閉じる。
- test 側で直接 `SchedulerService` や DB を組み立てる箇所は、constructor と service provider 作成を合わせて更新する。

## SQLite shutdown cleanup を触る場合

`Microsoft.Data.Sqlite` の pooled shared-cache WAL connection では `SqliteConnection.ClearAllPools()` だけで `-wal` / `-shm` が残ることがある。repo 実績の安全順序は次を基準にする。

1. pooled connection が生きている間に `PRAGMA optimize` を実行する。
2. `SqliteConnection.ClearAllPools()` を呼ぶ。
3. `Pooling=False` の専用 connection で `PRAGMA wal_checkpoint(TRUNCATE)` を実行する。
4. 同じ専用 connection で `PRAGMA journal_mode=DELETE` を実行する。
5. 再度 `SqliteConnection.ClearAllPools()` を呼ぶ。
6. retry 付きで `-wal` と `-shm` を削除する。

`ApplicationStopping` では checkpoint、`database.Close`、`CvBaseSqlite.ExDatabaseOption.ClearPools(connStr)` を別々の best-effort step として扱うと原因切り分けしやすい。

## 検証

通常の確認:

```powershell
git diff --check
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Tests/TestServer/TestServer.csproj"
dotnet run --project Tests/TestServer/TestServer.csproj --no-build
```

`CvBaseSqlite.dll` の copy で `MSB3026` / `MSB3027` / `MSB3021` が出た場合は、実行中の `CvServer` が lock している可能性を先に疑う。検証を続ける必要がある場合は alternate output を使う。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Tests/TestServer/TestServer.csproj -p:OutputPath=obj\CodexBuildOutput\"
dotnet Tests/TestServer/obj/CodexBuildOutput/TestServer.dll
```

## ログ

`Doc/aicoding_log.md` には、影響度調査、変更した lifetime / cleanup の理由、build と `Tests/TestServer` 実行結果を記録する。ログ追記後はファイル先頭または指定位置が崩れていないことを確認する。
