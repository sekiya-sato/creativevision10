using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace McpSql;

/// <summary>
/// MCP ツールの実体。全ツールは 1 本の接続を SemaphoreSlim で直列化して使う。
/// </summary>
static class SqliteTools {

	/// <summary>maxRows の既定値</summary>
	public const int DefaultMaxRows = 100;
	/// <summary>maxRows の上限 (これを超える指定は黙ってクランプする)</summary>
	public const int MaxRowsHardLimit = 1000;
	/// <summary>1 セルあたりの最大文字数</summary>
	const int MaxCellChars = 4000;
	/// <summary>応答 JSON の最大バイト数 (UTF-8)</summary>
	const int MaxResponseBytes = 100_000;
	/// <summary>BLOB のプレビューに含める先頭バイト数</summary>
	const int MaxBlobPreviewBytes = 64;
	/// <summary>行数取得の UNION ALL を分割する単位 (SQLITE_MAX_COMPOUND_SELECT 対策)</summary>
	const int CountChunkSize = 200;

	static ExDatabaseSqlite _db = null!;
	static bool _allowWrite;
	/// <summary>
	/// ★接続の直列化。McpServer は tools/call を並行ディスパッチするが、
	///   NPoco.Database / SqliteConnection / SqliteCommand はいずれも非スレッドセーフであり、
	///   ExDatabase.RawLastError も共有可変状態のため必須。
	/// </summary>
	static readonly SemaphoreSlim _gate = new(1, 1);

	public static void Initialize(ExDatabaseSqlite db, bool allowWrite) {
		_db = db;
		_allowWrite = allowWrite;
	}

	#region ツール本体 =========================================================

