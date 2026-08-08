namespace McpSql;

/// <summary>
/// トークン種別
/// </summary>
enum TokenKind {
	/// <summary>識別子・キーワード (大文字化して保持)</summary>
	Word,
	/// <summary>数値リテラル</summary>
	Number,
	/// <summary>文字列/BLOBリテラル・バインドパラメータ</summary>
	Literal,
	/// <summary>引用符付き識別子 ("x" / `x` / [x])</summary>
	QuotedIdent,
	/// <summary>記号</summary>
	Punct
}

/// <summary>
/// SQL トークン。Depth は括弧のネスト段数 (最上位が 0)。
/// </summary>
readonly record struct SqlToken(TokenKind Kind, string Text, int Depth);

/// <summary>
/// SQL 文が読み取り専用かどうかを判定する。
///
/// ★このクラスは SQLite のパーサではなく単なるレキサである。
///   したがってこれは「セキュリティ境界」ではなく「分かりやすいエラーを返すための第一関門」に過ぎない。
///   実際の書き込み禁止は接続側の Mode=ReadOnly と PRAGMA query_only=ON が担保する (Program.cs 参照)。
///   既知の限界:
///     - 将来 SQLite に書き込み可能な新構文が追加された場合、先頭キーワード判定を素通りしうる。
///     - クエリの実行コストは判定しない (SELECT * FROM a,b,c は読み取り専用だが暴走する)。
///       これは行数・文字数上限と SqliteCommand.Cancel() で対処する。
/// </summary>
static class SqlGuard {

	/// <summary>CTE の後続文として現れうる先頭キーワード</summary>
	static readonly HashSet<string> _statementHeads = new(StringComparer.Ordinal) {
		"SELECT", "VALUES", "INSERT", "UPDATE", "DELETE", "REPLACE"
	};

	/// <summary>
	/// 実行を許可する PRAGMA 名 (小文字)。
	/// ★「= が無ければ読み取り」は誤り。PRAGMA optimize / wal_checkpoint(...) / incremental_vacuum(...)
	///   はいずれも = 無しで書き込むため、許可リスト方式にしている。必要になったら都度追加すること。
	/// </summary>
	static readonly HashSet<string> _readOnlyPragmas = new(StringComparer.OrdinalIgnoreCase) {
		"table_info", "table_xinfo", "table_list",
		"index_list", "index_info", "index_xinfo",
		"foreign_key_list", "foreign_key_check",
		"database_list", "collation_list", "function_list", "module_list",
		"pragma_list", "compile_options",
		"freelist_count", "page_count", "page_size", "encoding", "journal_mode",
		"user_version", "application_id", "schema_version", "data_version",
		"integrity_check", "quick_check"
	};

	/// <summary>
	/// 読み取り専用として実行してよい SQL か判定する。
	/// 許可: SELECT / VALUES / WITH ... SELECT / 読み取り系 PRAGMA / それらへの EXPLAIN 前置
	/// </summary>
	public static bool TryValidateReadOnly(string sql, out string error) {
		if (!TryPrepare(sql, out var tokens, out error))
			return false;

		// EXPLAIN / EXPLAIN QUERY PLAN の前置を剥がす
		var start = 0;
		if (IsWord(tokens, 0, "EXPLAIN")) {
			start = 1;
			if (IsWord(tokens, 1, "QUERY") && IsWord(tokens, 2, "PLAN"))
				start = 3;
		}
		if (start >= tokens.Count) {
			error = "EXPLAIN の対象となる SQL がありません。";
			return false;
		}

		var head = tokens[start];
		if (head.Kind != TokenKind.Word) {
			error = $"SQL が文として解釈できません (先頭: {head.Text})。";
			return false;
		}
		switch (head.Text) {
			case "SELECT":
			case "VALUES":
				error = "";
				return true;
			case "WITH":
				return TryValidateCte(tokens, start + 1, out error);
			case "PRAGMA":
				return TryValidatePragma(tokens, start + 1, out error);
			default:
				error = $"{head.Text} は読み取り専用モードでは実行できません。SELECT / VALUES / WITH ... SELECT / 読み取り系 PRAGMA のみ実行できます。";
				return false;
		}
	}

	/// <summary>
	/// --allow-write 指定時の更新系 SQL の検証。
	/// 単文であることに加え、ATTACH/DETACH (ファイルシステム脱出) と PRAGMA (journal_mode 等の変更) を拒否する。
	/// </summary>
	public static bool TryValidateWrite(string sql, out string error) {
		if (!TryPrepare(sql, out var tokens, out error))
			return false;

		var head = tokens[0];
		if (head.Kind != TokenKind.Word) {
			error = $"SQL が文として解釈できません (先頭: {head.Text})。";
			return false;
		}
		switch (head.Text) {
			case "ATTACH":
			case "DETACH":
				error = $"{head.Text} は許可されていません。この MCP サーバは起動時に指定された 1 つの DB ファイルのみを対象とします。";
				return false;
			case "PRAGMA":
				error = "PRAGMA は execute では実行できません。journal_mode 等の変更を防ぐためです。";
				return false;
			default:
				error = "";
				return true;
		}
	}

