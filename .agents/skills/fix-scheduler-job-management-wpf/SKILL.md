---
name: fix-scheduler-job-management-wpf
description: Troubleshoots and fixes CvWpfclient Scheduler job management screens such as SysSchedulerJobMenteView and SysSchedulerCronEditView added around CodeShare.IScheduler and CvServer SchedulerService. Covers reading .omo scheduler design/implementation memos, direct IScheduler gRPC calls, XAML converter resources, menu wiring, and verification with CvWpfclient, CvServer, and Tests/TestServer. Use when the Scheduler management view fails to open, load tasks, edit Cron expressions, or update/delete jobs.
---

# Fix Scheduler Job Management WPF

このスキルは、`SysSchedulerJobMenteView` / `SysSchedulerCronEditView` と `CodeShare.IScheduler` / `CvServer.Services.SchedulerService` で構成される自動実行ジョブ管理画面の不具合調査・修正に使います。WPF共通規約は `wpf-project-guide`、画面単位の手順は `wpf-view-workflow`、XAML確認は `check-xaml` を併用します。

## いつ使うか

- `SysSchedulerJobMenteView` が開かない、初期表示で例外が出る、一覧が取得できないとき
- Cron式変更・削除・新規登録が `IScheduler` 呼び出しで失敗するとき
- `CvWpfclient` 側の `IScheduler` DI登録をやめて、ViewModel側から直接呼ぶ必要があるとき
- Scheduler ジョブ管理の作成時メモ（`.omo/scheduler_job_management_*.md`）を参考に修正するとき

## 事前確認

1. 可能なら作成時メモを読む。
   - `.omo/scheduler_job_management_design.md`
   - `.omo/scheduler_job_management_impl_memo.md`
2. 直近コミットや対象コミットを確認する。

```powershell
git show --stat --oneline <commit>
```

3. 対象ファイルを確認する。

```powershell
rg -n "IScheduler|SysSchedulerJobMenteView|SysSchedulerCronEditView|SchedulerTaskInfo|UpdateSchedulerTaskRequest" CodeShare CvServer CvWpfclient Tests
```

## 主な不具合候補

- `SysSchedulerJobMenteViewModel` が `AppGlobal.GetGrpcService<IScheduler>()` に依存している。
  - ユーザー指示が「DI処理をやめ、ViewModel側から直接呼ぶ」の場合は `create-wpf-direct-grpc-client` を使う。
- `SysSchedulerJobMenteView.xaml` で `BooleanToVisibilityConverter` を参照しているが、画面リソースにも App.xaml にも定義がない。
  - 同様の既存画面では画面ローカルに `<BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />` を定義している。
- `SysSchedulerCronEditView.xaml` の `StringIsNotEmptyToVisibilityConverter` は App.xaml の `helpers:StringIsNotEmptyToVisibilityConverter` 定義に依存している。
  - App.xaml と `CvWpfclient/Helpers/Converters/StringIsNotEmptyToVisibilityConverter.cs` の存在を確認する。
- `SysSchedulerCronEditViewModel` の Cron検証は `NCrontab` に依存する。
  - `CvWpfclient/CvWpfclient.csproj` と `Directory.Packages.props` に `NCrontab` があるか確認する。
- `BaseWindow` は `InitCommand` を自動実行する。
  - `ContentRendered` で `InitCommand` を重ねて追加しない。

## 修正手順

### 1. gRPC 呼び出し経路を整理する

`IScheduler` を DI から外す場合は、`App.xaml.cs` の `ConfigureClient<IScheduler>` を削除し、`SysSchedulerJobMenteViewModel` 側で直接クライアントを生成する。具体的な実装は `create-wpf-direct-grpc-client` に従う。

確認ポイント:

- `SysSchedulerJobMenteViewModel` から `AppGlobal.GetGrpcService<IScheduler>()` が消えている
- `CreateGrpcService<IScheduler>()` でクライアントを作っている
- `Common.ExtractSubPath(AppGlobal.Url)` と `GrpcSubPathHandler` でサブパス環境を維持している
- 各呼び出しに `AppGlobal.GetDefaultCallContext()` または `AppGlobal.GetDefaultCallContext(ct)` を渡している

### 2. XAML リソースを確認する

`SysSchedulerJobMenteView.xaml` に以下を追加する。

```xml
<helpers:BaseWindow.Resources>
    <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    ...
</helpers:BaseWindow.Resources>
```

`StringIsNotEmptyToVisibilityConverter` は App.xaml 側にある場合、重複追加せず既存定義を使う。

### 3. SchedulerService 側の契約を確認する

`CodeShare/IScheduler.cs` と `CvServer/Services/SchedulerService.cs` を確認する。

- `GetAllTasksAsync`
- `UpdateTaskAsync`
- `AddOneTaskAsync`
- `RemoveOneTaskAsync`
- `SchedulerTaskInfo`
- `UpdateSchedulerTaskRequest`

サーバー側の `NCrontab.Scheduler` API はバージョン差が出やすいので、疑わしい場合は実ビルドで確認する。設計メモだけで `RemoveTask(Guid)` / `UpdateTask(Guid, ...)` の有無を決めない。

### 4. メニュー導線を確認する

`CvWpfclient/Models/MenuData.cs` で「自動実行管理マスタ」が `Views._00System.SysSchedulerJobMenteView` を指しているか確認する。

## 確認手順

1. XAML変更がある場合は `check-xaml` の観点で、未定義リソースと Converter を確認する。
2. `git diff --check` を実行する。
3. WPF クライアントをビルドする。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

4. SchedulerService / 契約を触った場合、CvServer をビルドする。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"
```

5. SchedulerService 周辺の既存確認として TestServer を実行する。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestServer/TestServer.csproj"
```

`vscmd.bat` の最後に `The system cannot find the path specified.` が出ても、コマンドの終了コードと `0 warnings / 0 errors` またはテスト成功サマリを優先して判断する。

## ログとコミット

- `Doc/aicoding_log.md` に、原因、修正ファイル、DI登録削除、XAMLリソース修正、検証結果を追記する。
- `.omo/` の作業メモは scratch 領域なのでコミットしない。
- ステージ対象は修正ファイルと `Doc/aicoding_log.md` に限定する。

## 更新履歴

- **v0.1.0 (2026-06-03)**: Schedulerジョブ管理画面の `IScheduler` 直接呼び出し修正と XAMLリソース不備修正を元に初版作成
