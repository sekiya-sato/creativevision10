using CvBase;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.Common;


namespace CvBaseOracle;

/// <summary>
/// OracleDB用のデータベースクラス
/// [Database class for OracleDB]
/// <para>
/// Oracle は旧 CV.net からの移行元として参照するだけなので、テーブル作成(列定義の生成)は実装しない。
/// <c>GetSqlColumns</c> は基底の NotSupportedException になる。
/// </para>
/// </summary>
public partial class ExDatabaseOracle : ExDatabase {

	public ExDatabaseOracle(DbConnection conn) : base(conn) {
		if (conn != null) {
			if (conn.State == ConnectionState.Closed)
				conn.Open();
		}
	}
	/// <summary>
	/// 接続文字列を指定してOracleDBの接続を取得する
	/// </summary>
	/// <param name="connectionString">"Data Source=(簡易接続文字列IP/db名);User Id=(ユーザID);Password=(パスワード);"</param>
	/// <param name="isOpen">接続を開くかどうか</param>
	/// <returns>ExDatabaseOracleのインスタンス</returns>
	public static ExDatabaseOracle GetDbConn(string connectionString, bool isOpen = true) {
		var conn = new OracleConnection(connectionString);
		if (isOpen) {
			conn.Open();
		}
		var db = new ExDatabaseOracle(conn);
		return db;
	}
	public override void Open() {
		if (Connection is OracleConnection) {
			var connInner = (OracleConnection)Connection;
			if (connInner.State == ConnectionState.Closed)
				connInner.Open();
		}
	}
	public override void Close() {
		if (Connection is OracleConnection) {
			var connInner = (OracleConnection)Connection;
			if (connInner.State == ConnectionState.Open) {
				this.KeepConnectionAlive = false;
				connInner.Close();
			}
		}
	}
	/// <summary>
	/// テーブルが存在するかどうか
	/// </summary>
	public override bool IsExistTable(string tableName) {
		if (string.IsNullOrEmpty(tableName))
			return false;
		tableName = GetUnqualifiedTableName(tableName);
		return ExistsRow($"select * from user_tables where table_name=upper('{tableName}')");
	}
}


