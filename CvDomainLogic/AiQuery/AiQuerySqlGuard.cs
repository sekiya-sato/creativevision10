using CvBase;
using CvBase.Sql;

namespace CvDomainLogic.AiQuery;

/// <summary>
/// AI照会のSQL検査（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §7）。
/// <para>
/// 利用者別の認可ではなく「単一の照会で、実在の表・列に解決できる」ことの確認である。
/// 字句検査は cv-sqlite(McpSql) の <c>SqlGuard</c> を流用する。SQLは接続先によらずSQLite方言で書く。
/// SQLite では名前解決と書込み有無を <c>EXPLAIN</c> の命令列（行を取得しない）で確認する。
/// PostgreSQL / MariaDB では方言変換できることを確かめ、更新語・システム表の参照を字句で拒否してから、
/// 変換後のSQLを接続先の <c>EXPLAIN</c>（実行しない）で名前解決する。実行時も Msg101 で同じ変換が行われる。
/// それ以外の接続先（Oracle 等）は検査自体を失敗させる。
/// </para>
/// </summary>
public sealed class AiQuerySqlGuard(ExDatabase db) {
	/// <summary>SQL文字数の上限（<see cref="SysAiQuery.SqlText"/> の列長）</summary>
	public const int MaxSqlLength = 4000;

	/// <summary>SQLを検査する。結果行は取得しない。</summary>
	public AiQueryValidateResult Validate(string? sql, IReadOnlyList<AiQueryParam>? parameters) {
		var result = new AiQueryValidateResult();
		var errors = result.Errors;
		parameters ??= [];
		sql = (sql ?? string.Empty).Trim();

		if (!IsSupported(db)) {
			errors.Add(UnsupportedMessage);
			return result;
		}
		if (sql.Length == 0) {
			errors.Add("SQLが空です。");
			return result;
		}
		if (sql.Length > MaxSqlLength) {
			errors.Add($"SQLが長すぎます（{sql.Length}文字、上限{MaxSqlLength}文字）。");
			return result;
		}
		// 単文・WITH後続文の判定（複数文はEXPLAINより前に必ず止める。SqliteCommandは全文を実行するため）
		if (!McpSql.SqlGuard.TryValidateReadOnly(sql, out var guardError)) {
			errors.Add(guardError);
			return result;
		}
		McpSql.SqlGuard.TryTokenize(sql, out var tokens, out _);
		if (tokens.Count > 0 && tokens[^1] is { Text: ";" }) {
			tokens.RemoveAt(tokens.Count - 1);
		}
		// McpSql は PRAGMA・VALUES・EXPLAIN前置も許すが、AI照会は SELECT / WITH ... SELECT だけにする
		if (tokens[0].Text is not ("SELECT" or "WITH")) {
			errors.Add("SELECT または WITH ... SELECT の照会SQLだけを実行できます。");
			return result;
		}
		CheckParameters(tokens, parameters, errors);
		if (errors.Count > 0) {
			return result;
		}

		var body = sql.TrimEnd().TrimEnd(';');
		object[] args = [.. parameters.Select(x => (object)x.Value)];
		if (!IsSqlite(db)) {
			ValidateOtherDialect(body, tokens, args, result);
			result.IsValid = errors.Count == 0;
			return result;
		}
		var plan = db.RawExecCmd("EXPLAIN " + body, args);
		if (!string.IsNullOrEmpty(db.RawLastError)) {
			errors.Add($"SQLエラー: {db.RawLastError}");
			return result;
		}
		CheckPlan(plan, result);
		result.IsValid = errors.Count == 0;
		return result;
	}

	/// <summary>AI照会を利用できない接続先のときの応答文</summary>
	public const string UnsupportedMessage = "AI照会はSQLite・PostgreSQL・MariaDB接続のサーバだけで利用できます。";

	public static bool IsSqlite(ExDatabase db) =>
		string.Equals(db.Dialect.Name, SqlDialects.Sqlite.Name, StringComparison.Ordinal);

	static bool IsPostgre(ExDatabase db) =>
		string.Equals(db.Dialect.Name, SqlDialects.Postgre.Name, StringComparison.Ordinal);

	static bool IsMaria(ExDatabase db) =>
		string.Equals(db.Dialect.Name, SqlDialects.Maria.Name, StringComparison.Ordinal);

	/// <summary>AI照会を利用できる接続先（SQLite・PostgreSQL・MariaDB）か</summary>
	public static bool IsSupported(ExDatabase db) => IsSqlite(db) || IsPostgre(db) || IsMaria(db);

	/// <summary>利用者・LLM向けの接続先DB名</summary>
	public static string DisplayName(ExDatabase db) =>
		IsSqlite(db) ? "SQLite" : IsPostgre(db) ? "PostgreSQL" : IsMaria(db) ? "MariaDB" : db.Dialect.Name;