	/// <summary>
	/// トークナイズと単文チェックをまとめて行う。成功時 tokens は末尾のセミコロンを除いた 1 文。
	/// </summary>
	static bool TryPrepare(string sql, out List<SqlToken> tokens, out string error) {
		if (!TryTokenize(sql, out tokens, out error))
			return false;

		// ★単文強制は必須。SqliteCommand は CommandText 中の全ての文を順に実行するため、
		//   "select 1; delete from t" を通すと DELETE が走ってしまう。
		var semi = tokens.FindIndex(t => t.Kind == TokenKind.Punct && t.Text == ";");
		if (semi >= 0) {
			if (semi != tokens.Count - 1) {
				error = "複数の SQL 文は実行できません (指定できるのは 1 文と末尾の ; のみ)。";
				return false;
			}
			tokens.RemoveAt(semi);
		}
		if (tokens.Count == 0) {
			error = "SQL が空です。";
			return false;
		}
		error = "";
		return true;
	}

	/// <summary>
	/// WITH 句の後続文を判定する。
	/// SQLite 3.46 は WITH x AS (...) DELETE FROM t を許すため、後続文の先頭キーワードを必ず確認する。
	/// CTE 本体は必ず括弧の中 (Depth >= 1) にあるので、Depth == 0 の Word だけを見ればよい。
	/// </summary>
	static bool TryValidateCte(List<SqlToken> tokens, int from, out string error) {
		for (int i = from; i < tokens.Count; i++) {
			var t = tokens[i];
			if (t.Kind != TokenKind.Word || t.Depth != 0)
				continue;
			if (!_statementHeads.Contains(t.Text))
				continue;
			if (t.Text is "SELECT" or "VALUES") {
				error = "";
				return true;
			}
			error = $"CTE の後続文が {t.Text} です。読み取り専用モードでは実行できません。";
			return false;
		}
		error = "WITH 句の後続文を判定できませんでした。";
		return false;
	}

	/// <summary>
	/// PRAGMA が読み取り専用か判定する。
	/// </summary>
	static bool TryValidatePragma(List<SqlToken> tokens, int from, out string error) {
		var i = from;
		if (i >= tokens.Count || tokens[i].Kind is not (TokenKind.Word or TokenKind.QuotedIdent)) {
			error = "PRAGMA 名がありません。";
			return false;
		}
		// schema. 修飾があれば読み飛ばす (main.page_count など)
		if (i + 2 < tokens.Count && tokens[i + 1] is { Kind: TokenKind.Punct, Text: "." })
			i += 2;

		var name = tokens[i].Text;
		if (i + 1 < tokens.Count && tokens[i + 1] is { Kind: TokenKind.Punct, Text: "=" }) {
			error = $"PRAGMA {name} への代入は書き込み操作です。";
			return false;
		}
		if (!_readOnlyPragmas.Contains(name)) {
			error = $"PRAGMA {name} は許可されていません。許可されているのは {string.Join(", ", _readOnlyPragmas.Order(StringComparer.Ordinal))} です。";
			return false;
		}
		error = "";
		return true;
	}

	static bool IsWord(List<SqlToken> tokens, int index, string word)
		=> index < tokens.Count && tokens[index].Kind == TokenKind.Word && tokens[index].Text == word;

