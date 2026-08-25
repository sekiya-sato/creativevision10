using CvBase;
using Microsoft.Data.Sqlite;
using NPoco;
using System.Data;
using System.Data.Common;
using System.Reflection;


namespace CvBaseSqlite;

/// <summary>
/// SqliteDB用のデータベースクラス : ON CONFLICT ... DO UPDATE はSQLite 3.24.0 以降 / json_each() は SQLite 3.38 以降
/// [Database class for SqliteDB]
/// </summary>
public partial class ExDatabaseSqlite : ExDatabase {

	public override string Version { get; protected set; } = "";
	/// <summary>
	/// SQLite方言が正典なので変換しない。引数の参照をそのまま返す実装を明示的に返す。
	/// </summary>
	public override CvBase.Sql.ISqlDialect Dialect => CvBase.Sql.SqlDialects.Sqlite;
	public ExDatabaseSqlite(DbConnection conn) : base(conn) {
		if (conn != null) {
			if (conn.State == ConnectionState.Closed)
				conn.Open();
		}
	}
	/// <summary>
	/// データベース接続を取得する
	/// </summary>
	/// <param name="dbfile">データベースファイルのパス</param>
	/// <param name="isOpen">接続を開くかどうか</param>
	/// <returns>ExDatabaseSqliteのインスタンス</returns>
	public static ExDatabaseSqlite GetDbConn(string dbfile, bool isOpen = true) {
		// パフォーマンスと並行性を最大化する構成（WALモード併用）// Microsoft.Data.Sqliteでは、journal mode=WAL; は使えない
		var advancedConnectionString = new SqliteConnectionStringBuilder {
			DataSource = dbfile,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = true,                       // .NET側のコネクションプーリングを有効化
			Cache = SqliteCacheMode.Shared,       // プロセス内でのキャッシュ共有
			DefaultTimeout = 30                    // busy_timeout の初期値（秒単位）
		}.ToString();
		var conn = new SqliteConnection(advancedConnectionString);
		var _db = new ExDatabaseSqlite(conn);
		if (isOpen) {
			conn.Open();
			_db.EnableWalMode(conn);
			return _db;
		}
		return _db;
	}
	public override void Open() {
		if (Connection is SqliteConnection) {
			var connInner = (SqliteConnection)Connection;
			if (connInner.State == ConnectionState.Closed)
				connInner.Open();
			EnableWalMode(connInner);
		}
	}
	/// <summary>
	/// ロック待ち時間を変更する。SQLite固有の <c>PRAGMA busy_timeout</c> をここへ隔離している
	/// (基底クラスは他DBと共用のため何もしない)。
	/// </summary>
	public override void ChangeTimeout(int timeoutSec) {
		ArgumentOutOfRangeException.ThrowIfNegative(timeoutSec);
		RawExecCmd($"PRAGMA busy_timeout = {timeoutSec * 1000};");
	}
	public override void Close() {
		if (Connection is SqliteConnection) {
			var connInner = (SqliteConnection)Connection;
			if (connInner.State == ConnectionState.Open) {
				// DisableWalMode(connInner);
				this.KeepConnectionAlive = false;
				connInner.Close();
			}
		}
	}
	/// <summary>
	/// WALモードを有効にする : WALモードは、複数のプロセスが同時にデータベースにアクセスできるようにするためのSQLiteの機能で、パフォーマンスと並行性を向上させることができます。
	/// </summary>
	/// <param name="conn"></param>
	void EnableWalMode(SqliteConnection conn) {
		using (var cmd = conn.CreateCommand()) {
			cmd.CommandText = @"
    PRAGMA journal_mode = WAL;
    PRAGMA synchronous = NORMAL;
    PRAGMA page_size = 4096;
    PRAGMA cache_size = -64000;  -- 約64MBのキャッシュ
    PRAGMA temp_store = MEMORY;
    PRAGMA mmap_size = 268435456; -- 256MBまでのメモリマップドI/O
    PRAGMA foreign_keys = ON;
	PRAGMA journal_size_limit = 67108864; -- 64MBのジャーナルサイズ制限
";
			cmd.ExecuteNonQuery();
			cmd.CommandText = "SELECT sqlite_version();";
			Version = cmd.ExecuteScalar()?.ToString() ?? "";
		}
	}

