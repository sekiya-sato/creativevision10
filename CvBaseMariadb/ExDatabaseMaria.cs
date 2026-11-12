using CvBase;
using MySqlConnector;
using NPoco;
using System.Data;
using System.Data.Common;
using System.Reflection;


namespace CvBaseMariadb;

/// <summary>
/// mariaDB用のデータベースクラス CommandTimeout = 9999
/// [Database class for MariaDB with CommandTimeout = 9999]
/// </summary>
public partial class ExDatabaseMaria : ExDatabase {

	/// <summary>クライアント由来SQLをMariaDB方言へ変換する。</summary>
	public override CvBase.Sql.ISqlDialect Dialect => CvBase.Sql.SqlDialects.Maria;

	/// <summary>SQLiteのBINARY照合に合わせる照合順序</summary>
	public const string RequiredCollation = "utf8mb4_bin";

	/// <summary>
	/// テーブルの文字セットと照合順序を固定する。
	/// <para>
	/// MariaDBの既定照合順序 (utf8mb4_general_ci / uca1400_ai_ci) は大文字小文字とかなを
	/// 同一視するため、<c>=</c>、<c>LIKE</c>、<c>ORDER BY</c>、<c>DISTINCT</c>、<c>GROUP BY</c> の
	/// 結果がSQLiteと変わる。方言変換では直せない差なのでテーブル作成時に決め切る。
	/// </para>
	/// </summary>
	protected override string CreateTableSuffix => $" DEFAULT CHARSET=utf8mb4 COLLATE={RequiredCollation}";

	/// <summary>
	/// 接続先データベースの既定照合順序が <see cref="RequiredCollation"/> であることを確かめる。
	/// 既存テーブルの照合順序はテーブル定義側で決まるが、後から作られる一時テーブルや
	/// 式の比較でDB既定が効くため、DB自体も揃えておく必要がある。
	/// </summary>
	public override IReadOnlyList<string> ValidateSchema() {
		// NPoco は "@@" を "@" へ戻すため、システム変数 @@collation_database ではなく information_schema から読む
		var collation = ExecuteScalar<string>("select DEFAULT_COLLATION_NAME from information_schema.SCHEMATA where SCHEMA_NAME = database()") ?? "";
		return collation.Equals(RequiredCollation, StringComparison.OrdinalIgnoreCase)
			? []
			: [$"データベースの照合順序を {RequiredCollation} にしてください(SQLiteのBINARY照合に合わせるため)。現在={collation}"];
	}

	public ExDatabaseMaria(DbConnection conn) : this(conn, true) {
	}

	ExDatabaseMaria(DbConnection conn, bool isOpen) : base(EnsureMariaConnection(conn), isOpen) {
		CommandTimeout = 9999;
		if (isOpen) {
			UpdateVersion();
			ApplySessionSetup();
		}
		// NPoco はトランザクション終了時などに接続を閉じて開き直す。プールへ戻した接続は再利用時に
		// セッション状態がリセットされ、sql_mode(PIPES_AS_CONCAT / NO_BACKSLASH_ESCAPES)が消える。
		// 消えると `||` が論理和になり、ドライバの NoBackslashEscapes と食い違って '\' が壊れるため、
		// どの経路で開いても必ずセッション設定を当て直す。
		if (Connection is MySqlConnection mysql)
			mysql.StateChange += (_, e) => {
				if (e.CurrentState == ConnectionState.Open && e.OriginalState != ConnectionState.Open)
					ApplySessionSetup();
			};
	}

	/// <summary>
	/// MariaDB用のデータベース接続を取得します。
	/// </summary>
	/// <param name="connectionString">"Server=(サーバIP);Port=(サーバPort);User ID=(ユーザID);Password=(パスワード);Database=(データベース)"</param>
	/// <param name="isOpen">接続を開くかどうか</param>
	/// <returns>ExDatabaseMariaのインスタンス</returns>
	public static ExDatabaseMaria GetDbConn(string connectionString, bool isOpen = true) {
		// "Server=localhost;Port=3306;Database=mariadb;Uid=user;Pwd=password;"
		// "Pooling=true;ConnectionIdleTimeout=30;MinimumPoolSize=10;MaximumPoolSize=100;"AllowUserVariables=true;"
		var conn = new MySqlConnection(connectionString);
		return new ExDatabaseMaria(conn, isOpen);
	}

	public override void Open() {
		if (Connection is not MySqlConnection conn)
			return;
		if (conn.State == ConnectionState.Closed)
			conn.Open();
		UpdateVersion();
		ApplySessionSetup();
	}