	/// <summary>
	/// SELECT 文の中に現れると更新・出力・ロックになる語。
	/// PostgreSQL の WITH x AS (DELETE ... RETURNING) 、SELECT INTO、MariaDB の INTO OUTFILE、FOR UPDATE / LOCK IN SHARE MODE など。
	/// </summary>
	static readonly HashSet<string> _writeWords = new(StringComparer.Ordinal) {
		"INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "DROP", "ALTER", "CREATE", "GRANT", "REVOKE",
		"COPY", "CALL", "LOCK", "INTO", "OUTFILE", "DUMPFILE",
	};

	/// <summary>接続先のシステムスキーマ（スキーマ修飾として現れたら拒否する）</summary>
	static readonly HashSet<string> _systemSchemas = new(StringComparer.OrdinalIgnoreCase) {
		"INFORMATION_SCHEMA", "PG_CATALOG", "PG_TOAST", "MYSQL", "PERFORMANCE_SCHEMA", "SYS",
	};

	/// <summary>副作用・待機・ファイル参照・設定参照になる関数（呼出し形だけを拒否する）</summary>
	static readonly HashSet<string> _unsafeFunctions = new(StringComparer.Ordinal) {
		"SLEEP", "BENCHMARK", "LOAD_FILE", "GET_LOCK", "RELEASE_LOCK", "RELEASE_ALL_LOCKS", "IS_FREE_LOCK", "IS_USED_LOCK",
		"MASTER_POS_WAIT", "SET_CONFIG", "CURRENT_SETTING", "NEXTVAL", "SETVAL", "CURRVAL", "LASTVAL", "LAST_INSERT_ID",
		"QUERY_TO_XML", "QUERY_TO_XML_AND_XMLSCHEMA", "CURSOR_TO_XML", "TABLE_TO_XML", "SCHEMA_TO_XML", "DATABASE_TO_XML",
		"VERSION", "USER", "CURRENT_USER", "SESSION_USER", "SYSTEM_USER", "DATABASE", "SCHEMA",
	};

