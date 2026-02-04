

using Cvnet10Base;
using Newtonsoft.Json;
using NPoco;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace Cvnet10DomainLogic;

/// <summary>
/// データベース操作用Class mariaDB依存部分(主にDDL)
/// [Class for database operations (MariaDB dependent parts, mainly DDL)]
/// </summary>
public partial class ExDatabase : Database {

	readonly int _default_varchar2_length = 255;

	public virtual void Open() {
		Connection?.Open();
	}
	public virtual void Close() {
		Connection?.Close();
	}
	/// <summary>
	/// テーブル作成のSQL文の生成
	/// [Generating SQL statements for table creation]
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	string GetSqlCreateTable(Type classT) {
		var attr = (TableDmlAttribute?)Attribute.GetCustomAttribute(classT, typeof(TableDmlAttribute));
		var primaryKey = ((PrimaryKeyAttribute?)Attribute.GetCustomAttribute(classT, typeof(PrimaryKeyAttribute)))?.Value;

		var columns = GetSqlColumns(classT);

		var key = (primaryKey == null) ? "" : $",PRIMARY KEY({primaryKey})"; // キー定義 [Key definition]
		if (columns.Where(c => c.ToUpper().Contains("PRIMARY KEY")).Count() > 0) // 既に主キー定義があれば二重にならないようにする [Ensure that there is no duplication if a primary key is already defined]
			key = "";


		var sql = string.Format("Create Table IF NOT EXISTS {0} ({1}{2})", GetTableName(classT), string.Join(",", columns), key);
		return sql;
	}
	public string GetTableName(Type classT) {
		var tableNameAttr = (TableNameAttribute?)Attribute.GetCustomAttribute(classT, typeof(TableNameAttribute));
		var tableName = (tableNameAttr != null) ? tableNameAttr.Value : classT.Name; // テーブル名 [Table name]
		return tableName;
	}


