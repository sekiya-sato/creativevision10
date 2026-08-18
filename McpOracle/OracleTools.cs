using ModelContextProtocol;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Buffers;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace McpOracle;

/// <summary>Oracle 用 MCP ツール。単一接続を直列化して利用する。</summary>
static class OracleTools {
	public const int DefaultMaxRows = 100;
	public const int MaxRowsHardLimit = 1000;
	const int MaxCellChars = 4000;
	const int MaxResponseBytes = 100_000;
	const int MaxBlobPreviewBytes = 64;
	static OracleConnection _connection = null!;
	static bool _allowWrite;
	static readonly SemaphoreSlim _gate = new(1, 1);

	public static void Initialize(OracleConnection connection, bool allowWrite) { _connection = connection; _allowWrite = allowWrite; }

	public static Task<string> ListTablesAsync(
		[Description("テーブル名の前方一致フィルタ。省略時は全件。")] string? namePrefix = null,
		[Description("各テーブルの行数を count(*) で取得するか。既定は false。")] bool includeRowCounts = false,
		CancellationToken cancellationToken = default) => RunAsync((conn, ct) => {
		var table = ReadTable(conn, "SELECT object_name, object_type FROM user_objects WHERE object_type IN ('TABLE', 'VIEW') AND (:p0 IS NULL OR object_name LIKE UPPER(:p0) || '%') ORDER BY object_type, object_name", [namePrefix], ct);
		var counts = includeRowCounts ? GetRowCounts(conn, table.Rows.Select(x => x[0]?.ToString() ?? "").Where(x => x.Length > 0), ct) : null;
		return WriteJson(w => {
			w.WriteStartObject(); w.WriteStartArray("tables");
			foreach (var row in table.Rows) {
				var name = row[0]?.ToString() ?? "";
				w.WriteStartObject(); w.WriteString("name", name); w.WriteString("type", row[1]?.ToString());
				if (counts?.TryGetValue(name, out var count) == true) w.WriteNumber("row_count", count);
				w.WriteEndObject();
			}
			w.WriteEndArray(); w.WriteNumber("count", table.Rows.Count); w.WriteBoolean("row_counts_included", includeRowCounts); w.WriteEndObject();
		});
	}, cancellationToken);

