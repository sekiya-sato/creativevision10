# SQLite 3.38+ 改善提案と修正方針

## 適用する修正
1. `CvServer/Services/SchedulerService.cs`
   - `PRAGMA optimize; PRAGMA wal_checkpoint(TRUNCATE); VACUUM;` の複文実行を、単文3回の実行へ分離する。
   - `RawExecCmd` が返す `Error` 行を明示的に失敗扱いし、checkpoint の `busy=0` のときだけ `VACUUM` を実行する。
   - 目的: `RawExecCmd` の結果集合依存を減らし、失敗の取りこぼしと不要な `VACUUM` 実行を防ぐ。

2. `CvDomainLogic/SummaryDb.cs`
   - `FROM {tableName} AS t, json_each(t.Jmeisai) AS j` を `CROSS JOIN` に変更する。
   - 目的: SQLite依存の暗黙結合記法を明示化し、監査時に結合意図を読み取りやすくする。

3. `Tests/TestServer/TestServer.cs`
   - `SummaryDb.CalcSummaryStockCumulative` の CTE/window 更新を in-memory SQLite で実行し、3.38系での構文成立と結果更新を検証する。

## 今回は提案のみに留める項目
- `CvBase/BaseDbDerived.cs` の `FROM MasterShohin M, json_each(M.Jcolsiz) J`
  - 同様に `CROSS JOIN` へ寄せられるが、派生テーブル生成経路の影響を増やすため今回は記録のみ。
- `ShopUriageInputViewModel` の文字列連結WHERE句
  - これは安全性/設計改善の話で、SQLite 3.38 構文監査の主眼から外れるため別件扱い。

## 非修正理由
- `ZaikoQueryViewModel` の `HAVING` は `GROUP BY` 付きで3.38互換。
- `LoginService` の `SELECT count(*)` / `where LoginId=@0` は単純で問題なし。
- `Program.cs` の shutdown checkpoint は単文であり、今回の問題点に該当しない。
- `SchedulerService` の WAL maintenance は改善したが、`VACUUM` 自体は重い処理であるため nightly maintenance 前提で運用する。
