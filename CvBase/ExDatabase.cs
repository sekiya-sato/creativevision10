using Microsoft.Extensions.Logging;
using NPoco;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace CvBase;

/// <summary>
/// データベース操作用Class。DBに依存しない共通処理(方言変換の入口、CREATE文の組み立て、生SQL実行)を持つ。
/// 接続の生成、列定義(型名)、メタデータ照会などDB固有の処理は CvBaseSqlite / CvBaseMariadb / CvBasePostgre / CvBaseOracle の派生クラスに置く。
/// [Class for database operations (provider independent parts)]
/// </summary>
public partial class ExDatabase : Database {

	/// <summary>
	/// ロガーインスタンス [Logger instance]
	/// </summary>
	private static readonly ILogger<ExDatabase> _logger = new NLogExtender<ExDatabase>();

	public virtual string Version { get; protected set; } = "";
	/// <summary>
	/// クライアント由来SQLの方言変換。既定は恒等変換で、プロバイダーが上書きする。
	/// <para>
	/// 変換を差す経路は2つだけである。
	/// 1つは CvServer の HandlerClass のクライアントSQL受け口
	/// （HandleQueryOne / HandleQueryList / HandleQueryListSql）。
	/// もう1つはこのクラスの <c>ExecuteDialect</c> / <c>FetchDialect</c> / <c>TranslateDialect</c> で、
	/// CvDomainLogic のJSON配列再構築とUPSERTがここを通る。
	/// これら以外のSQLは変換器を通さず、必要な箇所へDB別分岐を置く。
	/// SQLiteを正典とし、接続先の方言に応じた構文変換とQueryKeyによる個別上書きを使う。
	/// </para>
	/// </summary>
	public virtual Sql.ISqlDialect Dialect => Sql.PassThroughSqlDialect.Instance;

	/// <summary>
	/// SQLite方言で書いたSQLを接続先の方言へ変換してから実行する。
	/// <para>
	/// サーバ側のSQL（<c>CvDomainLogic</c> 等）でJSON配列の作り直しやUPSERTのように
	/// DB間で構文が異なるものに使う。SQLite接続では <see cref="Sql.ISqlDialect.TranslatesSql"/> が
	/// false なので引数がそのまま渡り、<see cref="Execute(string, object[])"/> と完全に同じ動作になる。
	/// </para>
	/// </summary>
	public int ExecuteDialect(string sql, params object[] args) {
		if (Sql.SqlCapture.Enabled) Sql.SqlCapture.Record("server:ExecuteDialect", sql, args);
		return Execute(TranslateDialect(sql), args);
	}

	/// <summary>
	/// SQLite方言で書いたSQLを接続先の方言へ変換してから取得する。
	/// SQLite接続では <see cref="Fetch{T}(string, object[])"/> と完全に同じ動作になる。
	/// </summary>
	public List<T> FetchDialect<T>(string sql, params object[] args) {
		if (Sql.SqlCapture.Enabled) Sql.SqlCapture.Record("server:FetchDialect", sql, args, typeof(T));
		return Fetch<T>(TranslateDialect(sql), args);
	}

	/// <summary>
	/// SQLite方言で書いたSQLを接続先の方言へ変換し、先頭1件を返す（無ければ既定値）。
	/// SQLite接続では <c>Fetch&lt;T&gt;(sql, args).FirstOrDefault()</c> と同じ動作になる。
	/// </summary>
	public T? FirstOrDefaultDialect<T>(string sql, params object[] args) =>
		FetchDialect<T>(sql, args).FirstOrDefault();

	/// <summary>
	/// SQLite方言のSQLを接続先の方言へ変換する。SQLiteでは引数の参照をそのまま返す。
	/// </summary>
	public string TranslateDialect(string sql) =>
		Dialect.TranslatesSql ? Dialect.Translate(sql) : sql;