	/// <summary>
	/// PostgreSQL / MariaDB の検査。SQLは SQLite 方言のまま受け、実行時と同じ方言変換を通して名前解決する。
	/// <para>
	/// EXPLAIN の結果から書込みを判定できないため、更新語・システム表・副作用関数を字句で拒否する
	/// （cv-sqlite と同じく分かりやすいエラーを返す第一関門であり、実行経路 Msg101 の認可とは別）。
	/// 文字列・コメントの区切りが SQLite と異なる記法（\ エスケープ、# コメント、/*! */ 実行コメント、
	/// バッククォート・角括弧の識別子）は、字句検査と接続先の解釈がずれないよう使わせない。
	/// 参照表は、実在する表名と一致する語から求める（EXPLAIN の表示名は別名になり得るため使わない）。
	/// </para>
	/// </summary>
	void ValidateOtherDialect(string body, List<McpSql.SqlToken> tokens, object[] args, AiQueryValidateResult result) {
		var errors = result.Errors;
		var dbName = DisplayName(db);
		if (body.Contains('\\')) {
			errors.Add("バックスラッシュ（\\）は使えません（接続先DBで文字列の解釈が異なるため）。");
		}
		if (body.Contains("/*", StringComparison.Ordinal)) {
			errors.Add("ブロックコメント（/* */）は使えません。");
		}
		var tableNames = db.GetUserTableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
		// PostgreSQL は表名を小文字で作るため、表示は定義クラスの表名（Tran01Tenuri 等）に揃える
		var canonical = DefineDataTable.TableTypes
			.Select(db.GetTableName)
			.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		var tables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		var system = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < tokens.Count; i++) {
			var token = tokens[i];
			var next = i + 1 < tokens.Count ? tokens[i + 1] : default;
			var previous = i > 0 ? tokens[i - 1] : default;
			if (token is { Kind: McpSql.TokenKind.Punct, Text: "#" }) {
				errors.Add("# は使えません（MariaDBではコメントになるため）。");
				continue;
			}
			string name;
			if (token.Kind == McpSql.TokenKind.QuotedIdent) {
				if (token.Text[0] != '"') {
					errors.Add($"識別子 {token.Text} は使えません。引用符で囲まない名前を使ってください。");
					continue;
				}
				name = token.Text[1..^1].Replace("\"\"", "\"");
			}
			else if (token.Kind == McpSql.TokenKind.Word) {
				name = token.Text;
				// 修飾（h.Kubun 等）の後ろの語は列名なので、更新語・関数の検査をしない
				var qualified = previous is { Kind: McpSql.TokenKind.Punct, Text: "." };
				if (!qualified && (_writeWords.Contains(name) || (name == "SHARE" && previous is { Text: "FOR" or "IN" }))) {
					errors.Add($"{name} を含むSQLは実行できません（更新・出力・ロックを伴うため）。");
					continue;
				}
				if (!qualified && next is { Kind: McpSql.TokenKind.Punct, Text: "(" } && _unsafeFunctions.Contains(name)) {
					errors.Add($"関数 {name} は使えません。");
					continue;
				}
				if (name.StartsWith("PG_", StringComparison.Ordinal) || name.StartsWith("LO_", StringComparison.Ordinal)
					|| name.StartsWith("DBLINK", StringComparison.Ordinal)) {
					errors.Add($"{dbName} のシステム関数・システム表（{name}）は使えません。");
					continue;
				}
			}
			else {
				continue;
			}
			if (_systemSchemas.Contains(name) && next is { Kind: McpSql.TokenKind.Punct, Text: "." }) {
				errors.Add($"{dbName} のシステムスキーマ（{name}）は参照できません。");
				continue;
			}
			if (tableNames.Contains(name) || canonical.ContainsKey(name)) {
				var display = canonical.GetValueOrDefault(name) ?? name;
				(IsSystemTable(display) ? system : tables).Add(display);
			}
		}
		if (system.Count > 0) {
			errors.Add($"システムテーブル（{string.Join(", ", system)}）は参照できません。");
		}
		var findings = db.Dialect.Inspect(body);
		if (findings.Count > 0) {
			errors.Add($"{dbName} へ変換できない構文があります: {string.Join(", ", findings.Select(f => $"{f.Text}（{f.Construct.Note}）").Distinct())}。別の書き方にしてください。");
		}
		if (errors.Count > 0) {
			return;
		}
		// 実行（Msg101 の TranslateClientSql）と同じ変換結果を EXPLAIN する。EXPLAIN（ANALYZE なし）は照会を実行しない
		db.RawExecCmd("EXPLAIN " + db.TranslateDialect(body), args);
		if (!string.IsNullOrEmpty(db.RawLastError)) {
			errors.Add($"SQLエラー（{dbName}）: {db.RawLastError}");
			return;
		}
		result.Tables.AddRange(tables);
	}

	/// <summary>
	/// パラメータは @0,@1,… の番号指定だけを使い、件数を一致させる。
	/// 数値型は文字列でバインドされるため、比較の型ずれを防ぐよう CAST(@n AS …) を必須にする。
	/// </summary>
	static void CheckParameters(List<McpSql.SqlToken> tokens, IReadOnlyList<AiQueryParam> parameters, List<string> errors) {
		var maxIndex = -1;
		for (var i = 0; i < tokens.Count; i++) {
			var text = tokens[i].Text;
			if (tokens[i].Kind != McpSql.TokenKind.Literal || text.Length == 0 || text[0] is not ('@' or ':' or '$' or '?')) {
				continue;
			}
			if (text[0] != '@' || !int.TryParse(text.AsSpan(1), out var index)) {
				errors.Add($"パラメータ {text} は使えません。@0, @1, … の番号で指定してください。");
				continue;
			}
			maxIndex = Math.Max(maxIndex, index);
			if (index < parameters.Count && parameters[index].Type is EnumAiQueryParamType.Integer or EnumAiQueryParamType.Decimal) {
				var casted = i >= 2 && tokens[i - 1].Text == "(" && tokens[i - 2].Text == "CAST"
					&& i + 1 < tokens.Count && tokens[i + 1].Text == "AS";
				if (!casted) {
					errors.Add($"数値パラメータ {text}（{parameters[index].Label}）は CAST({text} AS INTEGER または REAL) で使ってください。");
				}
			}
		}
		if (maxIndex + 1 != parameters.Count) {
			errors.Add($"SQL内のパラメータ数（{maxIndex + 1}）と指定されたパラメータ数（{parameters.Count}）が一致しません。");
		}
	}

	/// <summary>
	/// EXPLAIN の命令列から書込み（OpenWrite）を拒否し、読込み（OpenRead の rootpage）から参照表を求める。
	/// Sys* 表（ログイン情報を含む）と sqlite_schema の参照は拒否する。
	/// </summary>
	void CheckPlan(List<Dictionary<string, object>> plan, AiQueryValidateResult result) {
		var rootPages = db.RawExecCmd("SELECT rootpage, tbl_name FROM sqlite_master WHERE rootpage > 0")
			.GroupBy(r => Convert.ToInt64(r["rootpage"]))
			.ToDictionary(g => g.Key, g => g.First()["tbl_name"]?.ToString() ?? string.Empty);
		var tables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in plan) {
			var opcode = row.TryGetValue("opcode", out var op) ? op?.ToString() : null;
			if (opcode == "OpenWrite") {
				result.Errors.Add("更新を伴うSQLは実行できません。");
				return;
			}
			// p3 はDB番号（0=main）。一時表（ORDER BY 等の作業領域）は OpenEphemeral で別命令になる
			if (opcode != "OpenRead" || Convert.ToInt64(row["p3"]) != 0) {
				continue;
			}
			var rootPage = Convert.ToInt64(row["p2"]);
			if (rootPage == 1) {
				result.Errors.Add("sqlite_schema は参照できません。");
				return;
			}
			if (rootPages.TryGetValue(rootPage, out var table) && table.Length > 0) {
				tables.Add(table);
			}
		}
		var system = tables.Where(IsSystemTable).ToList();
		if (system.Count > 0) {
			result.Errors.Add($"システムテーブル（{string.Join(", ", system)}）は参照できません。");
			return;
		}
		result.Tables.AddRange(tables);
	}

	internal static bool IsSystemTable(string table) =>
		table.StartsWith("Sys", StringComparison.OrdinalIgnoreCase) || table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase);
}