	static string _default_varchar = " default null";// " default '.'"
	/// <summary>
	/// クラスの中に含まれるプロパティの配列を"Name database型"で返す
	/// [Return an array of properties contained within the class as "Name database type"]
	/// </summary>
	/// <param name="classT"></param>
	/// <returns></returns>
	public virtual List<string> GetSqlColumns(Type classT) {
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
						   // カラムサイズの定義があるかどうか [Whether there is a column size definition]
			var attrSize = (ColumnSizeDmlAttribute?)Attribute.GetCustomAttribute(item, typeof(ColumnSizeDmlAttribute));
			// 無視するカラムかどうか [Whether it is an ignored column]
			var attrIgnore = (IgnoreAttribute?)Attribute.GetCustomAttribute(item, typeof(IgnoreAttribute));
			if (attrIgnore != null)
				continue; // 無視する項目だった場合 [If it is an ignored item]
			var attrComputed = (ComputedColumnAttribute?)Attribute.GetCustomAttribute(item, typeof(ComputedColumnAttribute));
			if (attrComputed != null)
				continue;
			var attrResult = (ResultColumnAttribute?)Attribute.GetCustomAttribute(item, typeof(ResultColumnAttribute));
			if (attrResult != null)
				continue;
			if (attrSize != null) { // サイズおよび型指定があれば
				if (attrSize.ColType == ColumnType.String) {
					type = $"varchar({attrSize.Size}) " + _default_varchar;
				}
				else if (attrSize.ColType == ColumnType.Json) {
					type = "JSON";
				}
				else if (attrSize.ColType == ColumnType.Enum) {
					type = "int not null default 0";
				}
			}
			else {
				switch (item.PropertyType.Name) {
					case "Boolean":
						type = "TINYINT";
						type += " not null default 0";
						break;
					case "Byte":
						type = "varchar(1)";
						type += " not null default '0'";
						break;
					case "Char":
						type = "varchar(1)";
						type += " " + _default_varchar;
						break;
					case "Decimal":
						type = "decimal(14,8)";
						type += " not null default 0";
						break;
					case "Double":
						type = "double(14,8)";
						type += " not null default 0";
						break;
					case "Single":
						type = "float(14,8)";
						type += " not null default 0";
						break;
					case "Int16":
						type = "int";
						type += " not null default 0";
						break;
					case "Int32":
						type = "int";
						type += " not null default 0";
						break;
					case "Int64":
						type = "bigint";
						type += " not null default 0";
						break;
					case "UInt16":
						type = "int";
						type += " not null default 0";
						break;
					case "UInt32":
						type = "int";
						type += " not null default 0";
						break;
					case "UInt64":
						type = "bigint";
						type += " not null default 0";
						break;
					case "SByte":
						type = "varchar(1)";
						type += " not null default '0'";
						break;
					case "String":
						type = $"varchar({_default_varchar2_length}) " + _default_varchar;
						break;
					default:
						if (item.PropertyType.Name.StartsWith("List"))
							continue;
						type = $"varchar({_default_varchar2_length}) " + _default_varchar;
						break;
				}
			}
			if (name == "Id") { // 列Idであればキー項目 [If it is a column Id, it is a key item]
				type = "bigint auto_increment"; // mariaDB
				if (Connection.ToString().ToLower().Contains("sqlite")) {
					type = "INTEGER PRIMARY KEY AUTOINCREMENT"; // Sqlite で自動採番を有効にする
				}
			}
			/*
			var attrComment = (CommentAttribute?)Attribute.GetCustomAttribute(item, typeof(CommentAttribute));
			if (attrComment != null) {
				type += $" comment '{attrComment.Content}'";
			}
			*/
			ret.Add((name + " " + type).Trim());
		}
		return ret;
	}
	public string GetCurrentDatabase() {
		var dbname = Connection?.Database ?? "";
		return dbname;
	}
	/// <summary>
	/// 使用データベースの変更
	/// [Change in the database being used]
	/// </summary>
	/// <param name="dbname"></param>
	/// <returns></returns>
	public int UseDatabase(string dbname) {
		if (Connection != null) {
			var cmd = CreateCommand(Connection, CommandType.Text, $"use {dbname}", new object[0]);
			return cmd.ExecuteNonQuery();
		}
		else
			return -1;
	}

	/// <summary>
	/// テーブルが存在するかどうか
	/// [Check if the table exists]
	/// </summary>
	/// <param name="tablename"></param>
	/// <returns></returns>
	public bool IsExistTable(Type classT) {
		var tableName = GetTableName(classT); // テーブル名 [Table name]
		return IsExistTable(tableName);
	}
	/// <summary>
	/// テーブルが存在するかどうか
	/// [Check if the table exists]
	/// </summary>
	/// <param name="tablename"></param>
	/// <returns></returns>
	public bool IsExistTable(string tableName) {
		bool ret = false;
		if (string.IsNullOrEmpty(tableName))
			return false;
		if (tableName.Contains('.')) {
			var splitName = tableName.Split('.');
			tableName = splitName[splitName.Length - 1];
		}
		var schema = GetCurrentDatabase();
		var checkSql = $"SELECT TABLE_NAME,TABLE_ROWS,TABLE_COMMENT,auto_increment from INFORMATION_SCHEMA.TABLES \r\n WHERE table_schema='{schema}' and TABLE_NAME = '{tableName}'";
		if (DatabaseType == NPoco.DatabaseType.SQLite)
			checkSql = $"select * from sqlite_master where type = 'table' and name='{tableName}'";
		if (DatabaseType == NPoco.DatabaseType.Oracle || DatabaseType == NPoco.DatabaseType.OracleManaged)
			checkSql = $"select * from user_tables where table_name=upper('{tableName}')";
		try {
			var schemaTables = Fetch<dynamic>(checkSql);
			if (schemaTables.Count > 0)
				ret = true;
		}
		catch (Exception ex) {
			NLog.LogManager.GetCurrentClassLogger()?.Error(ex, string.Format("SQL実行エラー {0}", checkSql));
			return false;
		}
		return ret;
	}
	/// <summary>
	/// テーブルおよび関連キーの作成(デフォルトはテーブル存在時は何もしない) 成功したら戻り値true
	/// [Create table and related keys (default: do nothing if the table already exists). Returns true if successful]
	/// </summary>
	/// <typeparam name="T">対象のテーブル</typeparam> [Target table]
	/// <param name="isForce">テーブル存在時、削除して再作成するかどうか</param> [Whether to delete and recreate the table if it already exists]
	/// <returns></returns>
	public bool CreateTable(Type classT, bool isForce = false, bool isCreateIndex = true) {
		bool ret = false;
		if (Connection == null)
			return ret;
		try {
			var createSql = GetSqlCreateTable(classT);
			using (DbCommand command = Connection.CreateCommand()) {
				if (IsExistTable(classT) && isForce) {
					command.CommandText = string.Format("drop table IF EXISTS {0}", GetTableName(classT));
					var ret1 = command.ExecuteNonQuery();
				}
				command.CommandText = createSql;
				command.ExecuteNonQuery();
				ret = true;
				if (isCreateIndex) {
					CreateIndex(classT, isForce);
				}
			}
			CreateComment(classT); // コメントの作成 [Create comment]

		}
		catch (Exception ex) {
			NLog.LogManager.GetCurrentClassLogger()?.Error(ex, string.Format("SQL実行エラー テーブル作成({0})", GetTableName(classT)));
			return false;
		}
		return ret;
	}
	/// <summary>
	/// テーブルの削除(Drop)
	/// [Delete (Drop) table]
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public void DropTable(Type classT) {
		if (Connection != null) {
			using (DbCommand command = Connection.CreateCommand()) {
				command.CommandText = string.Format("drop table IF EXISTS {0}", GetTableName(classT));
				var ret1 = command.ExecuteNonQuery();
			}
		}
	}
	void CreateIndex(Type classT, bool isForce = false) {
		var tableName = GetTableName(classT);
		var attr = (TableDmlAttribute?)Attribute.GetCustomAttribute(classT, typeof(TableDmlAttribute));
		var uniqueKey = attr?.UnieqeKey;
		if (uniqueKey != null) {
			var uniqueKey2 = uniqueKey.Split('@');
			if (uniqueKey2 != null && uniqueKey2.Length > 0) {
				foreach (var item in uniqueKey2) {
					var key = item.Split('=');
					if (key.Length == 2) {
						CreateIndex(tableName, key[0], key[1], true, isForce);
					}
				}

			}
		}
		var nouniqueKey = attr?.NonUnieqeKey;
		if (nouniqueKey != null) {
			var nouniqueKey2 = nouniqueKey.Split('@');
			if (nouniqueKey2 != null && nouniqueKey2.Length > 0) {
				foreach (var item in nouniqueKey2) {
					var key = item.Split('=');
					if (key.Length == 2) {
						CreateIndex(tableName, key[0], key[1], false, isForce);
					}
				}

			}
		}
	}
	/// <summary>
	/// インデックスの作成
	/// [Create index]
	/// </summary>
	/// <param name="tbName">テーブル名</param> [Table name]
	/// <param name="indexName">インデックス名</param> [Index name]
	/// <param name="dbColumn">DBカラム名</param> [DB column name]
	/// <param name="isForce">強制再作成するかどうか</param> [Whether to force recreation]
	/// <returns></returns>
	int CreateIndex(string tbName, string indexName, string dbColumn, bool isUnique = false, bool isForce = false) {
		// インデックスがあるかどうかの確認 [Check if the index exists]
		var checkSql = string.Format("select COLUMN_NAME, INDEX_NAME from INFORMATION_SCHEMA.STATISTICS where TABLE_SCHEMA='{0}' and INDEX_NAME='{1}'"
			, GetCurrentDatabase(), indexName);
		if (DatabaseType == NPoco.DatabaseType.SQLite)
			checkSql = $"select * from sqlite_master where type='index' and tbl_name='{tbName}' and name='{indexName}'";
		if (DatabaseType == NPoco.DatabaseType.Oracle || DatabaseType == NPoco.DatabaseType.OracleManaged)
			checkSql = string.Format("select * from USER_INDEXES where table_name='{0}' and index_name='{1}'", tbName, indexName);
		var listIndex = Fetch<dynamic>(checkSql);
		if (listIndex.Count > 0 && isForce) {
			int ret = Execute(string.Format("DROP INDEX {0}", indexName));
			if (ret != 0) return -1;
		}
		if (listIndex.Count == 0 || isForce) {
			int ret = Execute(string.Format("CREATE {3} INDEX {1} ON {0}({2})"
			, tbName, indexName, dbColumn, isUnique ? "unique" : ""));
			if (ret == 0) return 1;
			else return -1;
		}
		return 0;
	}
	/// <summary>
	/// コメントの作成(上書)
	/// [Create comment (overwrite)]
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	public int CreateComment(Type classT) {
		if (DatabaseType == NPoco.DatabaseType.SQLite)
			return 0;
		if (DatabaseType == NPoco.DatabaseType.Oracle || DatabaseType == NPoco.DatabaseType.OracleManaged)
			return 0;
		var ret = 0;
		var tableNameAttr = (CommentAttribute?)Attribute.GetCustomAttribute(classT, typeof(CommentAttribute));
		var tableName = GetTableName(classT);
		if (tableNameAttr != null) {
			var sql = $"ALTER TABLE {tableName} COMMENT = '{tableNameAttr.Content}'";
			ret = Execute(sql);
		}
		return ret;
	}
	/// <summary>
	/// RawExecCommand用の列挙型
	/// [Enumeration for RawExecCommand]
	/// </summary>
	public enum RawExecCmdEnum {
		/// <summary>
		/// Show Tables
		/// </summary>
		ShowTables = 1,
		/// <summary>
		/// Show Columns from $table$
		/// </summary>
		//ShowColumns = 2, // no use for mariaDB
		/// <summary>
		/// SHOW INDEX FROM $table$
		/// </summary>
		//ShowIndex = 3, // no use for mariaDB
		/// <summary>
		/// Analyze $sql$
		/// </summary>
		Analyze = 10,
		/// <summary>
		/// Analyze FORMAT=JSON $sql$
		/// </summary>
		AnalyzeJ = 11,
		/// <summary>
		/// Explain $sql$
		/// </summary>
		Explain = 12,
		/// <summary>
		/// Explain FORMAT=JSON $sql$
		/// </summary>
		ExplainJ = 13,
		/// <summary>
		/// Show Version()
		/// </summary>
		Version = 21,
		/// <summary>
		/// SHOW VARIABLES
		/// </summary>
		Variables = 22,
		/// <summary>
		/// SHOW STATUS
		/// </summary>
		Status = 23,
		/// <summary>
		/// SHOW ERRORS
		/// </summary>
		Errors = 30,
		/// <summary>
		/// その他 [Others]
		/// </summary>
		Zetc = 99999
	}
	/// <summary>
	/// RawExecCommand用のSQL文の生成
	/// [Generate SQL statements for RawExecCommand]
	/// </summary>
	/// <param name="cmdFlg"></param>
	/// <param name="addSql"></param>
	/// <returns></returns>
	public string RawExecGetSql(RawExecCmdEnum cmdFlg, string? addSql = null) {
		switch (cmdFlg) {
			case RawExecCmdEnum.ShowTables:
				return "show tables";
			//case RawExecCmdEnum.ShowColumns:
			//	return $"show columns from {addSql}";
			//case RawExecCmdEnum.ShowIndex:
			//	return $"SHOW INDEX FROM {addSql}";
			case RawExecCmdEnum.Analyze:
				return $"ANALYZE {addSql}";
			case RawExecCmdEnum.AnalyzeJ:
				return $"ANALYZE FORMAT=JSON {addSql}";
			case RawExecCmdEnum.Explain:
				return $"EXPLAIN {addSql}";
			case RawExecCmdEnum.ExplainJ:
				return $"EXPLAIN FORMAT=JSON {addSql}";
			case RawExecCmdEnum.Version:
				return $"select version()";
			case RawExecCmdEnum.Variables:
				return $"SHOW VARIABLES";
			case RawExecCmdEnum.Errors:
				return $"SHOW ERRORS";
			case RawExecCmdEnum.Status:
				return $"SHOW STATUS";
			default:
				return "";
		}
	}
	public string RawSQL = "";
	public object[]? RawSQLParam = null;
	public string RawLastError = "";
	DbConnection? _conn = null;
	/*
	private static readonly JsonSerializerSettings jsonOptions = new() {
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		DefaultValueHandling = DefaultValueHandling.Ignore,
	};
	*/


	public ExDatabase(DbConnection connection) : base(connection) {
		_conn = connection;
		if(_conn.State != ConnectionState.Open)
			_conn.Open();
		/* SerializedColumn に対して効果がない!
			JsonConvert.DefaultSettings = () => jsonOptions;
		*/
	}


	/// <summary>
	/// 直接SQLを実行して結果を返す (エラーだったらエラーを返す)
	/// [Execute SQL directly and return the result (return an error if there is one)]
	/// </summary>
	/// <param name="sql"></param>
	/// <param name="para"></param>
	/// <returns></returns>
	public List<Dictionary<string, object>> RawExecCmd(string sql, object[]? para = null) {
		RawSQL = sql;
		RawSQLParam = para;
		RawLastError = "";
		var ret = new List<Dictionary<string, object>>();
		if (Connection != null) {
			var conn = Transaction?.Connection ?? Connection;
			try {
				using (DbCommand command = CreateCommand(conn, CommandType.Text, sql, para ?? [])) {
					using (var reader = command.ExecuteReader()) {
						while (reader.Read()) {
							var row = new Dictionary<string, object>();
							for (int i = 0; i < reader.FieldCount; i++) {
								row.Add(reader.GetName(i), reader.GetValue(i));
							}
							ret.Add(row);
						}
					}
				}
			}
			catch (Exception ex) {
				ret = new List<Dictionary<string, object>>();
				var row = new Dictionary<string, object>();
				row.Add("Error", ex.Message);
				ret.Add(row);
				RawLastError = ex.Message;
			}
		}
		return ret;
	}

}
