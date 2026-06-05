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

	public ExDatabaseMaria(DbConnection conn) : base(conn) {
		if (conn != null) {
			this.CommandTimeout = 9999;
			if (conn.State == ConnectionState.Closed)
				conn.Open();
		}
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
		if (isOpen) {
			conn.Open();
		}
		var db = new ExDatabaseMaria(conn);
		return db;
	}
	public override void Open() {
		if (Connection is MySqlConnection) {
			var connInner = (MySqlConnection)Connection;
			if (connInner.State == ConnectionState.Closed)
				connInner.Open();
		}
	}
	public override void Close() {
		if (Connection is MySqlConnection) {
			var connInner = (MySqlConnection)Connection;
			if (connInner.State == ConnectionState.Open)
				connInner.Close();
		}
	}

}