	/// <summary>
	/// 接続直後のセッション設定を適用する。
	/// <para>
	/// 文字列連結 <c>||</c> と <c>ESCAPE '\'</c> をSQLite と同じ意味にするため
	/// <c>PIPES_AS_CONCAT</c> と <c>NO_BACKSLASH_ESCAPES</c> を足す。
	/// <c>ONLY_FULL_GROUP_BY</c> と <c>STRICT_TRANS_TABLES</c> は入れない(SQLiteの緩さに合わせる)。
	/// </para>
	/// </summary>
	void ApplySessionSetup() {
		if (Connection is not MySqlConnection conn || conn.State != ConnectionState.Open)
			return;
		foreach (var command in Dialect.SessionSetupCommands) {
			using var cmd = conn.CreateCommand();
			cmd.CommandText = command;
			cmd.ExecuteNonQuery();
		}
	}

	public override void Close() {
		if (Connection is MySqlConnection conn && conn.State == ConnectionState.Open)
			conn.Close();
	}

	public override void ChangeTimeout(int timeoutSec) {
		ArgumentOutOfRangeException.ThrowIfNegative(timeoutSec);
		CommandTimeout = timeoutSec;
	}

	const int _default_varchar2_length = 255; // Use for GetSqlColumns()
	const int _default_list_length = 1000; // Use for GetSqlColumns()
	const string _default_varchar = " default ''";// Use for GetSqlColumns()

	/// <summary>
	/// クラスの中に含まれるプロパティの配列を"Name database型"で返す(MariaDB の型名)。
	/// <para>
	/// 1) 列名を `` ` `` で囲む（<c>SysUpdateDb.Sql</c> のような予約語の列で CREATE TABLE が失敗するため）。
	/// 2) <c>[SerializedColumn]</c>（JSONを文字列で保存する列）は <c>ColumnSizeDml</c> の桁数に関係なく longtext にする。
	///    SQLiteは桁数を検査しないため実データが varchar(n) を超えており、MariaDB(厳格モード)では投入できない。
	///    JSON型(json_valid検査)にしないのは、空文字 '' の既定値・既存値を受け付けるため。
	/// </para>
	/// </summary>
	public override List<string> GetSqlColumns(Type classT) {
		var result = new List<string>();
		foreach (var property in GetColumnProperties(classT)) {
			var definition = GetMariaColumnType(property);
			if (definition == null)
				continue; // 列にしない項目 [Not a column]
			if (property.GetCustomAttribute<SerializedColumnAttribute>() != null
				&& !definition.StartsWith("JSON", StringComparison.OrdinalIgnoreCase)) {
				definition = "longtext";
			}
			result.Add($"{QuoteIdentifier(property.Name)} {definition}");
		}
		return result;
	}

	/// <summary>
	/// プロパティに対応するMariaDBの型定義を返す。列にしない場合は null。
	/// </summary>
	static string? GetMariaColumnType(PropertyInfo item) {
		// カラムサイズの定義があるかどうか [Whether there is a column size definition]
		// 文字列・JSON・列挙以外の指定はプロパティの型から決める(型名が空の列を作らない)
		var attrSize = item.GetCustomAttribute<ColumnSizeDmlAttribute>();
		var type = attrSize?.ColType switch {
			ColumnType.String => $"varchar({attrSize.Size}) " + _default_varchar,
			ColumnType.Json => "JSON",
			ColumnType.Enum => "int not null default 0",
			_ => null
		};
		if (type == null) {
			switch (item.PropertyType.Name) {
				case "Boolean":
					type = "TINYINT not null default 0";
					break;
				case "Byte":
				case "SByte":
					type = "varchar(1) not null default '0'";
					break;
				case "Char":
					type = "varchar(1) " + _default_varchar;
					break;
				case "Decimal":
					type = "decimal(14,8) not null default 0";
					break;
				case "Double":
					type = "double(14,8) not null default 0";
					break;
				case "Single":
					type = "float(14,8) not null default 0";
					break;
				case "Int16":
				case "Int32":
				case "UInt16":
				case "UInt32":
					type = "int not null default 0";
					break;
				case "Int64":
				case "UInt64":
					type = "bigint not null default 0";
					break;
				case "String":
					type = $"varchar({_default_varchar2_length}) " + _default_varchar;
					break;
				default:
					if (item.PropertyType.Name.StartsWith("List"))
						return null;
					type = $"varchar({_default_list_length}) " + _default_varchar;
					break;
			}
		}
		if (item.Name == "Id") // 列Idであればキー項目 [If it is a column Id, it is a key item]
			type = "bigint auto_increment";
		return type;
	}

