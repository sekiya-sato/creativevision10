using Microsoft.Data.Sqlite;

namespace CvBaseSqlite;

public class ExDatabaseOption {
	public static void ClearAllPools() {
		SqliteConnection.ClearAllPools();
	}
	public static void ClearPools(string databaseName) {
		var conn = new SqliteConnection($"Data Source={databaseName}");
		conn.Open();
		conn.Close();
		ClearAllPools();
	}
}