	static readonly string _default_varchar = " NOT NULL DEFAULT ''"; // not null ではない varchar型のデフォルト定義

	/// <summary>
	/// クラスの中に含まれるプロパティの配列を"Name database型"で返す
	/// [Returns an array of properties contained in the class as "Name database type"]
	/// </summary>
	/// <param name="classT"></param>
	/// <returns></returns>
	public override List<string> GetSqlColumns(Type classT) {
		var ret = new List<string>();
		var infoArray2 = classT.GetProperties();
		if (infoArray2 == null || infoArray2.Length == 0)
			return ret;
		var infoArray = new List<PropertyInfo>();
		var columnId = infoArray2.FirstOrDefault(c => c.Name == "Id");
		if (columnId != null)
			infoArray.Add(columnId);
		var columnCreated = infoArray2.FirstOrDefault(c => c.Name == "Vdc");
		if (columnCreated != null)
			infoArray.Add(columnCreated);
		var columnUpdated = infoArray2.FirstOrDefault(c => c.Name == "Vdu");
		if (columnUpdated != null)
			infoArray.Add(columnUpdated);
		for (int i = 0; i < infoArray2.Length; i++) {
			if (infoArray2[i].Name != "Id" && infoArray2[i].Name != "Vdc" && infoArray2[i].Name != "Vdu")
				infoArray.Add(infoArray2[i]);
		}
		foreach (var item in infoArray) {
			var name = item.Name;
			var type = ""; // NUMBER,TEXT,REAL
						   // 無視するカラムかどうか [Whether to ignore the column]
			var attrIgnore = (IgnoreAttribute?)Attribute.GetCustomAttribute(item, typeof(IgnoreAttribute));
			if (attrIgnore != null)
				continue; // 無視する項目だった場合 [If it was an item to ignore]
			var attrComputed = (ComputedColumnAttribute?)Attribute.GetCustomAttribute(item, typeof(ComputedColumnAttribute));
			if (attrComputed != null)
				continue;
			var attrResult = (ResultColumnAttribute?)Attribute.GetCustomAttribute(item, typeof(ResultColumnAttribute));
			if (attrResult != null)
				continue;
			switch (item.PropertyType.Name) {
				case "Boolean":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "Byte":
					type = "TEXT";
					type += _default_varchar;
					break;
				case "Char":
					type = "TEXT";
					type += _default_varchar;
					break;
				case "Decimal":
					type = "REAL";
					type += " not null default 0";
					break;
				case "Double":
					type = "REAL";
					type += " not null default 0";
					break;
				case "Single":
					type = "REAL";
					type += " not null default 0";
					break;
				case "Int16":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "Int32":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "Int64":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "UInt16":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "UInt32":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "UInt64":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "SByte":
					type = "NUMBER";
					type += " not null default 0";
					break;
				case "String":
					type = "TEXT";
					type += _default_varchar;
					break;
				default:
					if (item.PropertyType.Name.StartsWith("List")) {
						type = "TEXT";
						type += _default_varchar;
					}
					else {
						type = "TEXT";
						type += _default_varchar;
					}
					break;
			}
			if (name == "Id") // 列Idであれば主キー項目 [If it is a column ID, it is a primary key item]
				/*
					*	自動採番は、 INTEGER PRIMARY KEY AUTOINCREMENT で定義される bigintやNumberはNG
					*	また AUTOINCREMENTであっても値をセットすればそれが優先される
					*	C#のInt32= 2,147,483,647 : 20億レコードまで SqliteのINTEGER型は 2,147,483,647を超えても登録可
					*	
					*	[Automatic numbering is defined with INTEGER PRIMARY KEY AUTOINCREMENT; bigint or Number are not allowed
					*	 Also, even if it is AUTOINCREMENT, setting a value will take precedence
					*	 C#’s Int32 = 2,147,483,647: up to 2 billion records; SQLite’s INTEGER type can be registered even beyond 2,147,483,647]
					*/
				type = "INTEGER not null default 0 PRIMARY KEY AUTOINCREMENT";
			ret.Add((name + " " + type).Trim());
		}
		return ret;
	}
}


