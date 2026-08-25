/*
# description
JsonEachRule は FROM句の `json_each(X) alias` を対象DBの行展開へ置き換えます（ルール B02）。

これが「クライアントSQLを書き換えずに他DBへ移す」ための最大の梃子です。
PostgreSQL の `AS alias(value)` と MariaDB の `COLUMNS(value JSON PATH '$')` は
どちらも `alias.value` を提供するため、**呼び出し側SQLを1文字も変えずに済みます**。
実測で49箇所（17ファイル）が対象です。

SQLite の `json_each` は別名を省略できます（そのとき表名は `json_each`、列は `value`）。
MariaDB の `JSON_TABLE` は別名が必須なので、省略時は `json_each` を補います。

別名の判定では、直後の語がSQLのキーワードだった場合は別名とみなしません
（`from T, json_each(x) where ...` を誤って `where` を別名として取り込まないため）。

# example
PostgreSQL: json_each(h.Jmeisai) m  ->  jsonb_array_elements((h.Jmeisai)::jsonb) AS m(value)
MariaDB   : json_each(h.Jmeisai) m  ->  JSON_TABLE(h.Jmeisai,'$[*]' COLUMNS(value JSON PATH '$')) AS m
 */
namespace CvBase.Sql.Rules;

/// <summary>B02: json_each の行展開を写像する</summary>
public sealed class JsonEachRule : ISqlRewriteRule {

	/// <summary>別名とみなさない語。FROM句の後続要素になり得るSQLキーワード。</summary>
	static readonly HashSet<string> _notAlias = new(StringComparer.OrdinalIgnoreCase) {
		"where", "on", "using", "group", "order", "having", "limit", "offset", "union", "except", "intersect",
		"join", "inner", "left", "right", "full", "cross", "natural", "and", "or", "window", "for", "fetch",
		"select", "from", "returning", "set", "values", "as",
	};

	/// <summary>SQLiteで別名を省略したときの既定名</summary>
	const string DefaultAlias = "json_each";

	/// <summary>(対象式, 別名, 行番号列が必要か) から置換後のFROM要素を作る</summary>
	readonly Func<string, string, bool, string> _build;

	public JsonEachRule(Func<string, string, bool, string> build) {
		_build = build;
	}

	/// <summary>PostgreSQL向け。jsonb_array_elements は alias(value) で列名を与える。</summary>
	public static JsonEachRule ForPostgre() =>
		new((target, alias, withOrdinality) => withOrdinality
			? $"jsonb_array_elements(({target})::jsonb) WITH ORDINALITY AS {QuotePostgre(alias)}(value, {SqlRewriteContext.OrdinalityColumn})"
			: $"jsonb_array_elements(({target})::jsonb) AS {QuotePostgre(alias)}(value)");

	/// <summary>MariaDB向け。JSON_TABLE で value 列を作る。別名は必須。</summary>
	public static JsonEachRule ForMaria() =>
		new((target, alias, withOrdinality) => withOrdinality
			? $"JSON_TABLE({target},'$[*]' COLUMNS(value JSON PATH '$', {SqlRewriteContext.OrdinalityColumn} FOR ORDINALITY)) AS {QuoteMaria(alias)}"
			: $"JSON_TABLE({target},'$[*]' COLUMNS(value JSON PATH '$')) AS {QuoteMaria(alias)}");

	// 別名を省略したときの既定名 `json_each` をそのまま出すと、生成SQLを再度変換にかけた際に
	// 関数呼び出しとして再照合されてしまう。引用識別子にして語として扱われないようにする。
	static string QuotePostgre(string alias) =>
		alias == DefaultAlias ? $"\"{DefaultAlias}\"" : alias;

	static string QuoteMaria(string alias) =>
		alias == DefaultAlias ? $"`{DefaultAlias}`" : alias;

	public string Id => "B02-JsonEach";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "json_each", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		// json_each(X) のみ対象。json_each(X, path) の2引数形は使われていないため変換しない
		if (args.Count != 1)
			return false;
		var target = context.TextOf(args[0].Start, args[0].End).Trim();
		if (target.Length == 0)
			return false;

		var (alias, endIndex) = ReadAlias(context, close);
		// SQL内で alias.key を参照している場合だけ行番号列を作る
		var withOrdinality = context.OrdinalityAliases.Contains(alias);
		context.ReplaceRange(index, endIndex - index + 1, _build(target, alias, withOrdinality));
		return true;
	}

	/// <summary>
	/// 閉じ括弧の後ろにある別名を読む。<c>AS</c> は省略可。
	/// 別名が無ければ既定名を返し、置換範囲は閉じ括弧までにする。
	/// </summary>
	static (string Alias, int EndIndex) ReadAlias(SqlRewriteContext context, int closeIndex) {
		var next = context.NextCode(closeIndex);
		if (next < 0)
			return (DefaultAlias, closeIndex);

		var endIndex = closeIndex;
		if (context.Tokens[next].IsWord("as")) {
			var afterAs = context.NextCode(next);
			// AS の後ろに語が無ければ形が読めないので触らない範囲に留める
			if (afterAs < 0 || context.Tokens[afterAs].Kind != SqlTokenKind.Word)
				return (DefaultAlias, closeIndex);
			return (context.Tokens[afterAs].Text, afterAs);
		}
		if (context.Tokens[next].Kind != SqlTokenKind.Word)
			return (DefaultAlias, endIndex);
		if (_notAlias.Contains(context.Tokens[next].Text))
			return (DefaultAlias, endIndex);
		return (context.Tokens[next].Text, next);
	}
}
