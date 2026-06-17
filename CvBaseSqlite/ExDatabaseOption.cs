using Microsoft.Data.Sqlite;

namespace CvBaseSqlite;

public class ExDatabaseOption {
	public static void ClearAllPools() {
		SqliteConnection.ClearAllPools();
	}

	/// <summary>
	/// SQLite のプールをクリーンアップし、終了時に .db ファイルのみが残るように
	/// -wal / -shm サイドカーファイルを収束・削除します。
	/// databaseName にはデータファイル名、または接続文字列を指定できます。
	/// </summary>
	public static void ClearPools(string databaseName) {
		var databasePath = GetDatabasePath(databaseName);
		if (string.IsNullOrWhiteSpace(databasePath) || IsMemoryDatabase(databasePath)) {
			ClearAllPools();
			return;
		}

		var normalizedDatabasePath = Path.GetFullPath(databasePath);
		if (!File.Exists(normalizedDatabasePath)) {
			ClearAllPools();
			return;
		}

		// 1. pool を閉じる前に analyze 情報を格納する
		ExecuteOptimizeBeforePoolClear(normalizedDatabasePath);

		// 2. pooled 接続をすべて解放し、WAL/SHM ファイルのロックを外す
		ClearAllPools();

		// 3. Pooling=False の専用接続で WAL を収束し、journal_mode を DELETE に戻す
		FinalizeWalFiles(normalizedDatabasePath);

		// 4. 専用接続が解放された後、残った pool を再クリア
		ClearAllPools();

		// 5. ロックが外れた後に sidecar ファイルを削除
		// DeleteIfExistsWithRetry(normalizedDatabasePath + "-wal");
		// DeleteIfExistsWithRetry(normalizedDatabasePath + "-shm");
	}

	static string GetDatabasePath(string databaseName) {
		try {
			var builder = new SqliteConnectionStringBuilder(databaseName);
			if (!string.IsNullOrWhiteSpace(builder.DataSource)) {
				return builder.DataSource;
			}
		}
		catch (ArgumentException) {
		}

		return databaseName;
	}

	static bool IsMemoryDatabase(string databasePath) {
		return databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
			|| databasePath.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase);
	}

	static void ExecuteOptimizeBeforePoolClear(string databasePath) {
		using var conn = new SqliteConnection(BuildConnectionString(databasePath, pooling: true));
		conn.Open();
		ExecuteNonQuery(conn, "PRAGMA optimize;");
	}

	static void FinalizeWalFiles(string databasePath) {
		using var conn = new SqliteConnection(BuildConnectionString(databasePath, pooling: false));
		conn.Open();
		ExecuteNonQuery(conn, "PRAGMA wal_checkpoint(TRUNCATE);");
		ExecuteNonQuery(conn, "PRAGMA journal_mode=DELETE;");
	}

	static string BuildConnectionString(string databasePath, bool pooling) {
		var builder = new SqliteConnectionStringBuilder {
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWrite,
			Cache = SqliteCacheMode.Shared,
			Pooling = pooling
		};
		return builder.ConnectionString;
	}

	static void ExecuteNonQuery(SqliteConnection conn, string sql) {
		using var cmd = conn.CreateCommand();		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	static void DeleteIfExistsWithRetry(string path) {
		for (var i = 0; i < 5; i++) {
			if (!File.Exists(path)) {
				return;
			}

			try {
				File.Delete(path);
				return;
			}
			catch (IOException) when (i < 4) {
				ClearAllPools();
				Thread.Sleep(100);
			}
			catch (UnauthorizedAccessException) when (i < 4) {
				ClearAllPools();
				Thread.Sleep(100);
			}
		}

		if (File.Exists(path)) {
			File.Delete(path);
		}
	}
}