	/// <summary>
	/// NPoco の自動SELECT補完を WITH 句のSQLで止める。
	/// <para>
	/// NPoco（6.4.0 時点）は SELECT で始まらないSQLへ「SELECT 列 FROM 表」を補うため、
	/// <c>WITH ... SELECT</c> は構文エラーになる。先頭の空白・コメントを除いた最初の語が WITH のときだけ
	/// 先頭へ「;」を付ける（NPoco は「;」で始まるSQLを補完せず、「;」を取り除いて実行する）。
	/// それ以外のSQL（WHERE 句だけの断片を含む）は参照をそのまま返す。
	/// </para>
	/// </summary>
	public static string SuppressAutoSelectForWith(string sql) {
		var i = 0;
		while (i < sql.Length) {
			if (char.IsWhiteSpace(sql[i])) {
				i++;
			}
			else if (string.CompareOrdinal(sql, i, "--", 0, 2) == 0) {
				var end = sql.IndexOf('\n', i);
				i = end < 0 ? sql.Length : end + 1;
			}
			else if (string.CompareOrdinal(sql, i, "/*", 0, 2) == 0) {
				var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = end < 0 ? sql.Length : end + 2;
			}
			else {
				break;
			}
		}
		var isWith = i + 4 <= sql.Length
			&& string.Compare(sql, i, "WITH", 0, 4, StringComparison.OrdinalIgnoreCase) == 0
			&& (i + 4 == sql.Length || !(char.IsLetterOrDigit(sql[i + 4]) || sql[i + 4] == '_'));
		return isWith ? ";" + sql : sql;
	}
	public virtual void Open() {
		Connection?.Open();
	}
	public virtual void Close() {
		Connection?.Close();
	}
	/// <summary>
	/// データベースのタイムアウトを変更する (秒単位)
	/// <para>
	/// 基底では何もしない。SQLiteの <c>PRAGMA busy_timeout</c> は他DBに存在せず、
	/// 意味も「ロック待ち時間」対「コマンド実行時間」で異なるため、各プロバイダーで実装する。
	/// SQLite は <c>ExDatabaseSqlite</c>、MariaDB / PostgreSQL は <c>CommandTimeout</c> を使う。
	/// </para>
	/// </summary>
	/// <param name="timeoutSec"></param>
	public virtual void ChangeTimeout(int timeoutSec) {
		ArgumentOutOfRangeException.ThrowIfNegative(timeoutSec);
	}


	/// <summary>
	/// テーブル作成のSQL文の生成
	/// [Generating SQL statements for table creation]
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	public string GetSqlCreateTable(Type classT) {
		var keys = classT.GetCustomAttributes<KeyDmlAttribute>();
		var primaryKey = classT.GetCustomAttribute<PrimaryKeyAttribute>();

		var columns = GetSqlColumns(classT);

		// 属性オブジェクトではなく列名(Value)を使う（属性をそのまま埋め込むと "PRIMARY KEY(NPoco.PrimaryKeyAttribute)" になり MariaDB で表を作れない）
		var key = (primaryKey == null) ? "" : $",PRIMARY KEY({primaryKey.Value})"; // キー定義 [Key definition]
		if (columns.Where(c => c.ToUpper().Contains("PRIMARY KEY")).Count() > 0) // 既に主キー定義があれば二重にならないようにする [Ensure that there is no duplication if a primary key is already defined]
			key = "";


		var sql = string.Format("Create Table IF NOT EXISTS {0} ({1}{2}){3}", GetTableName(classT), string.Join(",", columns), key, CreateTableSuffix);
		return sql;
	}

	/// <summary>
	/// CREATE TABLE の閉じ括弧の後ろへ付ける定義。既定は空。
	/// <para>
	/// MariaDB で文字セットと照合順序を固定するために使う。照合順序は方言変換では直せない差
	/// (MariaDBの既定は大文字小文字とかなを同一視する) なので、テーブル作成時に決め切る。
	/// </para>
	/// </summary>
	protected virtual string CreateTableSuffix => string.Empty;

	/// <summary>
	/// スキーマ側の前提条件を検証する。不足内容の文言を返す。空なら合格。
	/// <para>
	/// 照合順序のようにDDLやDB作成時にしか決められない条件を起動時に確かめる。
	/// SQLite は既定でBINARY照合なので検証項目が無い。
	/// </para>
	/// </summary>
	public virtual IReadOnlyList<string> ValidateSchema() => [];
	public string GetTableName(Type classT) {
		var tableName = (classT.GetCustomAttribute<TableNameAttribute>()?.Value) ?? classT.Name; // テーブル名 [Table name]
		return tableName;
	}


