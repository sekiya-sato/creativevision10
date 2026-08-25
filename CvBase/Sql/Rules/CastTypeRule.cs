/*
# description
CastTypeRule は `CAST(x AS <型>)` の型名を対象DBの型名へ置き換えます（ルール A02）。

SQLite の型名はストレージクラス名（TEXT / REAL / INTEGER）で、MariaDB の CAST では
使えません。PostgreSQL は TEXT / REAL / INTEGER をそのまま解釈するため、
このルールは MariaDB 向けです。

実測ではクライアントSQLに `AS REAL` 65箇所、`AS TEXT` 32箇所、`AS INTEGER` 5箇所あります。
除算やROUNDの結果型に関わるため、`AS REAL` → `DOUBLE` は数値の一致確認が必要です
（差分テストの対象）。

置き換えるのは CAST の括弧内で深さ0にある `AS` の直後の型名1語だけです。
型名が写像表に無ければ何もしません。

# example
cast(a as text)     ->  cast(a as CHAR)
cast(a/b as real)   ->  cast(a/b as DOUBLE)
 */
namespace CvBase.Sql.Rules;

/// <summary>A02: CAST の型名を写像する</summary>
public sealed class CastTypeRule : ISqlRewriteRule {

	readonly Dictionary<string, string> _typeMap;

	/// <param name="typeMap">SQLiteの型名 → 対象DBの型名</param>
	public CastTypeRule(IDictionary<string, string> typeMap) {
		_typeMap = new Dictionary<string, string>(typeMap, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>MariaDB向けの型写像。</summary>
	public static CastTypeRule ForMaria() => new(new Dictionary<string, string> {
		["TEXT"] = "CHAR",
		["REAL"] = "DOUBLE",
		["INTEGER"] = "SIGNED",
		["INT"] = "SIGNED",
		["NUMERIC"] = "DECIMAL",
		["BLOB"] = "BINARY",
	});

	public string Id => "A02-CastType";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "cast", out var open, out var close))
			return false;

		// 括弧内で深さ0にある AS を探す。入れ子のCASTやCASTを含む式には手を出さない
		var depth = 0;
		for (var i = open + 1; i < close; i++) {
			var token = context.Tokens[i];
			if (token.IsOperator("(")) {
				depth++;
				continue;
			}
			if (token.IsOperator(")")) {
				depth--;
				continue;
			}
			if (depth != 0 || !token.IsWord("as"))
				continue;

			var typeIndex = context.NextCode(i);
			if (typeIndex < 0 || typeIndex >= close)
				return false;
			var typeToken = context.Tokens[typeIndex];
			if (typeToken.Kind != SqlTokenKind.Word || !_typeMap.TryGetValue(typeToken.Text, out var mapped))
				return false;
			// 型名の後ろが閉じ括弧でなければ `AS DECIMAL(10,2)` のような形なので触らない
			if (context.NextCode(typeIndex) != close)
				return false;
			context.Replace(typeIndex, mapped);
			return true;
		}
		return false;
	}
}
