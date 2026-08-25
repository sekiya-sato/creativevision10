/*
# description
SqlTokenizer は SQLite 方言の SQL を字句列へ分解します。

目的は構文解析ではなく「文字列リテラルとコメントを誤認しないこと」だけです。
`ifnull` を `coalesce` へ置換するとき、文字列リテラル内の `ifnull` を書き換えては
いけないため、その判別に必要な最小限の字句だけを認識します。

不変条件: 返した字句の Text を順に連結すると入力文字列に完全一致します。
この性質により、ルールが1つも適用されなければ出力は入力と1バイトも変わりません。

# example
var tokens = SqlTokenizer.Tokenize("select ifnull(a,'x') from T -- memo");
 */
using System.Text;

namespace CvBase.Sql;

/// <summary>SQLite方言のSQLを字句列へ分解する</summary>
public static class SqlTokenizer {

	/// <summary>複数文字の演算子。長いものから順に照合する。</summary>
	static readonly string[] _multiCharOperators = [
		"->>", "||", "->", "<<", ">>", "<=", ">=", "==", "!=", "<>",
	];

	/// <summary>
	/// SQLを字句列へ分解する。返した字句のTextを連結すると入力に完全一致する。
	/// </summary>
	public static List<SqlToken> Tokenize(string sql) {
		var tokens = new List<SqlToken>();
		if (string.IsNullOrEmpty(sql))
			return tokens;

		var i = 0;
		while (i < sql.Length) {
			var start = i;
			var c = sql[i];

			// 空白
			if (char.IsWhiteSpace(c)) {
				while (i < sql.Length && char.IsWhiteSpace(sql[i]))
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.Whitespace, sql[start..i], start));
				continue;
			}
			// 行コメント (改行は次の空白字句へ渡す)
			if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') {
				i += 2;
				while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.LineComment, sql[start..i], start));
				continue;
			}
			// ブロックコメント (未終端は末尾まで)
			if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') {
				i += 2;
				while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
					i++;
				i = (i + 1 < sql.Length) ? i + 2 : sql.Length;
				tokens.Add(new SqlToken(SqlTokenKind.BlockComment, sql[start..i], start));
				continue;
			}
			// 文字列リテラル ('' がエスケープ)
			if (c == '\'') {
				i = ScanQuoted(sql, i, '\'', hasDoubledEscape: true);
				tokens.Add(new SqlToken(SqlTokenKind.StringLiteral, sql[start..i], start));
				continue;
			}
			// 引用識別子
			if (c == '"' || c == '`') {
				i = ScanQuoted(sql, i, c, hasDoubledEscape: true);
				tokens.Add(new SqlToken(SqlTokenKind.QuotedIdent, sql[start..i], start));
				continue;
			}
			if (c == '[') {
				i++;
				while (i < sql.Length && sql[i] != ']')
					i++;
				if (i < sql.Length)
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.QuotedIdent, sql[start..i], start));
				continue;
			}
			// パラメータ
			if (c == '?') {
				i++;
				while (i < sql.Length && char.IsAsciiDigit(sql[i]))
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.Parameter, sql[start..i], start));
				continue;
			}
			if ((c == '@' || c == ':' || c == '$') && i + 1 < sql.Length && IsParameterBodyChar(sql[i + 1])) {
				i++;
				while (i < sql.Length && IsParameterBodyChar(sql[i]))
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.Parameter, sql[start..i], start));
				continue;
			}
			// 数値
			if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < sql.Length && char.IsAsciiDigit(sql[i + 1]))) {
				i = ScanNumber(sql, i);
				tokens.Add(new SqlToken(SqlTokenKind.Number, sql[start..i], start));
				continue;
			}
			// 語
			if (IsWordStartChar(c)) {
				while (i < sql.Length && IsWordBodyChar(sql[i]))
					i++;
				tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..i], start));
				continue;
			}
			// 複数文字演算子
			var matched = false;
			foreach (var op in _multiCharOperators) {
				if (i + op.Length <= sql.Length && string.CompareOrdinal(sql, i, op, 0, op.Length) == 0) {
					i += op.Length;
					tokens.Add(new SqlToken(SqlTokenKind.Operator, op, start));
					matched = true;
					break;
				}
			}
			if (matched)
				continue;

			// それ以外は1文字の記号として扱う (必ず1文字進むので無限ループしない)
			i++;
			tokens.Add(new SqlToken(SqlTokenKind.Operator, sql[start..i], start));
		}
		return tokens;
	}

	/// <summary>字句列を文字列へ戻す。</summary>
	public static string Render(IReadOnlyList<SqlToken> tokens) {
		var sb = new StringBuilder();
		foreach (var token in tokens)
			sb.Append(token.Text);
		return sb.ToString();
	}

	/// <summary>開始引用符から終了引用符までを読み飛ばす。未終端は末尾まで。</summary>
	static int ScanQuoted(string sql, int index, char quote, bool hasDoubledEscape) {
		var i = index + 1;
		while (i < sql.Length) {
			if (sql[i] != quote) {
				i++;
				continue;
			}
			if (hasDoubledEscape && i + 1 < sql.Length && sql[i + 1] == quote) {
				i += 2;
				continue;
			}
			return i + 1;
		}
		return sql.Length;
	}

	static int ScanNumber(string sql, int index) {
		var i = index;
		if (sql[i] == '0' && i + 1 < sql.Length && (sql[i + 1] == 'x' || sql[i + 1] == 'X')) {
			i += 2;
			while (i < sql.Length && char.IsAsciiHexDigit(sql[i]))
				i++;
			return i;
		}
		while (i < sql.Length && (char.IsAsciiDigit(sql[i]) || sql[i] == '.'))
			i++;
		if (i < sql.Length && (sql[i] == 'e' || sql[i] == 'E')) {
			var mark = i;
			i++;
			if (i < sql.Length && (sql[i] == '+' || sql[i] == '-'))
				i++;
			if (i < sql.Length && char.IsAsciiDigit(sql[i])) {
				while (i < sql.Length && char.IsAsciiDigit(sql[i]))
					i++;
			}
			else {
				i = mark; // 指数部が数値でなければ数値リテラルに含めない
			}
		}
		return i;
	}

	// 識別子には日本語を含み得るため char.IsLetter で判定する (テーブル・列名はASCIIだが安全側に倒す)
	static bool IsWordStartChar(char c) => char.IsLetter(c) || c == '_';

	static bool IsWordBodyChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

	static bool IsParameterBodyChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