	/// <summary>テーブル/ビューの一覧を返す</summary>
	public static async Task<string> ListTablesAsync(
		[Description("テーブル名の前方一致フィルタ。省略時は全件。")]
		string? namePrefix = null,
		[Description("各テーブルの行数を count(*) で取得するか。大きな DB では非常に遅いため既定は false。")]
		bool includeRowCounts = false,
		[Description("Sys* で始まるシステム系テーブルも含めるか。既定は false。")]
		bool includeSystem = false,
		CancellationToken cancellationToken = default) {

		return await RunAsync((conn, ct) => {
			// ★ExDatabase.GetTableCounts() は使わない。Sys* を無条件に除外し、
			//   テーブルごとに count(*) を必ず実行するため (実 DB は 9GB 超になりうる)。
			var rows = ExecOrThrow("""
				SELECT type, name, sql FROM sqlite_master
				 WHERE type IN ('table','view') AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
				 ORDER BY type, name
				""");

			var items = new List<(string Type, string Name)>();
			foreach (var row in rows) {
				var name = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
				var type = row.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
				if (name.Length == 0)
					continue;
				if (!includeSystem && name.StartsWith("Sys", StringComparison.OrdinalIgnoreCase))
					continue;
				if (!string.IsNullOrEmpty(namePrefix) && !name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
					continue;
				items.Add((type, name));
			}

			var counts = includeRowCounts
				? GetRowCounts(items.Where(x => x.Type == "table").Select(x => x.Name).ToList(), ct)
				: null;

			var buffer = new ArrayBufferWriter<byte>();
			using var w = new Utf8JsonWriter(buffer);
			w.WriteStartObject();
			w.WriteStartArray("tables");
			foreach (var (type, name) in items) {
				w.WriteStartObject();
				w.WriteString("name", name);
				w.WriteString("type", type);
				if (counts != null && counts.TryGetValue(name, out var cnt))
					w.WriteNumber("row_count", cnt);
				w.WriteEndObject();
			}
			w.WriteEndArray();
			w.WriteNumber("count", items.Count);
			w.WriteBoolean("row_counts_included", includeRowCounts);
			w.WriteBoolean("system_tables_included", includeSystem);
			w.WriteEndObject();
			w.Flush();
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>テーブル定義 (列・主キー・NOT NULL・既定値・外部キー・CREATE 文) を返す</summary>
	public static async Task<string> DescribeTableAsync(
		[Description("対象のテーブル名またはビュー名。")]
		string table,
		CancellationToken cancellationToken = default) {

		if (string.IsNullOrWhiteSpace(table))
			throw new McpException("テーブル名を指定してください。");

		return await RunAsync((conn, ct) => {
			// ★テーブル名は連結せず必ずバインドする。table-valued pragma 関数なら値として渡せる。
			var master = ReadTable(conn, "SELECT type, sql FROM sqlite_master WHERE name = @p0", [table], ct);
			if (master.Rows.Count == 0)
				throw new McpException($"テーブル '{table}' は存在しません。list_tables で一覧を確認してください。");

			var type = master.Rows[0][0]?.ToString() ?? "";
			var createSql = master.Rows[0][1]?.ToString();

			var columns = ReadTable(conn, "SELECT cid, name, type, \"notnull\", dflt_value, pk FROM pragma_table_info(@p0)", [table], ct);
			var foreignKeys = ReadTable(conn, "SELECT id, seq, \"table\", \"from\", \"to\", on_update, on_delete, match FROM pragma_foreign_key_list(@p0)", [table], ct);

			var buffer = new ArrayBufferWriter<byte>();
			using var w = new Utf8JsonWriter(buffer);
			w.WriteStartObject();
			w.WriteString("table", table);
			w.WriteString("type", type);
			WriteObjects(w, "columns", columns);
			WriteObjects(w, "foreign_keys", foreignKeys);
			if (createSql is null)
				w.WriteNull("create_sql");
			else
				WriteTruncatedString(w, "create_sql", createSql);
			w.WriteEndObject();
			w.Flush();
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>インデックス一覧を返す</summary>
	public static async Task<string> ListIndexesAsync(
		[Description("対象テーブル名。省略時は全テーブルのインデックスを列挙する。")]
		string? table = null,
		CancellationToken cancellationToken = default) {

		return await RunAsync((conn, ct) => {
			var sql = """
				SELECT m.name AS tbl, il.name AS idx, il."unique" AS uq, il.origin, il.partial, ii.seqno, ii.name AS col,
				       (SELECT s.sql FROM sqlite_master s WHERE s.type = 'index' AND s.name = il.name) AS idx_sql
				  FROM sqlite_master m
				  JOIN pragma_index_list(m.name) il
				  LEFT JOIN pragma_index_info(il.name) ii
				 WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite\_%' ESCAPE '\'
				""";
			object?[]? values = null;
			if (!string.IsNullOrWhiteSpace(table)) {
				sql += " AND m.name = @p0";
				values = [table];
			}
			sql += " ORDER BY m.name, il.seq, ii.seqno";

			var result = ReadTable(conn, sql, values, ct);

			// (テーブル, インデックス) 単位に列をまとめる
			var grouped = new List<(string Tbl, string Idx, long Unique, string Origin, long Partial, string? Sql, List<string> Cols)>();
			foreach (var r in result.Rows) {
				var tbl = r[0]?.ToString() ?? "";
				var idx = r[1]?.ToString() ?? "";
				var col = r[6]?.ToString();
				var last = grouped.Count > 0 ? grouped[^1] : default;
				if (grouped.Count == 0 || last.Tbl != tbl || last.Idx != idx) {
					grouped.Add((tbl, idx, ToLong(r[2]), r[3]?.ToString() ?? "", ToLong(r[4]), r[7]?.ToString(), col is null ? [] : [col]));
				}
				else if (col is not null) {
					last.Cols.Add(col);
				}
			}

			var buffer = new ArrayBufferWriter<byte>();
			using var w = new Utf8JsonWriter(buffer);
			w.WriteStartObject();
			w.WriteStartArray("indexes");
			foreach (var g in grouped) {
				w.WriteStartObject();
				w.WriteString("table", g.Tbl);
				w.WriteString("name", g.Idx);
				w.WriteBoolean("unique", g.Unique != 0);
				// origin: c=CREATE INDEX / u=UNIQUE制約 / pk=主キー
				w.WriteString("origin", g.Origin);
				w.WriteBoolean("partial", g.Partial != 0);
				w.WriteStartArray("columns");
				foreach (var c in g.Cols)
					w.WriteStringValue(c);
				w.WriteEndArray();
				if (g.Sql is null)
					w.WriteNull("sql");
				else
					WriteTruncatedString(w, "sql", g.Sql);
				w.WriteEndObject();
			}
			w.WriteEndArray();
			w.WriteNumber("count", grouped.Count);
			w.WriteEndObject();
			w.Flush();
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>読み取り専用 SQL を実行して結果を返す</summary>
	public static async Task<string> QueryAsync(
		[Description("実行する読み取り専用 SQL。SELECT / VALUES / WITH ... SELECT / 読み取り系 PRAGMA のみ。文は 1 つだけ (末尾の ; は可)。")]
		string sql,
		[Description("SQL 内の @p0, @p1, ... に先頭から順にバインドする値の配列。JSON の数値・文字列・真偽値・null が使える。")]
		JsonElement[]? parameters = null,
		[Description("返却する最大行数。既定 100、上限 1000。超過分は truncated=true で示される。")]
		int maxRows = DefaultMaxRows,
		CancellationToken cancellationToken = default) {

		EnsureReadOnlySql(sql);
		var limit = Math.Clamp(maxRows, 1, MaxRowsHardLimit);
		var values = ToDbValues(parameters);
		return await RunAsync((conn, ct) => ExecuteQueryJson(conn, sql, values, limit, ct), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>クエリ実行計画を返す</summary>
	public static async Task<string> ExplainAsync(
		[Description("実行計画を取得する読み取り専用 SQL。")]
		string sql,
		[Description("SQL 内の @p0, @p1, ... にバインドする値の配列。")]
		JsonElement[]? parameters = null,
		[Description("true なら EXPLAIN QUERY PLAN (既定)、false なら EXPLAIN (VDBE バイトコード)。")]
		bool queryPlan = true,
		CancellationToken cancellationToken = default) {

		EnsureReadOnlySql(sql);
		var values = ToDbValues(parameters);
		var prefixed = (queryPlan ? "EXPLAIN QUERY PLAN " : "EXPLAIN ") + sql;
		return await RunAsync((conn, ct) => ExecuteQueryJson(conn, prefixed, values, MaxRowsHardLimit, ct), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>更新系 SQL を実行する (--allow-write 指定時のみ登録される)</summary>
	public static async Task<string> ExecuteAsync(
		[Description("実行する更新系 SQL (INSERT / UPDATE / DELETE / CREATE / ALTER / DROP など)。文は 1 つだけ。ATTACH / DETACH / PRAGMA は拒否される。")]
		string sql,
		[Description("SQL 内の @p0, @p1, ... にバインドする値の配列。")]
		JsonElement[]? parameters = null,
		CancellationToken cancellationToken = default) {

		if (!_allowWrite)
			throw new McpException("このサーバは読み取り専用で起動しています。書き込みには --allow-write が必要です。");
		if (!SqlGuard.TryValidateWrite(sql, out var error))
			throw new McpException($"実行できません: {error}\n対象SQL: {Head(sql)}");

		var values = ToDbValues(parameters);
		return await RunAsync((conn, ct) => {
			var sw = Stopwatch.StartNew();
			int affected;
			// ★query_only を開ける窓は 1 文ぶんだけ。RunAsync がセマフォを保持しているので
			//   この間に読み取りツールが OFF を観測することはない。
			ExecPragma(conn, "PRAGMA query_only = OFF;");
			try {
				using var cmd = CreateCommand(conn, sql, values);
				using var reg = RegisterCancel(cmd, ct);
				affected = cmd.ExecuteNonQuery();
			}
			finally {
				ExecPragma(conn, "PRAGMA query_only = ON;");
			}
			var buffer = new ArrayBufferWriter<byte>();
			using var w = new Utf8JsonWriter(buffer);
			w.WriteStartObject();
			w.WriteNumber("rows_affected", affected);
			w.WriteNumber("elapsed_ms", sw.ElapsedMilliseconds);
			w.WriteEndObject();
			w.Flush();
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}, cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region 実行基盤 ===========================================================

	/// <summary>
	/// 接続を直列化して処理を実行し、例外を McpException に正規化する。
	/// </summary>
	static async Task<string> RunAsync(Func<SqliteConnection, CancellationToken, string> work, CancellationToken ct) {
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try {
			if (_db.Connection is not SqliteConnection conn)
				throw new McpException("SQLite 接続が初期化されていません。");
			// 念のため毎回再適用する。プロセス内 PRAGMA なのでコストは無視できる。
			ExecPragma(conn, "PRAGMA query_only = ON;");
			return work(conn, ct);
		}
		catch (McpException) {
			throw;
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (SqliteException ex) {
			throw new McpException($"SQLite エラー ({ex.SqliteErrorCode}): {ex.Message}");
		}
		catch (Exception ex) {
			// ★診断は必ず stderr へ。stdout は JSON-RPC 専用。
			Console.Error.WriteLine($"[McpSql] {ex}");
			throw new McpException(ex.Message);
		}
		finally {
			_gate.Release();
		}
	}

	static void EnsureReadOnlySql(string sql) {
		if (!SqlGuard.TryValidateReadOnly(sql, out var error))
			throw new McpException($"読み取り専用モードでは実行できません: {error}\n対象SQL: {Head(sql)}");
	}

	static string Head(string sql) => sql.Length <= 200 ? sql : sql[..200] + "…";

	static void ExecPragma(SqliteConnection conn, string sql) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	/// <summary>
	/// CvBase.ExDatabase.RawExecCmd を使って固定 SQL を実行する。
	/// ★RawExecCmd は例外を握り潰して [{"Error": ...}] を返すため、必ず RawLastError で判定する。
	///   "Error" キーの有無で判定すると `select 1 as Error` を誤検知する。
	/// </summary>
	static List<Dictionary<string, object>> ExecOrThrow(string sql, object[]? para = null) {
		var rows = _db.RawExecCmd(sql, para);
		if (!string.IsNullOrEmpty(_db.RawLastError))
			throw new McpException($"SQL 実行エラー: {_db.RawLastError}");
		return rows;
	}

	static SqliteCommand CreateCommand(SqliteConnection conn, string sql, IReadOnlyList<object?>? values) {
		var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		if (values != null) {
			for (int i = 0; i < values.Count; i++) {
				var p = cmd.CreateParameter();
				p.ParameterName = $"@p{i}";
				p.Value = values[i] ?? DBNull.Value;
				cmd.Parameters.Add(p);
			}
		}
		return cmd;
	}

	/// <summary>
	/// ★Microsoft.Data.Sqlite の ExecuteReaderAsync は実行前にしか ct を見ない。
	///   実行中のクエリを止められるのは sqlite3_interrupt (= Cancel()) だけ。
	///   なお CommandTimeout は busy/locked のリトライ時間でありクエリタイムアウトではない。
	/// </summary>
	static CancellationTokenRegistration RegisterCancel(SqliteCommand cmd, CancellationToken ct)
		=> ct.Register(() => {
			try {
				cmd.Cancel();
			}
			catch {
				// 実行完了・破棄との競合は無視してよい
			}
		});

	/// <summary>内部のスキーマ問い合わせ用。列名と行 (object?[]) を返す。</summary>
	static (string[] Columns, List<object?[]> Rows) ReadTable(SqliteConnection conn, string sql, object?[]? values, CancellationToken ct) {
		using var cmd = CreateCommand(conn, sql, values);
		using var reg = RegisterCancel(cmd, ct);
		using var reader = cmd.ExecuteReader();
		var columns = new string[reader.FieldCount];
		for (int i = 0; i < columns.Length; i++)
			columns[i] = reader.GetName(i);
		var rows = new List<object?[]>();
		while (reader.Read()) {
			ct.ThrowIfCancellationRequested();
			var row = new object?[columns.Length];
			for (int i = 0; i < columns.Length; i++)
				row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			rows.Add(row);
		}
		return (columns, rows);
	}

	/// <summary>指定テーブル群の行数を UNION ALL でまとめて取得する。</summary>
	static Dictionary<string, long> GetRowCounts(List<string> tableNames, CancellationToken ct) {
		var result = new Dictionary<string, long>(StringComparer.Ordinal);
		for (int offset = 0; offset < tableNames.Count; offset += CountChunkSize) {
			ct.ThrowIfCancellationRequested();
			var chunk = tableNames.Skip(offset).Take(CountChunkSize)
				.Select(n => $"SELECT '{n.Replace("'", "''")}' AS name, count(*) AS cnt FROM {QuoteIdent(n)}");
			var rows = ExecOrThrow(string.Join(" UNION ALL ", chunk));
			foreach (var row in rows) {
				var name = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "";
				if (name.Length > 0 && row.TryGetValue("cnt", out var c))
					result[name] = ToLong(c);
			}
		}
		return result;
	}

	static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

	static long ToLong(object? v) => v is null || v is DBNull ? 0L : Convert.ToInt64(v, CultureInfo.InvariantCulture);

	#endregion

	#region JSON 出力 =========================================================

	/// <summary>
	/// クエリを実行して結果を JSON 文字列にする。
	/// 行はオブジェクトではなく配列で出す (キー名の反復を避けるため、
	/// かつ JOIN で重複する列名 Id/Vdc/Vdu を保持するため)。
	/// </summary>
	static string ExecuteQueryJson(SqliteConnection conn, string sql, IReadOnlyList<object?>? values, int maxRows, CancellationToken ct) {
		var sw = Stopwatch.StartNew();
		using var cmd = CreateCommand(conn, sql, values);
		using var reg = RegisterCancel(cmd, ct);
		using var reader = cmd.ExecuteReader();

		var buffer = new ArrayBufferWriter<byte>();
		using var w = new Utf8JsonWriter(buffer);
		w.WriteStartObject();

		var fieldCount = reader.FieldCount;
		w.WriteStartArray("columns");
		for (int i = 0; i < fieldCount; i++)
			w.WriteStringValue(reader.GetName(i));
		w.WriteEndArray();

		var rowCount = 0;
		var truncated = false;
		string? reason = null;
		w.WriteStartArray("rows");
		while (reader.Read()) {
			ct.ThrowIfCancellationRequested();
			if (rowCount >= maxRows) {
				truncated = true;
				reason = "行数上限";
				break;
			}
			// ★書き込み前に判定する。完成後に文字列を切ると不正な JSON になるため。
			if (w.BytesCommitted + w.BytesPending >= MaxResponseBytes) {
				truncated = true;
				reason = "文字数上限";
				break;
			}
			w.WriteStartArray();
			for (int i = 0; i < fieldCount; i++)
				WriteCell(w, reader, i);
			w.WriteEndArray();
			rowCount++;
		}
		w.WriteEndArray();

		w.WriteNumber("row_count", rowCount);
		w.WriteBoolean("truncated", truncated);
		if (reason != null)
			w.WriteString("truncated_reason", reason);
		w.WriteNumber("elapsed_ms", sw.ElapsedMilliseconds);
		w.WriteEndObject();
		w.Flush();
		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	/// <summary>
	/// SqliteDataReader の値を JSON 安全な形で書き出す。
	/// </summary>
	static void WriteCell(Utf8JsonWriter w, SqliteDataReader reader, int i) {
		if (reader.IsDBNull(i)) {
			w.WriteNullValue();
			return;
		}
		switch (reader.GetValue(i)) {
			case long l:
				w.WriteNumberValue(l);
				break;
			case double d:
				// NaN / ±Infinity は JSON にできない (Utf8JsonWriter が例外を投げる)
				if (double.IsFinite(d))
					w.WriteNumberValue(d);
				else
					w.WriteStringValue(d.ToString(CultureInfo.InvariantCulture));
				break;
			case decimal m:
				w.WriteNumberValue(m);
				break;
			case bool b:
				w.WriteBooleanValue(b);
				break;
			case string s:
				WriteTruncatedString(w, s);
				break;
			case byte[] blob:
				// BLOB は先頭だけ hex で示す。丸ごと載せるとコンテキストを食い潰す。
				w.WriteStartObject();
				w.WriteString("$blob", Convert.ToHexString(blob.AsSpan(0, Math.Min(blob.Length, MaxBlobPreviewBytes))));
				w.WriteNumber("len", blob.Length);
				w.WriteEndObject();
				break;
			case DateTime dt:
				w.WriteStringValue(dt);
				break;
			case DateTimeOffset dto:
				w.WriteStringValue(dto);
				break;
			case Guid g:
				w.WriteStringValue(g);
				break;
			case var other:
				w.WriteStringValue(Convert.ToString(other, CultureInfo.InvariantCulture) ?? "");
				break;
		}
	}

	static void WriteTruncatedString(Utf8JsonWriter w, string s) {
		if (s.Length <= MaxCellChars)
			w.WriteStringValue(s);
		else
			w.WriteStringValue($"{s[..MaxCellChars]}…(切り捨て {s.Length - MaxCellChars} 文字)");
	}

	static void WriteTruncatedString(Utf8JsonWriter w, string propertyName, string s) {
		w.WritePropertyName(propertyName);
		WriteTruncatedString(w, s);
	}

	/// <summary>スキーマ情報のように列数が少ない結果は名前付きオブジェクトで出す。</summary>
	static void WriteObjects(Utf8JsonWriter w, string propertyName, (string[] Columns, List<object?[]> Rows) table) {
		w.WriteStartArray(propertyName);
		foreach (var row in table.Rows) {
			w.WriteStartObject();
			for (int i = 0; i < table.Columns.Length; i++) {
				w.WritePropertyName(table.Columns[i]);
				WriteObjectValue(w, row[i]);
			}
			w.WriteEndObject();
		}
		w.WriteEndArray();
	}

	static void WriteObjectValue(Utf8JsonWriter w, object? v) {
		switch (v) {
			case null or DBNull:
				w.WriteNullValue();
				break;
			case long l:
				w.WriteNumberValue(l);
				break;
			case double d when double.IsFinite(d):
				w.WriteNumberValue(d);
				break;
			case string s:
				WriteTruncatedString(w, s);
				break;
			case byte[] blob:
				w.WriteStringValue(Convert.ToHexString(blob.AsSpan(0, Math.Min(blob.Length, MaxBlobPreviewBytes))));
				break;
			default:
				w.WriteStringValue(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");
				break;
		}
	}

	#endregion

	#region パラメータ変換 =====================================================

	static object?[]? ToDbValues(JsonElement[]? parameters) {
		if (parameters is null || parameters.Length == 0)
			return null;
		var values = new object?[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
			values[i] = ToDbValue(parameters[i], i);
		return values;
	}

	/// <summary>
	/// ★SQLite は動的型で比較演算子が型に敏感 (WHERE Id = '5' は整数 5 に一致しない)。
	///   そのため全部を文字列にせず JSON の型を保って渡す。
	/// </summary>
	static object? ToDbValue(JsonElement e, int index) => e.ValueKind switch {
		JsonValueKind.Null or JsonValueKind.Undefined => null,
		JsonValueKind.True => 1L,
		JsonValueKind.False => 0L,
		JsonValueKind.String => e.GetString(),
		JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
		_ => throw new McpException($"parameters[{index}] に配列・オブジェクトは指定できません (数値・文字列・真偽値・null のみ)。")
	};

	#endregion
}
