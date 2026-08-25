using CvBase;
using MySqlConnector;
using System.Data;
using System.Data.Common;


namespace CvBaseMariadb;

/// <summary>
/// mariaDB用のデータベースクラス CommandTimeout = 9999
/// [Database class for MariaDB with CommandTimeout = 9999]
/// </summary>
public partial class ExDatabaseMaria : ExDatabase {

	public ExDatabaseMaria(DbConnection conn) : this(conn, true) {
	}

	ExDatabaseMaria(DbConnection conn, bool isOpen) : base(EnsureMariaConnection(conn), isOpen) {
		CommandTimeout = 9999;
		if (isOpen)
			UpdateVersion();
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
	}

	public override void Close() {
		if (Connection is MySqlConnection conn && conn.State == ConnectionState.Open)
			conn.Close();
	}

	public override ExDatabase CloneDb() => GetDbConn(Connection.ConnectionString);

	public override void ChangeTimeout(int timeoutSec) {
		ArgumentOutOfRangeException.ThrowIfNegative(timeoutSec);
		CommandTimeout = timeoutSec;
	}

	public override List<Tuple<string, string, long>> GetTableCounts(string tableName = "") {
		var sql = """
select table_name, coalesce(table_comment, '') table_comment
  from information_schema.tables
 where table_schema=database()
   and table_type='BASE TABLE'
   and table_name not like 'Sys%'
""";
		var args = Array.Empty<object>();
		if (!string.IsNullOrWhiteSpace(tableName)) {
			sql += " and table_name=@0";
			args = [tableName.Split('.').Last().Trim('`')];
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
		if (conn is not MySqlConnection)
			throw new ArgumentException("MySqlConnectionを指定してください。", nameof(conn));
		return conn;
	}

	void UpdateVersion() {
		if (Connection is MySqlConnection conn && conn.State == ConnectionState.Open)
			Version = conn.ServerVersion;
	}

	static string QuoteIdentifier(string identifier) =>
		$"`{identifier.Replace("`", "``")}`";
}


