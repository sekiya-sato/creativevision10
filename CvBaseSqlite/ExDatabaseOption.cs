using CvBase;
using Microsoft.Data.Sqlite;

namespace CvBaseSqlite;

/// <summary>
/// SQLite 固有の保守処理(統計情報の更新、WAL の収束、プールの後始末)
/// </summary>
public class ExDatabaseOption {

	/// <summary>統計情報を更新するSQL</summary>
	public const string OptimizeSql = "PRAGMA optimize;";

	/// <summary>WAL を本体へ書き戻して切り詰めるSQL</summary>
	public const string WalCheckpointSql = "PRAGMA wal_checkpoint(TRUNCATE);";

	/// <summary>
	/// 接続中のDBで統計情報を更新する。エラーは結果行の "Error" に入る(<see cref="ExDatabase.RawExecCmd"/>)。
	/// </summary>
	public static List<Dictionary<string, object>> ExecuteOptimize(ExDatabase db) => db.RawExecCmd(OptimizeSql);

	/// <summary>
	/// 接続中のDBで WAL チェックポイントを実行する。結果行は busy / log / checkpointed。
	/// エラーは結果行の "Error" に入る(<see cref="ExDatabase.RawExecCmd"/>)。
	/// </summary>
	public static List<Dictionary<string, object>> ExecuteWalCheckpoint(ExDatabase db) => db.RawExecCmd(WalCheckpointSql);

	/// <summary>
	/// SQLite のプールをクリーンアップし、終了時に .db ファイルのみが残るように
	/// -wal / -shm サイドカーファイルを収束・削除します。
	/// databaseName にはデータファイル名、または接続文字列を指定できます。
	/// </summary>
	public static void ClearPools(string databaseName) {
		var databasePath = GetDatabasePath(databaseName);
		if (string.IsNullOrWhiteSpace(databasePath) || IsMemoryDatabase(databasePath)) {
			SqliteConnection.ClearAllPools();
			return;
		}

		var normalizedDatabasePath = Path.GetFullPath(databasePath);
		if (!File.Exists(normalizedDatabasePath)) {
			SqliteConnection.ClearAllPools();
			return;
		}

		// 1. pool を閉じる前に analyze 情報を格納する
		ExecuteOptimizeBeforePoolClear(normalizedDatabasePath);

		// 2. pooled 接続をすべて解放し、WAL/SHM ファイルのロックを外す
		SqliteConnection.ClearAllPools();

		// 3. Pooling=False の専用接続で WAL を収束し、journal_mode を DELETE に戻す
		FinalizeWalFiles(normalizedDatabasePath);

		// 4. 専用接続が解放された後、残った pool を再クリア
		SqliteConnection.ClearAllPools();
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
		ExecuteNonQuery(conn, OptimizeSql);
	}

	static void FinalizeWalFiles(string databasePath) {
		using var conn = new SqliteConnection(BuildConnectionString(databasePath, pooling: false, defaultTimeout: 1));
		conn.Open();
		ExecuteNonQuery(conn, WalCheckpointSql);
		ExecuteNonQuery(conn, "PRAGMA journal_mode=DELETE;");
	}

	static string BuildConnectionString(string databasePath, bool pooling, int defaultTimeout = 5) {
		var builder = new SqliteConnectionStringBuilder {
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWrite,
			Cache = SqliteCacheMode.Shared,
			Pooling = pooling,
			DefaultTimeout = defaultTimeout
		};
		return builder.ConnectionString;
	}

	static void ExecuteNonQuery(SqliteConnection conn, string sql) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}
}