	/// <summary>
	/// クラスの中に含まれるプロパティの配列を"Name database型"で返す。
	/// 型名・既定値・自動採番の書き方がDBごとに違うため、接続先プロバイダーで実装する。
	/// </summary>
	/// <param name="classT"></param>
	/// <returns></returns>
	public virtual List<string> GetSqlColumns(Type classT) =>
		throw new NotSupportedException("列定義の生成は接続先プロバイダーで実装してください。");
	/// <summary>
	/// テーブルの列になるプロパティを列順(Id, Vdc, Vdu, 以降は定義順)で返す。
	/// <para>
	/// 対象は public のインスタンスプロパティ。Static を含めないのは DerivedJodai.TodaySql のような
	/// 式プロパティを列にしないため。Ignore / ComputedColumn / ResultColumn 属性の付いたものは除く。
	/// </para>
	/// </summary>
	protected static List<PropertyInfo> GetColumnProperties(Type classT) {
		// BindingFlags.Public だけでは何も返らない(Instance/Static の指定が必須)。
		var properties = classT.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		var ordered = new List<PropertyInfo>();
		foreach (var head in (string[])["Id", "Vdc", "Vdu"]) {
			var property = properties.FirstOrDefault(c => c.Name == head);
			if (property != null)
				ordered.Add(property);
		}
		foreach (var property in properties) {
			if (property.Name != "Id" && property.Name != "Vdc" && property.Name != "Vdu")
				ordered.Add(property);
		}
		ordered.RemoveAll(static property =>
			property.GetCustomAttribute<IgnoreAttribute>() != null
			|| property.GetCustomAttribute<ComputedColumnAttribute>() != null
			|| property.GetCustomAttribute<ResultColumnAttribute>() != null);
		return ordered;
	}
	public string GetCurrentDatabase() {
		var dbname = Connection?.Database ?? "";
		return dbname;
	}
	/// <summary>
	/// SqlDepends: 使用データベースの変更
	/// </summary>
	/// <param name="dbname"></param>
	/// <returns></returns>
	public virtual int UseDatabase(string dbname) {
		if (Connection != null) {
			var cmd = CreateCommand(Connection, CommandType.Text, $"use {dbname}", new object[0]);
			return cmd.ExecuteNonQuery();
		}
		else
			return -1;
	}

	/// <summary>
	/// 接続先の実表名を取得する。Sys表を含み、DB内部表・ビューを除く。照会失敗は呼出元へ通知する。
	/// </summary>
	public virtual IReadOnlyList<string> GetUserTableNames() =>
		throw new NotSupportedException("実表一覧の取得は接続先プロバイダーで実装してください。");