	/// <summary>
	/// SQL をトークン列に分解する。
	/// コメントと文字列リテラルをここで消費するため、
	/// "-- c\n/* c */ SELECT 'DELETE FROM x'" のようなケースを正しく扱える。
	/// </summary>
	public static bool TryTokenize(string sql, out List<SqlToken> tokens, out string error) {
		tokens = [];
		error = "";
		var depth = 0;
		var i = 0;
		var len = sql.Length;

		while (i < len) {
			var c = sql[i];

			if (char.IsWhiteSpace(c)) {
				i++;
				continue;
			}
			// 行コメント
			if (c == '-' && i + 1 < len && sql[i + 1] == '-') {
				i += 2;
				while (i < len && sql[i] != '\n')
					i++;
				continue;
			}
			// ブロックコメント
			if (c == '/' && i + 1 < len && sql[i + 1] == '*') {
				var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
				if (end < 0) {
					error = "ブロックコメント (/* ... */) が閉じられていません。";
					return false;
				}
				i = end + 2;
				continue;
			}
			// BLOB リテラル x'...'
			if ((c is 'x' or 'X') && i + 1 < len && sql[i + 1] == '\'') {
				if (!TryReadDelimited(sql, i + 1, '\'', out var blobEnd)) {
					error = "BLOB リテラル (x'...') が閉じられていません。";
					return false;
				}
				tokens.Add(new SqlToken(TokenKind.Literal, sql[i..blobEnd], depth));
				i = blobEnd;
				continue;
			}
			// 文字列リテラル
			if (c == '\'') {
				if (!TryReadDelimited(sql, i, '\'', out var strEnd)) {
					error = "文字列リテラル (' ... ') が閉じられていません。";
					return false;
				}
				tokens.Add(new SqlToken(TokenKind.Literal, sql[i..strEnd], depth));
				i = strEnd;
				continue;
			}
			// 引用符付き識別子
			if (c is '"' or '`') {
				if (!TryReadDelimited(sql, i, c, out var identEnd)) {
					error = $"引用符付き識別子 ({c} ... {c}) が閉じられていません。";
					return false;
				}
				tokens.Add(new SqlToken(TokenKind.QuotedIdent, sql[i..identEnd], depth));
				i = identEnd;
				continue;
			}
			if (c == '[') {
				var close = sql.IndexOf(']', i + 1);
				if (close < 0) {
					error = "引用符付き識別子 ([ ... ]) が閉じられていません。";
					return false;
				}
				tokens.Add(new SqlToken(TokenKind.QuotedIdent, sql[i..(close + 1)], depth));
				i = close + 1;
				continue;
			}
			// バインドパラメータ (@x, :x, $x, ?, ?1)
			// ★ Literal として扱う。Word にすると @delete のような名前を更新文と誤判定してしまう。
			if (c is '@' or ':' or '$' or '?') {
				var j = i + 1;
				while (j < len && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
					j++;
				tokens.Add(new SqlToken(TokenKind.Literal, sql[i..j], depth));
				i = j;
				continue;
			}
			// 識別子・キーワード
			if (char.IsLetter(c) || c == '_') {
				var j = i;
				while (j < len && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_' || sql[j] == '$'))
					j++;
				tokens.Add(new SqlToken(TokenKind.Word, sql[i..j].ToUpperInvariant(), depth));
				i = j;
				continue;
			}
			// 数値
			if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < len && char.IsAsciiDigit(sql[i + 1]))) {
				var j = i;
				// 16進 (0x...)
				if (c == '0' && i + 1 < len && sql[i + 1] is 'x' or 'X') {
					j = i + 2;
					while (j < len && char.IsAsciiHexDigit(sql[j]))
						j++;
				}
				else {
					while (j < len && (char.IsAsciiDigit(sql[j]) || sql[j] == '.'))
						j++;
					// 指数部
					if (j < len && sql[j] is 'e' or 'E') {
						var k = j + 1;
						if (k < len && sql[k] is '+' or '-')
							k++;
						if (k < len && char.IsAsciiDigit(sql[k])) {
							j = k;
							while (j < len && char.IsAsciiDigit(sql[j]))
								j++;
						}
					}
				}
				tokens.Add(new SqlToken(TokenKind.Number, sql[i..j], depth));
				i = j;
				continue;
			}
			// 括弧
			if (c == '(') {
				tokens.Add(new SqlToken(TokenKind.Punct, "(", depth));
				depth++;
				i++;
				continue;
			}
			if (c == ')') {
				depth--;
				if (depth < 0) {
					error = "括弧の対応が取れていません (閉じ括弧が多すぎます)。";
					return false;
				}
				tokens.Add(new SqlToken(TokenKind.Punct, ")", depth));
				i++;
				continue;
			}
			// その他の記号は 1 文字ずつ
			tokens.Add(new SqlToken(TokenKind.Punct, c.ToString(), depth));
			i++;
		}

		if (depth != 0) {
			error = "括弧の対応が取れていません (閉じ括弧が足りません)。";
			return false;
		}
		if (tokens.Count == 0) {
			error = "SQL が空です。";
			return false;
		}
		return true;
	}

	/// <summary>
	/// start 位置のデリミタで始まる区間を読む。close の 2 個連続はエスケープとして扱う。
	/// 戻り値は成否、end は終端の次の位置。
	/// </summary>
	static bool TryReadDelimited(string sql, int start, char close, out int end) {
		end = start;
		var i = start + 1; // 開始デリミタを飛ばす
		var len = sql.Length;
		while (i < len) {
			if (sql[i] == close) {
				if (i + 1 < len && sql[i + 1] == close) {
					i += 2; // '' / "" / `` はエスケープ
					continue;
				}
				end = i + 1;
				return true;
			}
			i++;
		}
		return false;
	}
}
