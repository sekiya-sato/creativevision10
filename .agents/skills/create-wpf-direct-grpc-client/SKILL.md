---
name: create-wpf-direct-grpc-client
description: Creates or fixes CvWpfclient ViewModel-owned code-first gRPC clients when a service should not be registered through App.xaml.cs DI. Preserves AppGlobal.Url, subpath handling with Common.ExtractSubPath and GrpcSubPathHandler, AppGlobal.GetDefaultCallContext metadata, and WPF build verification. Use when replacing AppGlobal.GetGrpcService<T> or ConfigureClient<T> for a specific WPF ViewModel.
---

# Create WPF Direct gRPC Client

このスキルは、`CvWpfclient` の特定 ViewModel だけで gRPC サービスを直接呼ぶ必要があり、`App.xaml.cs` の `ConfigureClient<TService>` DI 登録に依存させたくない場合の実装手順です。WPF全体の共通規約は `wpf-project-guide`、画面単位の作業は `wpf-view-workflow` を前提にします。

## いつ使うか

- ユーザーが「DI処理をやめ、ViewModel側から直接呼ぶ」と指示したとき
- 対象サービスだけ `AppGlobal.GetGrpcService<T>()` 依存を外したいとき
- `App.xaml.cs` の gRPC DI 登録追加が過剰で、単一画面内に通信責務を局所化したいとき

## 使わない場合

- `ICoreService` や `ILoginService` など、多数の画面で共有されている既存サービス
- 設定変更後のホスト再構築やキャッシュ再利用が重要なサービス
- 単なる gRPC 呼び出し追加で、DI登録済みサービスを使えば十分な場合

## 事前確認

1. `CvWpfclient/App.xaml.cs` の `ConfigureClient<TService>` 登録状況を確認する。
2. `CvWpfclient/AppGlobal.cs` の `Url`、`GetDefaultCallContext()`、`GetGrpcService<T>()` を確認する。
3. `CvWpfclient/Helpers/Communication/GrpcSubPathHandler.cs` を確認し、サブパス付きURLの扱いを維持する。
4. 認証ヘッダーは `AppGlobal.GetDefaultCallContext()` 側で付与されるため、呼び出し時に必ず `CallContext` を渡す。

## 実装手順

### 1. 現在の登録と利用者を照合してから対象サービスだけ DI 登録を外す

`App.xaml.cs` の `ConfigureServices`、対象 ViewModel、全利用者を `rg` で照合する。`ConfigureClient<TService>` が存在する場合だけ、対象サービスの登録を削除する。`GetGrpcService<T>()` や他サービスの登録は残す。現行 Scheduler 契約は `ISchedulerService` / `GetTasksAsync` であり、過去の `IScheduler` / `GetAllTasksAsync` を使わない。

```csharp
// 削除例
ConfigureClient<ISchedulerService>(services, url, subPath);
```

### 2. ViewModel に直接クライアントを持たせる

必要な using を追加する。

```csharp
using CvAsset;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using System.Net.Http;
```

ViewModel にチャンネルとクライアントを追加する。

```csharp
private readonly GrpcChannel _schedulerChannel;
private readonly ISchedulerService _schedulerClient;

public XxxViewModel() {
    _schedulerChannel = CreateSchedulerChannel();
    _schedulerClient = _schedulerChannel.CreateGrpcService<ISchedulerService>();
}
```

### 3. 既存DIと同じ通信条件でチャンネルを作る

`AppGlobal.Url` を使い、`Common.ExtractSubPath` と `GrpcSubPathHandler` でサブパス付きURLを維持する。

```csharp
private static GrpcChannel CreateSchedulerChannel() {
    var socketsHandler = new SocketsHttpHandler {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        EnableMultipleHttp2Connections = true,
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
    };

    HttpMessageHandler handler = socketsHandler;
    var subPath = Common.ExtractSubPath(AppGlobal.Url);
    if (!string.IsNullOrEmpty(subPath)) {
        handler = new GrpcSubPathHandler(subPath) {
            InnerHandler = handler,
        };
    }

    var httpClient = new HttpClient(handler) {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    return GrpcChannel.ForAddress(AppGlobal.Url, new GrpcChannelOptions {
        HttpClient = httpClient,
    });
}
```

### 4. 呼び出し時は CallContext を渡す

```csharp
var response = await _schedulerClient.GetTasksAsync(AppGlobal.GetDefaultCallContext(ct));
```

キャンセル可能なコマンドでは `CancellationToken` 付きの `GetDefaultCallContext(ct)` を使う。

### 5. 後片付け

ViewModel の終了経路でチャンネルを破棄する。既存の `BaseViewModel.OnExit()` を使う画面では以下の形を基本にする。

```csharp
protected override void OnExit() {
    _schedulerChannel.Dispose();
    base.OnExit();
}
```

閉じる方法が複数ある画面でリソース寿命が重要な場合は、View の `Closed` から明示的に破棄するか、既存の画面パターンに合わせて追加対応する。

## 注意点

- `JwtAuthorizationHandler` は現状 no-op で、認証系ヘッダーは `CallContext` 側が正なので、直接生成時に必須ではない。
- `AppGlobal.Url` にサブパスが含まれる環境では、`GrpcSubPathHandler` を外すと通信先パスが変わる。
- 対象サービス以外の `ConfigureClient<T>()` を削除しない。
- 直接生成したクライアントを静的キャッシュ化しない。設定変更や画面寿命と競合しやすい。

## 確認手順

1. 対象 ViewModel から `AppGlobal.GetGrpcService<対象サービス>()` が消えていることを確認する。
2. `App.xaml.cs` から対象サービスの `ConfigureClient<対象サービス>()` だけが消えていることを確認する。
3. `git diff --check` を実行する。
4. WPF クライアントをビルドする。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

5. 可能なら対象画面を起動し、初回ロード・更新・キャンセル時の例外がないことを確認する。

`ISchedulerService`、サービス名、メソッド名は作業時点の `CodeShare/ISchedulerService.cs`、`CvServer/Services/SchedulerService.cs`、対象 ViewModel で再確認する。設計メモやこの例だけから契約を固定しない。

## ログ

作業完了後は `Doc/aicoding_log.md` に、DI登録削除、ViewModel直接生成、サブパス維持、ビルド結果を記録する。

## 更新履歴

- **v0.1.0 (2026-06-03)**: `SysSchedulerJobMenteViewModel` の `IScheduler` 直接呼び出し修正を元に初版作成
