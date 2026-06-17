using Microsoft.Data.Sqlite;

namespace CvBaseSqlite;

public class ExDatabaseOption {
	public static void ClearAllPools() =>
		SqliteConnection.ClearAllPools();
}
