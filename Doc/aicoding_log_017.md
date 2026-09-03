## [2026-08-25] MariaDBプロバイダー基盤処理の補完

### Agent
- Sekiya Sato Codex

### 目的
- CvServer/CvWpfclientの個別SQL方言対応は保留し、`CvBase.ExDatabase` と既存SQLite実装を基準に、MariaDB/PostgreSQLプロバイダーの基盤処理を最小差分で整合させる。

### 実施内容
- `CvBase.ExDatabase` に、派生プロバイダーが接続を開くかどうか指定できるprotected constructorを追加した。
- `ExDatabaseMaria.GetDbConn` が `isOpen=false` でも接続を開いていた処理を修正した。
- MariaDBのOpen時にDBバージョンを取得し、Clone時も `ExDatabaseMaria` を維持するようにした。
- MariaDBのタイムアウト変更でSQLite用 `PRAGMA busy_timeout` を使用せず、NPocoの `CommandTimeout` を設定するようにした。
- MariaDBのテーブル一覧・件数取得を `information_schema.tables` ベースで実装し、共通実装の `sqlite_master` 依存を回避した。
- `CvBasePostgre` は必要なoverrideが既に実装済みだったため変更せず、`CvBaseSqlite` と変更元DB専用の `CvBaseOracle` も現状維持とした。

### 検証
- `CvBase`、`CvBaseSqlite`、`CvBaseMariadb`、`CvBasePostgre` を順次buildし、すべて警告0・エラー0。
- `dotnet build creativevision10.slnx --no-restore` 成功（警告0・エラー0）。
- `dotnet run --project Tests/TestServer/TestServer.csproj --no-restore` 成功（212件、失敗0、スキップ0）。
- MariaDB/PostgreSQLの実サーバー接続によるCRUD・メタデータ取得は未実施。

## [2026-08-25] macOS ZIP内日本語ファイル名正規化スキルの追加

### Agent
- Sekiya Sato Codex

### 目的
- macOSで圧縮されたZIP内の分解形式の日本語ファイル名を、Windows 11で扱いやすいUnicode NFC形式へ変換する手順をスキル化する。

### 実施内容
- `.agents/skills/normalize-macos-zip-filenames/SKILL.md` に適用条件、安全な出力モード、完了条件、対象外を記録した。
- `scripts/normalize-zip-filenames.ps1` を追加し、ZIP内エントリ名のNFC正規化、重複検出、一時ZIP生成、エントリ数・展開後SHA-256検証、明示時のみの元ZIP置換を実装した。
- `agents/openai.yaml` にスキル表示名と説明を設定した。

### 検証
- NFD形式の日本語エントリを含むテストZIPで、別ZIP出力を確認（RenamedEntryCount=1、SHA-256 OK）。
- 同テストZIPで `-ReplaceInput` を確認し、NFC名への変換と一時ファイル残存なしを確認した。
- `quick_validate.py` は同梱Pythonに `PyYAML` がないため実行できなかった。

## [2026-08-25] PostgreSQL用DBプロバイダーの追加

### Agent
- Sekiya Sato Codex

### 目的
- SQLite、MariaDB、Oracle用プロジェクトと同じレイヤーに `CvBasePostgre` を追加し、NpgsqlおよびPostgreSQL固有処理をプロバイダー内へ隔離する。

### 実施内容
- `CvBasePostgre` を追加し、接続生成、Open/Close、Clone、タイムアウト、DDL型変換、テーブル存在確認、コメント、テーブル件数、診断SQLをPostgreSQL向けに実装した。
- NPocoが引用する識別子を小文字へ統一し、既存の非引用SQLがPostgreSQLで小文字化される規則と物理名を一致させた。
- `CvBase.ExDatabase` にDB種別指定コンストラクターとPostgreSQL側で必要な仮想拡張点を追加した。既存プロバイダーの既定動作は維持した。
- `CvBasePostgre/ExDatabasePostgre.cs` に、残存するSQLite固有SQLと将来のSQL方言抽象化方針をコメントとして記録した。
- `Directory.Packages.props` に Npgsql 10.0.3、`creativevision10.slnx` と `readme.md` に新規プロジェクトを追加した。

### 検証
- `dotnet build CvBasePostgre/CvBasePostgre.csproj --no-restore` 成功（警告0・エラー0）。
- `dotnet build creativevision10.slnx --no-restore` 成功（警告0・エラー0）。
- `dotnet run --project Tests/TestServer/TestServer.csproj --no-build --no-restore` 成功（212件、失敗0、スキップ0）。
- PostgreSQL実サーバーがローカル環境にないため、実接続によるDDL/CRUD検証は未実施。

---
