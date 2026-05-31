# SQLite 3.38+ 検証結果

## 実行した確認
- `grep` による静的確認
  - `RIGHT JOIN` / `FULL OUTER JOIN` / `IS DISTINCT FROM` / `IS NOT DISTINCT FROM` は未検出
- `Tests/TestServer/TestServer.csproj` のビルド成功
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe` 実行成功
  - 6件成功 / 0失敗
  - `ExecuteSqliteWalCheckpoint_ReturnsCheckpointRow`
  - `CalcSummaryStockCumulative_UpdatesRunningTotalsInSqlite`
- `CvServer/CvServer.csproj /p:UseAppHost=false` のビルド成功

## 検証で確認できたこと
- `SummaryDb.CalcSummaryStockCumulative()` の CTE + window 関数更新が SQLite で成立する
- `SchedulerService.ExecuteSqliteWalCheckpoint()` が checkpoint 結果行 (`busy/log/checkpointed`) を返せる
- temp SQLite ファイルを使う既存 checkpoint テストは cleanup を安定化したうえで通過する

## 既知の制約
- `dotnet test Tests/TestServer/TestServer.csproj` は、このリポジトリの .NET 10 / Microsoft.Testing.Platform 設定により `VSTest target is no longer supported` で失敗した
- `dotnet build creativevision10.slnx` は既存の solution-level restore/build 問題で失敗したため、今回は `CvServer` と `TestServer` の直接検証で代替した
- `QueryListSqlParam` を通る動的SQLの全パターンまでは静的に保証できない
