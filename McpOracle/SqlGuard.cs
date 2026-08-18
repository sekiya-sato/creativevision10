namespace McpOracle;

/// <summary>単文かつ読み取り専用 SQL かを判定する軽量な字句検証器。</summary>
static class SqlGuard {
	static readonly HashSet<string> WriteHeads = new(StringComparer.Ordinal) {
		"INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "COMMIT", "ROLLBACK", "DECLARE", "BEGIN"
	};

	public static bool TryValidateReadOnly(string sql, out string error) {
		if (!TryFirstWords(sql, out var words, out error)) return false;
		if (words[0] == "SELECT") { error = ""; return true; }
		if (words[0] == "WITH") {
			var head = words.FirstOrDefault(WriteHeads.Contains);
			if (head is null || head == "SELECT") { error = ""; return true; }
			error = $"WITH 句の後続に {head} が含まれています。";
			return false;
		}
		error = $"{words[0]} は読み取り専用モードでは実行できません。SELECT または WITH ... SELECT のみ実行できます。";
		return false;
	}

	public static bool TryValidateWrite(string sql, out string error) {
		if (!TryFirstWords(sql, out var words, out error)) return false;
		if (words[0] is "GRANT" or "REVOKE" || (words[0] == "ALTER" && words.Contains("SYSTEM")) || (words[0] == "CREATE" && words.Contains("USER"))) {
			error = $"{words[0]} は MCP サーバから実行できません。";
			return false;
		}
		error = "";
		return true;
	}

	static bool TryFirstWords(string sql, out List<string> words, out string error) {
		words = [];
		error = "";
		var i = 0;
		var depth = 0;
		var semicolon = false;
		while (i < sql.Length) {
			if (char.IsWhiteSpace(sql[i])) { i++; continue; }
			if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { i = SkipTo(sql, i + 2, "\n"); continue; }
			if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*') {
				var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
				if (end < 0) { error = "ブロックコメントが閉じられていません。"; return false; }
				i = end + 2; continue;
			}
			if (sql[i] == '\'') { if (!SkipQuoted(sql, ref i, '\'')) { error = "文字列リテラルが閉じられていません。"; return false; } continue; }
			if (sql[i] == '"') { if (!SkipQuoted(sql, ref i, '"')) { error = "引用符付き識別子が閉じられていません。"; return false; } continue; }
			if (sql[i] == ';') { semicolon = true; i++; continue; }
			if (semicolon) { error = "複数の SQL 文は実行できません。"; return false; }
			if (char.IsLetter(sql[i]) || sql[i] == '_') {
				var start = i++;
				while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$' or '#')) i++;
				if (depth == 0) words.Add(sql[start..i].ToUpperInvariant());
				continue;
			}
			if (sql[i] == '(') depth++;
			else if (sql[i] == ')' && --depth < 0) { error = "括弧の対応が取れていません。"; return false; }
			i++;
		}
		if (depth != 0) { error = "括弧の対応が取れていません。"; return false; }
		if (words.Count == 0) { error = "SQL が空です。"; return false; }
		return true;
	}

	static int SkipTo(string text, int start, string value) { var end = text.IndexOf(value, start, StringComparison.Ordinal); return end < 0 ? text.Length : end + value.Length; }
	static bool SkipQuoted(string text, ref int i, char quote) {
		for (i++; i < text.Length; i++) if (text[i] == quote) { if (i + 1 < text.Length && text[i + 1] == quote) { i++; continue; } i++; return true; }
		return false;
	}
}
