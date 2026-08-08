using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpSql;

/// <summary>
/// SQLite ファイルを参照するための stdio MCP サーバ。
/// 既定は読み取り専用で、--allow-write を指定したときだけ更新系ツール (execute) を登録する。
///
/// 使い方:
///   McpSql.exe &lt;dbfile&gt; [--allow-write]
///   McpSql.exe                      (環境変数 MCPSQL_DBFILE を使用)
/// </summary>
static class Program {

	static ExDatabaseSqlite? _db;
	static string _dbfile = "";
	static bool _allowWrite;
	static int _shutdownDone;

	static async Task<int> Main(string[] args) {
		// ★stdout は JSON-RPC 専用チャネル。野良 Console.WriteLine を無害化するため最初に差し替える。
		//   StdioServerTransport は Console.OpenStandardOutput() の生ストリームを使うので影響しない。
		Console.SetOut(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

		if (!TryParseArgs(args, out _dbfile, out _allowWrite))
			return 1;

		// ★接続確立までを一括で囲む。ここで例外を漏らすと未処理例外のスタックトレースが出て
		//   MCP クライアントからは原因不明の起動失敗にしか見えなくなる。
		string version;
		try {
			_db = OpenDatabase(_dbfile, _allowWrite);
			// 破損ファイル等はここで初めて顕在化する (SQLITE_CORRUPT はスキーマ読み込み時に出る)
			version = GetSqliteVersion(_db);
		}
		catch (Exception ex) {
			LogErr($"DB を開けませんでした: {ex.Message}");
			Shutdown();
			return 1;
		}
		SqliteTools.Initialize(_db, _allowWrite);
		LogErr($"{_dbfile} に接続しました (SQLite {version} / {(_allowWrite ? "書き込み可" : "読み取り専用")})");

		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) => {
			e.Cancel = true;
			cts.Cancel();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

		try {
			var options = BuildOptions(_dbfile, _allowWrite);
			// 環境変数 MCPSQL_DEBUG が設定されているときだけ、MCP 側の診断ログを stderr に出す
			var loggerFactory = Environment.GetEnvironmentVariable("MCPSQL_DEBUG") is { Length: > 0 }
				? new StderrLoggerFactory(Microsoft.Extensions.Logging.LogLevel.Trace)
				: null;
			await using var transport = new StdioServerTransport(options, loggerFactory);
			await using var server = McpServer.Create(transport, options, loggerFactory);
			await server.RunAsync(cts.Token);
		}
		catch (OperationCanceledException) {
			// Ctrl+C による正常終了
		}
		catch (Exception ex) {
			LogErr($"サーバが異常終了しました: {ex}");
			return 1;
		}
		finally {
			Shutdown();
		}
		return 0;
	}

	#region 起動処理 ===========================================================

	/// <summary>
	/// 引数を解析する。--allow-write はどの位置にあってもよい。
	/// DB ファイルは 第1の非フラグ引数 → 環境変数 MCPSQL_DBFILE の順で決定する。
	/// </summary>
	static bool TryParseArgs(string[] args, out string dbfile, out bool allowWrite) {
		dbfile = "";
		allowWrite = false;

		foreach (var a in args) {
			if (!a.StartsWith("--", StringComparison.Ordinal))
				continue;
			if (string.Equals(a, "--allow-write", StringComparison.OrdinalIgnoreCase))
				allowWrite = true;
			else {
				LogErr($"不明なオプションです: {a}");
				LogUsage();
				return false;
			}
		}

		var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
			?? Environment.GetEnvironmentVariable("MCPSQL_DBFILE");
		if (string.IsNullOrWhiteSpace(path)) {
			LogErr("DB ファイルが指定されていません。第1引数または環境変数 MCPSQL_DBFILE を指定してください。");
			LogUsage();
			return false;
		}

		try {
			dbfile = Path.GetFullPath(path);
		}
		catch (Exception ex) {
			LogErr($"DB ファイルのパスが不正です ({path}): {ex.Message}");
			return false;
		}
		// ★ここで止めないと ReadWriteCreate がパスの打ち間違いで空 DB を作ってしまい、
		//   「テーブルが 0 件」という紛らわしい結果になる。
		if (!File.Exists(dbfile)) {
			LogErr($"DB ファイルが存在しません: {dbfile}");
			return false;
		}
		return true;
	}

	static void LogUsage() {
		LogErr("使い方: McpSql.exe <dbfile> [--allow-write]");
		LogErr("        環境変数 MCPSQL_DBFILE でも DB ファイルを指定できます。");
	}

	/// <summary>
	/// DB 接続を開く。
	///
	/// ★読み取り専用時に ExDatabaseSqlite.GetDbConn() を使わない理由:
	///   GetDbConn は Mode=ReadWriteCreate で開いたうえで EnableWalMode() を実行する。
	///   その中の PRAGMA journal_mode = WAL は DB ヘッダを書き換えて -wal / -shm を生成するため、
	///   「読み取り専用」を謳うツールが起動しただけでユーザの DB を変更してしまう。
	///   しかもこの書き込みは PRAGMA query_only を適用する前に起きるので後追いでは防げない。
	///   ExDatabaseSqlite(DbConnection) の public コンストラクタは接続を開くだけなのでこちらを使う。
	/// </summary>
	static ExDatabaseSqlite OpenDatabase(string dbfile, bool allowWrite) {
		ExDatabaseSqlite db;
		if (allowWrite) {
			db = ExDatabaseSqlite.GetDbConn(dbfile);
		}
		else {
			try {
				db = OpenWith(dbfile, SqliteOpenMode.ReadOnly);
			}
			catch (SqliteException ex) when (ex.SqliteErrorCode is 14 or 8) {
				// WAL 状態の DB を ReadOnly で開くには -shm をマップできる必要がある。
				// 14=SQLITE_CANTOPEN / 8=SQLITE_READONLY。ReadWrite で開き直し query_only に頼る。
				LogErr($"ReadOnly で開けなかったため ReadWrite + query_only で継続します: {ex.Message}");
				db = OpenWith(dbfile, SqliteOpenMode.ReadWrite);
			}
		}
		// ★KeepConnectionAlive は必須。NPoco は共有接続を開閉するため、プールに戻ると
		//   下で設定する PRAGMA query_only が失われる (接続単位の設定のため)。
		db.KeepConnectionAlive = true;
		ExecPragma(db, "PRAGMA query_only = ON;");
		return db;
	}

	static ExDatabaseSqlite OpenWith(string dbfile, SqliteOpenMode mode) {
		var csb = new SqliteConnectionStringBuilder {
			DataSource = dbfile,
			Mode = mode,
			Pooling = true,
			DefaultTimeout = 30
		};
		return new ExDatabaseSqlite(new SqliteConnection(csb.ConnectionString));
	}

	static void ExecPragma(ExDatabaseSqlite db, string sql) {
		using var cmd = db.Connection.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	/// <summary>
	/// SQLite のバージョンを取得する。
	/// ExDatabaseSqlite.Version は EnableWalMode() の中で設定されるため、
	/// それを通らない読み取り専用モードでは空になる (setter は protected で外から設定できない)。
	/// </summary>
	static string GetSqliteVersion(ExDatabaseSqlite db) {
		using var cmd = db.Connection.CreateCommand();
		cmd.CommandText = "select sqlite_version();";
		return cmd.ExecuteScalar()?.ToString() ?? "?";
	}

	#endregion

	#region MCP 構成 ===========================================================

	static McpServerOptions BuildOptions(string dbfile, bool allowWrite) {
		var readOnlyOpts = (string name, string description) => new McpServerToolCreateOptions {
			Name = name,
			Description = description,
			ReadOnly = true,
			Destructive = false,
			Idempotent = true,
			OpenWorld = false
		};

		var tools = new List<McpServerTool> {
			McpServerTool.Create(SqliteTools.ListTablesAsync, readOnlyOpts(
				"list_tables",
				"DB 内のテーブルとビューの一覧を返す。行数は既定では取得しない (大きな DB では非常に遅いため)。")),
			McpServerTool.Create(SqliteTools.DescribeTableAsync, readOnlyOpts(
				"describe_table",
				"指定テーブルの列定義 (型・NOT NULL・既定値・主キー)、外部キー、CREATE 文を返す。")),
			McpServerTool.Create(SqliteTools.ListIndexesAsync, readOnlyOpts(
				"list_indexes",
				"インデックスの一覧 (対象列・UNIQUE 有無・CREATE 文) を返す。テーブル名を省略すると全テーブル分。")),
			McpServerTool.Create(SqliteTools.QueryAsync, readOnlyOpts(
				"query",
				"読み取り専用 SQL を実行して結果を返す。SELECT / VALUES / WITH ... SELECT / 読み取り系 PRAGMA のみ。")),
			McpServerTool.Create(SqliteTools.ExplainAsync, readOnlyOpts(
				"explain",
				"読み取り専用 SQL の実行計画 (EXPLAIN QUERY PLAN) を返す。インデックスが使われているかの確認に使う。"))
		};
		if (allowWrite) {
			tools.Add(McpServerTool.Create(SqliteTools.ExecuteAsync, new McpServerToolCreateOptions {
				Name = "execute",
				Description = "更新系 SQL (INSERT / UPDATE / DELETE / DDL) を実行する。DB を変更するため実行前に必ず内容を確認すること。",
				ReadOnly = false,
				Destructive = true,
				Idempotent = false,
				OpenWorld = false
			}));
		}

		var mode = allowWrite
			? "このサーバは --allow-write 付きで起動しているため execute ツールで DB を変更できます。実行前に必ず内容を確認してください。"
			: "このサーバは読み取り専用です。INSERT/UPDATE/DELETE/DDL/ATTACH は全て拒否されます。";

		return new McpServerOptions {
			ServerInfo = new Implementation { Name = "McpSql", Version = "1.0.0" },
			Capabilities = new() { Tools = new() { ListChanged = false } },
			ServerInstructions = $"""
				CreativeVision10 の SQLite データベース ({Path.GetFileName(dbfile)}) を参照する MCP サーバです。
				{mode}

				手順: まず list_tables でテーブルを確認し、describe_table でスキーマを見てから query を実行してください。
				query で実行できるのは SELECT / VALUES / WITH ... SELECT / 読み取り系 PRAGMA のみで、文は 1 つだけです。
				値は SQL に埋め込まず @p0, @p1, ... と parameters 配列でバインドしてください
				(SQLite は動的型のため、整数列を文字列 '5' と比較すると一致しません)。
				query は既定 {SqliteTools.DefaultMaxRows} 行・上限 {SqliteTools.MaxRowsHardLimit} 行で打ち切られ、
				打ち切られた場合は truncated=true が返ります。
				Sys* で始まるシステム系テーブル (ログイン情報を含む) は list_tables の既定では表示されません。
				""",
			ToolCollection = [.. tools]
		};
	}

	#endregion

	#region 終了処理 ===========================================================

	static void Shutdown() {
		if (Interlocked.Exchange(ref _shutdownDone, 1) != 0)
			return;
		try {
			_db?.Close();
		}
		catch (Exception ex) {
			LogErr($"接続のクローズに失敗しました: {ex.Message}");
		}
		try {
			if (_allowWrite) {
				// ★ClearPools は PRAGMA optimize / wal_checkpoint(TRUNCATE) / journal_mode=DELETE を実行する。
				//   つまり DB を書き換えるので、読み取り専用モードでは絶対に呼んではならない。
				//   (CvServer が同時起動している場合は journal_mode を奪うことにもなる)
				ExDatabaseOption.ClearPools(_dbfile);
			}
			else {
				SqliteConnection.ClearAllPools();
			}
		}
		catch (Exception ex) {
			LogErr($"終了処理に失敗しました: {ex.Message}");
		}
		_db = null;
	}

	#endregion

	/// <summary>診断出力は必ず stderr へ。stdout は JSON-RPC 専用。</summary>
	static void LogErr(string message) => Console.Error.WriteLine($"[McpSql] {message}");
}