	public static Task<string> DescribeTableAsync(
		[Description("対象のテーブル名またはビュー名。")] string table,
		CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(table)) throw new McpException("テーブル名を指定してください。");
		return RunAsync((conn, ct) => {
			var objectInfo = ReadTable(conn, "SELECT object_name, object_type FROM user_objects WHERE object_name = UPPER(:p0) AND object_type IN ('TABLE', 'VIEW')", [table], ct);
			if (objectInfo.Rows.Count == 0) throw new McpException($"テーブル '{table}' は存在しません。list_tables で一覧を確認してください。");
			var name = objectInfo.Rows[0][0]?.ToString() ?? table;
			var columns = ReadTable(conn, "SELECT column_id, column_name, data_type, data_length, data_precision, data_scale, nullable, data_default FROM user_tab_columns WHERE table_name = :p0 ORDER BY column_id", [name], ct);
			var constraints = ReadTable(conn, "SELECT c.constraint_name, c.constraint_type, cc.column_name, c.r_constraint_name, c.delete_rule, c.status FROM user_constraints c LEFT JOIN user_cons_columns cc ON cc.constraint_name = c.constraint_name AND cc.table_name = c.table_name WHERE c.table_name = :p0 ORDER BY c.constraint_name, cc.position", [name], ct);
			string? ddl = null;
			try { ddl = ReadTable(conn, "SELECT DBMS_METADATA.GET_DDL(:p0, :p1) FROM dual", [objectInfo.Rows[0][1]?.ToString(), name], ct).Rows[0][0]?.ToString(); }
			catch (OracleException) { /* DBMS_METADATA 権限がない環境でも列定義は返す。 */ }
			return WriteJson(w => { w.WriteStartObject(); w.WriteString("table", name); w.WriteString("type", objectInfo.Rows[0][1]?.ToString()); WriteObjects(w, "columns", columns); WriteObjects(w, "constraints", constraints); if (ddl is null) w.WriteNull("ddl"); else WriteString(w, "ddl", ddl); w.WriteEndObject(); });
		}, cancellationToken);
	}

	public static Task<string> ListIndexesAsync(
		[Description("対象テーブル名。省略時は全テーブルのインデックスを列挙する。")] string? table = null,
		CancellationToken cancellationToken = default) => RunAsync((conn, ct) => {
		var indexes = ReadTable(conn, "SELECT i.table_name, i.index_name, i.uniqueness, i.index_type, c.column_name, c.column_position, c.descend FROM user_indexes i JOIN user_ind_columns c ON c.index_name = i.index_name WHERE (:p0 IS NULL OR i.table_name = UPPER(:p0)) ORDER BY i.table_name, i.index_name, c.column_position", [table], ct);
		var groups = new List<(string Table, string Name, string Unique, string Type, List<string> Columns)>();
		foreach (var row in indexes.Rows) {
			var tableName = row[0]?.ToString() ?? ""; var indexName = row[1]?.ToString() ?? "";
			if (groups.Count == 0 || groups[^1].Table != tableName || groups[^1].Name != indexName) groups.Add((tableName, indexName, row[2]?.ToString() ?? "", row[3]?.ToString() ?? "", []));
			groups[^1].Columns.Add($"{row[4]} {row[6]}");
		}
		return WriteJson(w => { w.WriteStartObject(); w.WriteStartArray("indexes"); foreach (var index in groups) { w.WriteStartObject(); w.WriteString("table", index.Table); w.WriteString("name", index.Name); w.WriteBoolean("unique", index.Unique == "UNIQUE"); w.WriteString("type", index.Type); w.WriteStartArray("columns"); foreach (var column in index.Columns) w.WriteStringValue(column); w.WriteEndArray(); w.WriteEndObject(); } w.WriteEndArray(); w.WriteNumber("count", groups.Count); w.WriteEndObject(); });
	}, cancellationToken);

	public static Task<string> QueryAsync(
		[Description("実行する読み取り専用 SQL。SELECT または WITH ... SELECT。文は 1 つだけ。")] string sql,
		[Description("SQL 内の :p0, :p1, ... に先頭から順にバインドする値の配列。")] JsonElement[]? parameters = null,
		[Description("返却する最大行数。既定 100、上限 1000。")] int maxRows = DefaultMaxRows,
		CancellationToken cancellationToken = default) {
		EnsureReadOnlySql(sql);
		return RunAsync((conn, ct) => ExecuteQueryJson(conn, sql, ToDbValues(parameters), Math.Clamp(maxRows, 1, MaxRowsHardLimit), ct), cancellationToken);
	}

	public static Task<string> ExplainAsync(
		[Description("実行計画を取得する読み取り専用 SQL。")] string sql,
		[Description("SQL 内の :p0, :p1, ... にバインドする値の配列。")] JsonElement[]? parameters = null,
		CancellationToken cancellationToken = default) {
		EnsureReadOnlySql(sql);
		return RunAsync((conn, ct) => {
			using (var explain = CreateCommand(conn, "EXPLAIN PLAN FOR " + TrimSemicolon(sql), ToDbValues(parameters))) { using var reg = RegisterCancel(explain, ct); explain.ExecuteNonQuery(); }
			return ExecuteQueryJson(conn, "SELECT plan_table_output FROM TABLE(DBMS_XPLAN.DISPLAY())", null, MaxRowsHardLimit, ct);
		}, cancellationToken);
	}

	public static Task<string> ExecuteAsync(
		[Description("実行する更新系 SQL。文は 1 つだけ。")] string sql,
		[Description("SQL 内の :p0, :p1, ... にバインドする値の配列。")] JsonElement[]? parameters = null,
		CancellationToken cancellationToken = default) {
		if (!_allowWrite) throw new McpException("このサーバは読み取り専用で起動しています。書き込みには --allow-write が必要です。");
		if (!SqlGuard.TryValidateWrite(sql, out var error)) throw new McpException($"実行できません: {error}");
		return RunAsync((conn, ct) => { var sw = Stopwatch.StartNew(); using var cmd = CreateCommand(conn, sql, ToDbValues(parameters)); using var reg = RegisterCancel(cmd, ct); var affected = cmd.ExecuteNonQuery(); return WriteJson(w => { w.WriteStartObject(); w.WriteNumber("rows_affected", affected); w.WriteNumber("elapsed_ms", sw.ElapsedMilliseconds); w.WriteEndObject(); }); }, cancellationToken);
	}

	static async Task<string> RunAsync(Func<OracleConnection, CancellationToken, string> work, CancellationToken ct) {
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try { if (_connection.State != ConnectionState.Open) throw new McpException("Oracle 接続が初期化されていません。"); return work(_connection, ct); }
		catch (McpException) { throw; }
		catch (OperationCanceledException) { throw; }
		catch (OracleException ex) { throw new McpException($"Oracle エラー (ORA-{ex.Number}): {ex.Message}"); }
		catch (Exception ex) { Console.Error.WriteLine($"[McpOracle] {ex}"); throw new McpException(ex.Message); }
		finally { _gate.Release(); }
	}

	static void EnsureReadOnlySql(string sql) { if (!SqlGuard.TryValidateReadOnly(sql, out var error)) throw new McpException($"読み取り専用モードでは実行できません: {error}"); }
	static string TrimSemicolon(string sql) => sql.Trim().TrimEnd(';').TrimEnd();

	static (string[] Columns, List<object?[]> Rows) ReadTable(OracleConnection connection, string sql, object?[]? values, CancellationToken ct) {
		using var cmd = CreateCommand(connection, sql, values); using var reg = RegisterCancel(cmd, ct); using var reader = cmd.ExecuteReader();
		var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray(); var rows = new List<object?[]>();
		while (reader.Read()) { ct.ThrowIfCancellationRequested(); var row = new object?[reader.FieldCount]; reader.GetValues(row); rows.Add(row); }
		return (columns, rows);
	}

	static OracleCommand CreateCommand(OracleConnection connection, string sql, object?[]? values) {
		var command = connection.CreateCommand(); command.BindByName = true; command.CommandText = sql;
		if (values != null) for (var i = 0; i < values.Length; i++) command.Parameters.Add(new OracleParameter($"p{i}", values[i] ?? DBNull.Value));
		return command;
	}

	static CancellationTokenRegistration RegisterCancel(OracleCommand command, CancellationToken ct) => ct.Register(static state => ((OracleCommand)state!).Cancel(), command);
	static Dictionary<string, long> GetRowCounts(OracleConnection connection, IEnumerable<string> names, CancellationToken ct) { var result = new Dictionary<string, long>(StringComparer.Ordinal); foreach (var name in names) { ct.ThrowIfCancellationRequested(); using var command = CreateCommand(connection, $"SELECT COUNT(*) FROM {QuoteIdent(name)}", null); result[name] = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); } return result; }
	static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

	static string ExecuteQueryJson(OracleConnection connection, string sql, object?[]? values, int maxRows, CancellationToken ct) {
		var sw = Stopwatch.StartNew(); using var cmd = CreateCommand(connection, sql, values); using var reg = RegisterCancel(cmd, ct); using var reader = cmd.ExecuteReader();
		return WriteJson(w => { w.WriteStartObject(); w.WriteStartArray("columns"); for (var i = 0; i < reader.FieldCount; i++) w.WriteStringValue(reader.GetName(i)); w.WriteEndArray(); w.WriteStartArray("rows"); var rows = 0; var truncated = false; string? reason = null; while (reader.Read()) { ct.ThrowIfCancellationRequested(); if (rows >= maxRows) { truncated = true; reason = "行数上限"; break; } if (w.BytesCommitted + w.BytesPending >= MaxResponseBytes) { truncated = true; reason = "応答サイズ上限"; break; } w.WriteStartArray(); for (var i = 0; i < reader.FieldCount; i++) WriteValue(w, reader.IsDBNull(i) ? null : reader.GetValue(i)); w.WriteEndArray(); rows++; } w.WriteEndArray(); w.WriteNumber("row_count", rows); w.WriteBoolean("truncated", truncated); if (reason != null) w.WriteString("truncated_reason", reason); w.WriteNumber("elapsed_ms", sw.ElapsedMilliseconds); w.WriteEndObject(); });
	}

	static string WriteJson(Action<Utf8JsonWriter> write) { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); write(writer); writer.Flush(); return Encoding.UTF8.GetString(buffer.WrittenSpan); }
	static void WriteObjects(Utf8JsonWriter writer, string name, (string[] Columns, List<object?[]> Rows) table) { writer.WriteStartArray(name); foreach (var row in table.Rows) { writer.WriteStartObject(); for (var i = 0; i < table.Columns.Length; i++) { writer.WritePropertyName(table.Columns[i]); WriteValue(writer, row[i]); } writer.WriteEndObject(); } writer.WriteEndArray(); }
	static void WriteString(Utf8JsonWriter writer, string name, string value) { writer.WritePropertyName(name); WriteValue(writer, value); }
	static void WriteValue(Utf8JsonWriter writer, object? value) {
		switch (value) {
			case null or DBNull: writer.WriteNullValue(); break;
			case OracleClob clob: WriteValue(writer, clob.Value); break;
			case OracleBlob blob: writer.WriteStartObject(); writer.WriteString("$blob", Convert.ToHexString(blob.Value.AsSpan(0, Math.Min(blob.Value.Length, MaxBlobPreviewBytes)))); writer.WriteNumber("len", blob.Length); writer.WriteEndObject(); break;
			case OracleDecimal number: WriteValue(writer, number.Value); break;
			case byte[] bytes: writer.WriteStartObject(); writer.WriteString("$blob", Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, MaxBlobPreviewBytes)))); writer.WriteNumber("len", bytes.Length); writer.WriteEndObject(); break;
			case decimal number: writer.WriteNumberValue(number); break;
			case int number: writer.WriteNumberValue(number); break;
			case long number: writer.WriteNumberValue(number); break;
			case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
			case float number when float.IsFinite(number): writer.WriteNumberValue(number); break;
			case bool boolean: writer.WriteBooleanValue(boolean); break;
			case DateTime dateTime: writer.WriteStringValue(dateTime); break;
			case DateTimeOffset dateTimeOffset: writer.WriteStringValue(dateTimeOffset); break;
			case string text: writer.WriteStringValue(text.Length <= MaxCellChars ? text : $"{text[..MaxCellChars]}…(切り捨て {text.Length - MaxCellChars} 文字)"); break;
			default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
		}
	}

	static object?[]? ToDbValues(JsonElement[]? parameters) {
		if (parameters is null || parameters.Length == 0) return null;
		var values = new object?[parameters.Length];
		for (var i = 0; i < parameters.Length; i++) {
			var value = parameters[i];
			values[i] = value.ValueKind switch {
				JsonValueKind.Null or JsonValueKind.Undefined => null,
				JsonValueKind.True => 1,
				JsonValueKind.False => 0,
				JsonValueKind.String => value.GetString(),
				JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : value.GetDouble(),
				_ => throw new McpException($"parameters[{i}] に配列・オブジェクトは指定できません。")
			};
		}
		return values;
	}
}