	/// <summary>
	/// テーブルが存在するかどうか
	/// </summary>
	public override bool IsExistTable(string tableName) {
		if (string.IsNullOrEmpty(tableName))
			return false;
		tableName = GetUnqualifiedTableName(tableName);
		var schema = GetCurrentDatabase();
		return ExistsRow($"SELECT TABLE_NAME,TABLE_ROWS,TABLE_COMMENT,auto_increment from INFORMATION_SCHEMA.TABLES \r\n WHERE table_schema='{schema}' and TABLE_NAME = '{tableName}'");
	}

	/// <summary>
	/// 索引を削除するSQL。MariaDB は <c>ON 表名</c> が無いと構文エラーになる
	/// (付けないと CreateTable の強制再作成が索引の削除で失敗し、索引が作られない)。
	/// </summary>
	protected override string GetSqlDropIndex(string tableName, string indexName) =>
		$"DROP INDEX IF EXISTS {indexName} ON {tableName}";

	/// <summary>
	/// コメントの作成(上書)
	/// [Create comment (overwrite)]
	/// </summary>
	public override int CreateComment(Type classT) {
		var ret = 0;
		var tableNameAttr = classT.GetCustomAttribute<CommentAttribute>();
		var tableName = GetTableName(classT);
		if (tableNameAttr != null) {
			// コメント内の ' を二重化する（MasterShohin のコメントに ' を含み、MariaDBで構文エラーになっていた）
			var sql = $"ALTER TABLE {tableName} COMMENT = '{tableNameAttr.Content.Replace("'", "''")}'";
			ret = Execute(sql);
		}
		return ret;
	}

	public override IReadOnlyList<string> GetUserTableNames() =>
		Fetch<string>("select table_name from information_schema.tables where table_schema=database() and table_type='BASE TABLE' order by table_name");

	public override List<Tuple<string, string, long>> GetTableCounts(string tableName = "") {
		var sql = """
select table_name, coalesce(table_comment, '') table_comment
  from information_schema.tables
 where table_schema=database()
   and table_type='BASE TABLE'
""";
		var args = Array.Empty<object>();
		if (!string.IsNullOrWhiteSpace(tableName)) {
			sql += " and table_name=@0";
			args = [tableName.Split('.').Last().Trim('`')];
		}
		else {
			sql += " and table_name not like 'Sys%'";
		}
		sql += " order by table_name";

		var result = new List<Tuple<string, string, long>>();
		foreach (var row in Fetch<dynamic>(sql, args)) {
			var currentTableName = Convert.ToString(row.table_name) ?? "";
			var comment = Convert.ToString(row.table_comment) ?? "";
			var count = ExecuteScalar<long>($"select count(*) from {QuoteIdentifier(currentTableName)}");
			result.Add(Tuple.Create(currentTableName, comment, count));
		}
		return result;
	}

	static DbConnection EnsureMariaConnection(DbConnection conn) {
		ArgumentNullException.ThrowIfNull(conn);
		if (conn is not MySqlConnection mysql)
			throw new ArgumentException("MySqlConnectionを指定してください。", nameof(conn));
		// セッションで NO_BACKSLASH_ESCAPES を設定する(ApplySessionSetup)ため、ドライバにも同じ前提を伝える。
		// 伝えないと MySqlConnector がパラメータの '\' を '\\' へエスケープし、サーバは2文字として保存する
		// (JSON の あ が \\u3042 になり、名称・明細が読めなくなる)。
		if (mysql.State == ConnectionState.Closed) {
			var builder = new MySqlConnectionStringBuilder(mysql.ConnectionString) { NoBackslashEscapes = true };
			mysql.ConnectionString = builder.ConnectionString;
		}
		return conn;
	}

	/// <summary>ドライバが '\' をエスケープしない設定になっているか（単体テスト用）</summary>
	public bool IsNoBackslashEscapes =>
		Connection is MySqlConnection mysql && new MySqlConnectionStringBuilder(mysql.ConnectionString).NoBackslashEscapes;

	void UpdateVersion() {
		if (Connection is MySqlConnection conn && conn.State == ConnectionState.Open)
			Version = conn.ServerVersion;
	}

	static string QuoteIdentifier(string identifier) =>
		$"`{identifier.Replace("`", "``")}`";
}


