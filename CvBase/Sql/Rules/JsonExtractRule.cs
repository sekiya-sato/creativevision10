/*
# description
JsonExtractRule は `json_extract(X,'$.Prop')` を対象DBの表現へ置き換えます（ルール B01）。

CV10 のJSONパスは実測で39種類・すべて単一階層 `$.Prop` です。配列添字・入れ子パス・
動的パスは使われていないため、パスの形を限定した写像で足ります。
この形に一致しないパス（`$[0]` や `$.a.b` など）は変換しません。

PostgreSQL は `->>` でテキストを取り出します。数値として使う箇所は呼び出し側が
`cast(... as integer)` で包んでいるため、テキスト返しでも結果は変わりません。
MariaDB は `JSON_VALUE` を使います。`JSON_EXTRACT` は文字列に引用符が付くため使いません。

# example
PostgreSQL: json_extract(m.value,'$.Su')  ->  ((m.value)::jsonb ->> 'Su')
MariaDB   : json_extract(m.value,'$.Su')  ->  JSON_VALUE(m.value,'$.Su')
 */
namespace CvBase.Sql.Rules;

/// <summary>B01: json_extract の写像</summary>
public sealed class JsonExtractRule : ISqlRewriteRule {

	readonly Func<string, string, string> _build;

	/// <param name="build">(対象式, プロパティ名) から置換後の式を作る</param>
	public JsonExtractRule(Func<string, string, string> build) {
		_build = build;
	}

	/// <summary>PostgreSQL向け。jsonb へキャストして ->> でテキストを取り出す。</summary>
	public static JsonExtractRule ForPostgre() =>
		new((target, property) => $"(({target})::jsonb ->> '{property}')");

	/// <summary>MariaDB向け。JSON_VALUE は引用符を外したスカラーを返す。</summary>
	public static JsonExtractRule ForMaria() =>
		new((target, property) => $"JSON_VALUE({target},'$.{property}')");

	public string Id => "B01-JsonExtract";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "json_extract", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count != 2)
			return false;

		var target = context.TextOf(args[0].Start, args[0].End).Trim();
		if (target.Length == 0)
			return false;

		var property = JsonPath.SingleLevelProperty(context, args[1]);
		if (property == null)
			return false;

		context.ReplaceRange(index, close - index + 1, _build(target, property));
		return true;
	}
}

/// <summary>JSONパス引数の解釈</summary>
internal static class JsonPath {

	/// <summary>
	/// 引数が単一階層のJSONパスリテラル <c>'$.Prop'</c> ならプロパティ名を返す。
	/// それ以外（入れ子・添字・式）は null を返し、呼び出し側は変換しない。
	/// </summary>
	internal static string? SingleLevelProperty(SqlRewriteContext context, (int Start, int End) argument) {
		// 引数が文字列リテラル1個だけであること
		var codeIndex = -1;
		for (var i = argument.Start; i <= argument.End; i++) {
			if (!context.Tokens[i].IsCode)
				continue;
			if (codeIndex >= 0)
				return null;
			codeIndex = i;
		}
		if (codeIndex < 0)
			return null;
		var token = context.Tokens[codeIndex];
		if (token.Kind != SqlTokenKind.StringLiteral || token.Text.Length < 5)
			return null;

		var inner = token.Text[1..^1]; // 前後の ' を外す
		if (!inner.StartsWith("$.", StringComparison.Ordinal))
			return null;
		var property = inner[2..];
		if (property.Length == 0)
			return null;
		// 単一階層のみ。識別子として妥当な文字だけを認める
		foreach (var c in property) {
			if (!char.IsLetterOrDigit(c) && c != '_')
				return null;
		}
		return property;
	}
}