	/// <summary>
	/// テーブルが存在するかどうか
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns></returns>
	public bool IsExistTable(Type classT) {
		var tableName = GetTableName(classT); // テーブル名 [Table name]
		return IsExistTable(tableName);
	}
	/// <summary>
	/// テーブルが存在するかどうか。メタデータの照会方法がDBごとに違うため、接続先プロバイダーで実装する。
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns></returns>
	public virtual bool IsExistTable(string tableName) =>
		throw new NotSupportedException("テーブルの存在確認は接続先プロバイダーで実装してください。");
	/// <summary>
	/// スキーマ修飾(a.b)を外した表名を返す。
	/// </summary>
	protected static string GetUnqualifiedTableName(string tableName) {
		if (tableName.Contains('.')) {
			var splitName = tableName.Split('.');
			tableName = splitName[splitName.Length - 1];
		}
		return tableName;
	}
	/// <summary>
	/// 存在確認SQLを実行し、1行以上返れば true。SQLエラーはログへ出して false を返す。
	/// </summary>
	protected bool ExistsRow(string checkSql) {
		try {
			return Fetch<dynamic>(checkSql).Count > 0;
		}
		catch (Exception ex) {
			_logger.LogError(ex, string.Format("SQL実行エラー {0}", checkSql));
			return false;
		}
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
		if (classT.GetCustomAttribute<NoCreateAttribute>() != null)
			return true;
		if (classT.GetCustomAttribute<SubTableDefineAttribute>() != null)
			return true;
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
			_logger.LogError(ex, string.Format("SQL実行エラー テーブル作成({0})", GetTableName(classT)));
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
	/// <summary>
	/// Idを明示して行を投入した後、自動採番の次の値を表内の最大Id+1へ合わせる。
	/// <para>
	/// 基底では何もしない。SQLite(AUTOINCREMENT の sqlite_sequence)と MariaDB(AUTO_INCREMENT)は
	/// 明示Idの挿入で自動的に追随する。PostgreSQL の IDENTITY は追随しないためプロバイダーで実装する。
	/// </para>
	/// </summary>
	public virtual void ResetIdentity(Type classT) {
	}
	void CreateIndex(Type classT, bool isForce = false) {
		var tableName = GetTableName(classT);
		// 同名の索引は派生クラスの定義だけを使う。SummaryStock は親 SummaryRealStock と同じ名前(unq1)を
		// 別の列構成で定義しており、親の分まで実行すると強制再作成のとき子の索引を消して親の列構成で作り直してしまう。
		var attrs = new List<KeyDmlAttribute>();
		for (var type = classT; type != null; type = type.BaseType) {
			foreach (var attrKey in type.GetCustomAttributes<KeyDmlAttribute>(false)) {
				if (!attrs.Any(c => c.KeyName == attrKey.KeyName))
					attrs.Add(attrKey);
			}
		}
		foreach (var attrKey in attrs) {
			var indexName = attrKey.KeyName;
			var colNames = string.Join(",", attrKey.ColNames);
			CreateIndex(tableName, indexName, colNames, attrKey.IsUnique, isForce);
		}
	}
	/// <summary>
	/// インデックスの作成
	/// [Create index]
	/// </summary>
	/// <param name="tbName">テーブル名</param> [Table name]
	/// <param name="indexSubName">インデックス名</param> [Index name]
	/// <param name="dbColumn">DBカラム名</param> [DB column name]
	/// <param name="isForce">強制再作成するかどうか</param> [Whether to force recreation]
	/// <returns></returns>
	int CreateIndex(string tbName, string indexSubName, string dbColumn, bool isUnique = false, bool isForce = false) {
		var indexName = $"{tbName}_{indexSubName}"; // 必ずテーブル名をつけUniqueになるように
		/*
		var checkSql = string.Format("select COLUMN_NAME, INDEX_NAME from INFORMATION_SCHEMA.STATISTICS where TABLE_SCHEMA='{0}' and INDEX_NAME='{1}'"
			, GetCurrentDatabase(), indexName);
		if (DatabaseType == NPoco.DatabaseType.SQLite)
			checkSql = $"select * from sqlite_master where type='index' and tbl_name='{tbName}' and name='{indexName}'";
		if (DatabaseType == NPoco.DatabaseType.Oracle || DatabaseType == NPoco.DatabaseType.OracleManaged)
			checkSql = string.Format("select * from USER_INDEXES where table_name='{0}' and index_name='{1}'", tbName, indexName);
		var listIndex = Fetch<dynamic>(checkSql);
		if (listIndex.Count > 0 && isForce) {
			int ret = Execute(string.Format("DROP INDEX IF EXISTS {0}", indexName));
			if (ret != 0) return -1;
		}
		 */
		int ret = 0;
		if (isForce) {
			ret = Execute(GetSqlDropIndex(tbName, indexName));
		}
		ret = Execute(string.Format("CREATE {3} INDEX IF NOT EXISTS {1} ON {0}({2})"
			, tbName, indexName, dbColumn, isUnique ? "unique" : ""));
		if (ret == 0) return 1;
		else return -1;
	}
	/// <summary>
	/// 索引を削除するSQL。MariaDB は <c>ON 表名</c> が必須のためプロバイダーで上書きする。
	/// </summary>
	protected virtual string GetSqlDropIndex(string tableName, string indexName) =>
		string.Format("DROP INDEX IF EXISTS {0}", indexName);
	/*
	public bool CreateView<T>(bool isForce = false) where T : IViewClass, new() {
		var ret = 0;
		var viewsql = new T().CreateSql;
		var viewname = typeof(T).Name;
		var createSql = $"CREATE VIEW IF NOT EXISTS {viewname} AS {viewsql}";
		using (DbCommand command = Connection.CreateCommand()) {
			if (isForce) {
				command.CommandText = string.Format($"drop view IF EXISTS {viewname}");
				ret = command.ExecuteNonQuery();
			}
			command.CommandText = createSql;
			ret = command.ExecuteNonQuery();
		}
		return (ret == 0);
	}*/
	[Obsolete("Use CreateTable instead.")]
	public bool CreateDerivedTable(Type t, bool isForce = false) {
		var ret = true;
		ret = CreateTable(t, isForce);

		if (isForce) {
			DropTable(t);
			ret = CreateTable(t, isForce);
			var createSql = GetSqlCreateTable(t);
			BeginTransaction();
			var cnt = Execute(createSql);
			CompleteTransaction();
		}
		return ret;
	}


	/// <summary>
	/// コメントの作成(上書)。基底では何もしない(SQLite はテーブルコメントを持たない)。
	/// 構文がDBごとに違うため、対応するプロバイダーで実装する。
	/// [Create comment (overwrite)]
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	public virtual int CreateComment(Type classT) => 0;
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
	/// <param name="cmdFlag"></param>
	/// <param name="addSql"></param>
	/// <returns></returns>
	public virtual string RawExecGetSql(RawExecCmdEnum cmdFlag, string? addSql = null) {
		switch (cmdFlag) {
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
	public string RawSql { get; private set; } = "";
	public object[]? RawSqlParam { get; private set; } = null;
	public string RawLastError { get; private set; } = "";
	DbConnection? _conn = null;
	/*
	private static readonly JsonSerializerSettings jsonOptions = new() {
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		DefaultValueHandling = System.ComponentModel.DefaultValueHandling.Ignore,
	};
	*/


	public ExDatabase(DbConnection connection) : base(connection) {
		InitializeConnection(connection, true);
	}

	/// <summary>
	/// 接続を開くかどうかを指定して初期化します。
	/// </summary>
	/// <param name="connection">使用するDB接続。</param>
	/// <param name="isOpen">接続を開く場合はtrue。</param>
	protected ExDatabase(DbConnection connection, bool isOpen) : base(connection) {
		InitializeConnection(connection, isOpen);
	}

	/// <summary>
	/// DBプロバイダー固有の <see cref="DatabaseType"/> を指定して初期化します。
	/// </summary>
	/// <param name="connection">使用するDB接続。</param>
	/// <param name="databaseType">NPocoのDBプロバイダー定義。</param>
	/// <param name="isOpen">接続を開く場合はtrue。</param>
	protected ExDatabase(DbConnection connection, DatabaseType databaseType, bool isOpen = true) : base(connection, databaseType) {
		InitializeConnection(connection, isOpen);
	}

	void InitializeConnection(DbConnection connection, bool isOpen) {
		_conn = connection;
		if (isOpen && _conn.State != ConnectionState.Open)
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
		RawSql = sql;
		RawSqlParam = para;
		RawLastError = "";
		var ret = new List<Dictionary<string, object>>();
		if (Connection != null) {
			var conn = Transaction?.Connection ?? Connection;
			try {
				using (DbCommand command = CreateCommand(conn, CommandType.Text, sql, para ?? [])) {
					// NPoco 経由の実行と同じくプロバイダーの前処理を通す（PostgreSQL の文字列パラメータの型推論など）
					OnExecutingCommand(command);
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

	/// <summary>
	/// テーブル一覧と件数の取得。メタデータの照会方法がDBごとに違うため、接続先プロバイダーで実装する。
	/// </summary>
	/// <returns></returns>
	public virtual List<Tuple<string, string, long>> GetTableCounts(string tableName = "") =>
		throw new NotSupportedException("テーブル一覧と件数の取得は接続先プロバイダーで実装してください。");
	/// <summary>
	/// テーブル定義クラスの <see cref="CommentAttribute"/> からテーブルコメントを取得する(CvBase.dll 内の同名の型を探す)
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns></returns>
	protected string GetComment(string tableName) {
		var assembly = Assembly.GetExecutingAssembly(); // CvBase.dll 自身
		var type = assembly.GetTypes().FirstOrDefault(t => t.Name == tableName);
		if (type == null) {
			return "";
		}
		var commentAttr = Attribute.GetCustomAttribute(type, typeof(CommentAttribute)) as CommentAttribute;
		return commentAttr?.Content ?? "";
	}
}

