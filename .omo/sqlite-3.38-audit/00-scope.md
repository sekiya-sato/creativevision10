# SQLite 3.38+ SQL監査スコープ

## 対象
- SQLite実行経路に到達する checked-in SQL
- 主要対象: `CvBaseSqlite`, `CvDomainLogic`, `CvServer`, `CvWpfclient` のSQL文字列/SQL生成コード
- テスト対象: `Tests/TestServer`, `Tests/TestLogin`

## 除外
- Doc/コメントのみのSQL例
- Oracle/MariaDB専用SQLの互換修正
- `QueryListSqlParam` 経由の外部入力SQLすべてを完全保証すること

## 基準
- SQLite 3.38 を下限とする
- 3.39以降専用構文 (`RIGHT/FULL JOIN`, `IS DISTINCT FROM`, `GROUP BY なし HAVING`) は持ち込まない
- 既存の `json_each`, `json_extract`, `ON CONFLICT`, `PRAGMA`, `changes()` はSQLite経路で妥当性を確認する
