using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Oracle.ManagedDataAccess.Client;

namespace McpOracle;

/// <summary>Oracle を参照するための stdio MCP サーバ。</summary>
static class Program {

	static OracleConnection? _connection;
	static bool _allowWrite;
	static int _shutdownDone;

	static async Task<int> Main(string[] args) {
		// stdout は JSON-RPC 専用にし、通常の診断は stderr に出力する。
		Console.SetOut(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
		if (!TryParseArgs(args, out var connectionString, out _allowWrite))
			return 1;

		try {
			_connection = new OracleConnection(connectionString);
			_connection.Open();
			OracleTools.Initialize(_connection, _allowWrite);
			LogErr($"{GetDataSource(_connection)} に接続しました ({(_allowWrite ? "書き込み可" : "読み取り専用")})");
		}
		catch (OracleException ex) {
			LogErr($"Oracle に接続できませんでした (ORA-{ex.Number}): {ex.Message}");
			Shutdown();
			return 1;
		}
		catch (Exception ex) {
			LogErr($"Oracle に接続できませんでした: {ex.Message}");
			Shutdown();
			return 1;
		}

		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
		AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
		try {
			var options = BuildOptions(_allowWrite);
			var loggerFactory = Environment.GetEnvironmentVariable("MCPORACLE_DEBUG") is { Length: > 0 }
				? new StderrLoggerFactory(Microsoft.Extensions.Logging.LogLevel.Trace) : null;
			await using var transport = new StdioServerTransport(options, loggerFactory);
			await using var server = McpServer.Create(transport, options, loggerFactory);
			await server.RunAsync(cts.Token);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) {
			LogErr($"サーバが異常終了しました: {ex}");
			return 1;
		}
		finally { Shutdown(); }
		return 0;
	}

	static bool TryParseArgs(string[] args, out string connectionString, out bool allowWrite) {
		connectionString = "";
		allowWrite = false;
		foreach (var arg in args.Where(x => x.StartsWith("--", StringComparison.Ordinal))) {
			if (string.Equals(arg, "--allow-write", StringComparison.OrdinalIgnoreCase))
				allowWrite = true;
			else {
				LogErr($"不明なオプションです: {arg}");
				LogUsage();
				return false;
			}
		}
		connectionString = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal))
			?? Environment.GetEnvironmentVariable("MCPORACLE_CONNECTION_STRING") ?? "";
		if (!string.IsNullOrWhiteSpace(connectionString))
			return true;
		LogErr("接続文字列が指定されていません。第1引数または環境変数 MCPORACLE_CONNECTION_STRING を指定してください。");
		LogUsage();
		return false;
	}

	static void LogUsage() {
		LogErr("使い方: McpOracle.exe <connection-string> [--allow-write]");
		LogErr("        環境変数 MCPORACLE_CONNECTION_STRING でも接続文字列を指定できます。");
	}

	static McpServerOptions BuildOptions(bool allowWrite) {
		var readOnly = (string name, string description) => new McpServerToolCreateOptions {
			Name = name, Description = description, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false
		};
		var tools = new List<McpServerTool> {
			McpServerTool.Create(OracleTools.ListTablesAsync, readOnly("list_tables", "接続ユーザーのテーブルとビューの一覧を返す。")),
			McpServerTool.Create(OracleTools.DescribeTableAsync, readOnly("describe_table", "指定テーブルまたはビューの列、制約、DDL を返す。")),
			McpServerTool.Create(OracleTools.ListIndexesAsync, readOnly("list_indexes", "インデックスの一覧と対象列を返す。")),
			McpServerTool.Create(OracleTools.QueryAsync, readOnly("query", "読み取り専用 SQL を実行して結果を返す。SELECT または WITH ... SELECT のみ。")),
			McpServerTool.Create(OracleTools.ExplainAsync, readOnly("explain", "読み取り専用 SQL の実行計画を返す。"))
		};
		if (allowWrite) {
			tools.Add(McpServerTool.Create(OracleTools.ExecuteAsync, new McpServerToolCreateOptions {
				Name = "execute", Description = "更新系 SQL を実行する。DB を変更するため実行前に必ず内容を確認すること。",
				ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false
			}));
		}
		var mode = allowWrite
			? "このサーバは --allow-write 付きで起動しているため execute で DB を変更できます。"
			: "このサーバは読み取り専用です。更新系 SQL は拒否されます。最終的なアクセス制御は Oracle アカウントの権限で設定してください。";
		return new McpServerOptions {
			ServerInfo = new Implementation { Name = "McpOracle", Version = "1.0.0" },
			Capabilities = new() { Tools = new() { ListChanged = false } },
			ServerInstructions = $"""
				CreativeVision10 の Oracle データベースを参照する MCP サーバです。
				{mode}

				手順: list_tables、describe_table でスキーマを確認してから query を実行してください。
				値は SQL に埋め込まず :p0, :p1, ... と parameters 配列でバインドしてください。
				query は既定 {OracleTools.DefaultMaxRows} 行・上限 {OracleTools.MaxRowsHardLimit} 行で打ち切り、truncated=true を返します。
				""",
			ToolCollection = [.. tools]
		};
	}

	static string GetDataSource(OracleConnection connection) {
		try { return new OracleConnectionStringBuilder(connection.ConnectionString).DataSource; }
		catch { return "Oracle"; }
	}

	static void Shutdown() {
		if (Interlocked.Exchange(ref _shutdownDone, 1) != 0) return;
		try { _connection?.Dispose(); }
		catch (Exception ex) { LogErr($"接続のクローズに失敗しました: {ex.Message}"); }
		_connection = null;
	}

	static void LogErr(string message) => Console.Error.WriteLine($"[McpOracle] {message}");
}
