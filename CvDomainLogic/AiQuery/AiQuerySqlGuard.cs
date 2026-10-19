using CvBase;
using CvBase.Sql;

namespace CvDomainLogic.AiQuery;

/// <summary>
/// AI照会のSQL検査（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §7）。
/// <para>
/// 利用者別の認可ではなく「単一の照会で、実在の表・列に解決できる」ことの確認である。
/// 字句検査は cv-sqlite(McpSql) の <c>SqlGuard</c> を流用し、名前解決と書込み有無は SQLite の
/// <c>EXPLAIN</c>（行を取得しない）で確認する。SQLite 専用の検査であり、他DBでは検査自体を失敗させる。
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

		if (!IsSqlite(db)) {
			errors.Add("AI照会はSQLite接続のサーバだけで利用できます。");
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
		var plan = db.RawExecCmd("EXPLAIN " + body, [.. parameters.Select(x => (object)x.Value)]);
		if (!string.IsNullOrEmpty(db.RawLastError)) {
			errors.Add($"SQLエラー: {db.RawLastError}");
			return result;
		}
		CheckPlan(plan, result);
		result.IsValid = errors.Count == 0;
		return result;
	}

	public static bool IsSqlite(ExDatabase db) =>
		string.Equals(db.Dialect.Name, SqlDialects.Sqlite.Name, StringComparison.Ordinal);

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
