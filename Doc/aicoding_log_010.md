## [2026-08-07] 16:21 在庫・売掛・買掛の当月/前月 再集計タスクを自動実行に追加
### Agent
- Claude Opus 5 : Anthropic
### Editor
- Claude Code
### 目的
- ユーザーからの要望：SchedulerService に自動実行タスクを1つ追加する。cron "10 1 * * *"、タスク名「在庫 売掛 買掛 の当月と前月 を再集計するタスク」。SummaryDb の SummaryAllAsyncStream / SummaryUriKakeAsyncStream / SummaryKaiKakeAsyncStream を前月分・当月分で実行し、在庫・売掛・買掛は互いに独立（在庫がエラーで止まっても売掛・買掛は実行）
### 実施内容
- CvServer/Services/SchedulerService.cs: 定数 `MonthlyResummaryCronExpression` / `MonthlyResummaryTaskName` と固定 GUID `MonthlyResummaryTaskId` を追加。既存の WAL チェックポイント・ワークファイル削除と同じ `RegisterTask` 経由の `RegisterMonthlyResummaryTask()` を追加
- CvServer/Services/SchedulerService.cs: `ExecuteMonthlyResummaryCoreAsync()` を追加。区分（在庫→売掛→買掛）× 月（前月→当月）の二重ループで再集計し、区分ごとの結果を Memo に集約
- CvServer/Services/SchedulerService.cs: `RunSummaryStreamAsync()` を追加。`StreamStepProgress` を消費し `IsError` 検出時点で列挙を打ち切る。補助レコード `ResummaryGroup` / `SummaryStreamResult` を追加
- CvServer/Services/SchedulerService.cs: `IsSystemTask()` に新タスク名を追加し、クライアントからの削除対象外（システムタスク扱い）にした
- CvServer/Program.cs: `ApplicationStarted` の起動時登録に `RegisterMonthlyResummaryTask()` を追加
- Tests/TestServer/TestServer.cs: cron が 01:10 に解決されること・固定 TaskId で登録されることを確認するテストを追加
### 技術決定 Why
- 既存 `ExecuteRunSummaryAsync`（gRPC 経由の RunSummary）とは別メソッドにした。RunSummary は Payload の1年月に対する在庫集計のみで意味が異なり、共通化すると既存 I/F の挙動が変わるため
- `StreamStepProgressRunner` はステップがエラーでも次ステップを継続する実装のため、「在庫の更新途中でエラーがあったら STOP」は呼び出し側で `await foreach` を抜けて列挙を打ち切る形で実現した（列挙をやめれば以降のステップは実行されない）
- エラー時のスキップ範囲は「同一区分の残月まで」とし、区分をまたがない。前月がエラーの状態で当月を再集計しても整合が取れないため
- タスク登録は gRPC コントラクト（`SchedulerTaskType`）を拡張せず、システムタスクとしてサーバ内に閉じた。CodeShare の enum に値を追加すると PosClient 含む全クライアントの再ビルドが必要になるため
- TaskId は既存2タスクと同様に固定 GUID とし、サーバ再起動時に同一 ID で再登録されるようにした
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` でビルド成功（0 警告 / 0 エラー）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestServer/TestServer.csproj"` でテスト 34 件すべて成功
- `git diff --check` で空白エラーなしを確認
- 実際の 01:10 起動時の再集計動作は未検証（スケジュール登録とcron解決のみテスト済み）

---
