# SQLite 3.38+ SQL棚卸し

## 静的確認結果サマリ
- `RIGHT JOIN` / `FULL OUTER JOIN` / `IS DISTINCT FROM` / `IS NOT DISTINCT FROM`: 未検出
- `HAVING`: `CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs` の1件のみ。`GROUP BY T.Id_Shohin` 付きで3.38互換
- SQLite固有構文の集中箇所: `SummaryDb`, `ConvertDbTran`, `ConvertDb`, `BaseDbDerived`, `ExDatabaseSqlite`, `SchedulerService`, `Program`

## 主要監査対象一覧
| ファイル | 区分 | 主なSQL/機能 | 判定 | 対応 |
|---|---|---|---|---|
| `CvDomainLogic/SummaryDb.cs` | runtime-sqlite | `json_each`, `json_extract`, `ON CONFLICT`, `changes()`, CTE/window | 要改善 | 暗黙結合を `CROSS JOIN` へ明示化、CTE/windowをテストで確認 |
| `CvDomainLogic/ConvertDbTran.cs` | runtime-sqlite | `json_each`, `json_group_array`, `json_set`, `changes()` | 現状維持 | 3.38互換。重い更新SQLのため今回は記録のみ |
| `CvDomainLogic/ConvertDb.cs` | runtime-sqlite | `json_extract` によるJSON列判定 | 現状維持 | 3.38互換。構文問題なし |
| `CvBase/BaseDbDerived.cs` | runtime-sqlite | `json_each`, `json_extract`, `ROW_NUMBER()` | 改善提案 | 暗黙結合を `CROSS JOIN` に寄せる候補。今回は提案止まり |
| `CvBaseSqlite/ExDatabaseSqlite.cs` | runtime-sqlite | `PRAGMA journal_mode`, `PRAGMA synchronous`, `sqlite_version()` | 現状維持 | 構文問題なし |
| `CvServer/Services/SchedulerService.cs` | runtime-sqlite | `PRAGMA optimize`, `wal_checkpoint`, `VACUUM` | 修正済み | 単文分離し、`RawExecCmd` の Error 行を失敗扱い、`busy=0` のときのみ `VACUUM` 実行 |
| `CvServer/Program.cs` | runtime-sqlite | `PRAGMA wal_checkpoint(TRUNCATE)` | 現状維持 | 単文で妥当 |
| `CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs` | dynamic-sql | `HAVING IFNULL(SUM(T.Su), 0) <> 0` | 現状維持 | `GROUP BY` 付きで妥当 |
| `CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs` | dynamic-sql | パラメータ化済み `EXISTS` / `LIKE` | 現状維持 | 3.38互換 |
| `CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs` | dynamic-sql | 文字列連結 `json_extract` 条件 | 改善提案 | 構文問題なし。安全性改善は別タスク |
| `CvServer/Services/HandlerClass.cs` | pass-through | `QueryListSqlParam` をそのまま実行 | 要注意 | 静的監査の未保証範囲として管理 |

## 監査メモ
- `SummaryDb` の `FROM table, json_each(...)` は SQLite で合法だが、明示的な `CROSS JOIN` の方が監査しやすい。
- `SchedulerService` のメンテナンスSQLは複文でも動く可能性があるが、`RawExecCmd` の戻り値が最終結果集合依存のため単文分離の方が安定する。
- `wal_checkpoint(TRUNCATE)` が busy を返した場合は `VACUUM` を続行しないようにして、夜間メンテナンスのロック影響を抑えた。
