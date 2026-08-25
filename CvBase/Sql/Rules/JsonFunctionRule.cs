/*
# description
JsonFunctionRule は引数1個のJSON関数を対象DBの表現へ置き換えます（ルール B03 / B04）。

対象は次の2つです。
- `json_valid(X)` : 不正JSONガード。PostgreSQL は 16 以降の `IS JSON` 述語を使う。
  MariaDB は同名関数があるため書き換え不要（NativeConstructIds で扱う）。
- `json(X)` : JSONとしての正規化。PostgreSQL は `::jsonb`、MariaDB は `CAST(X AS JSON)`。

`json_valid()` ガードは AGENTS.md の規約で必須なので、変換漏れが起きると不正JSONで
例外になる。そのためこのルールは引数1個の形だけを厳密に照合し、
それ以外は変換せず未対応構文として報告させます。

# example
PostgreSQL: json_valid(h.Jmeisai)  ->  ((h.Jmeisai) IS JSON)
MariaDB   : json(X.value2)         ->  CAST(X.value2 AS JSON)
 */
namespace CvBase.Sql.Rules;

/// <summary>B03 / B04: 引数1個のJSON関数を写像する</summary>
public sealed class JsonFunctionRule : ISqlRewriteRule {

	readonly string _functionName;
	readonly Func<string, string> _build;

	/// <param name="id">ルールID</param>
	/// <param name="functionName">SQLite側の関数名</param>
	/// <param name="build">引数の式から置換後の式を作る</param>
	public JsonFunctionRule(string id, string functionName, Func<string, string> build) {
		Id = id;
		_functionName = functionName;
		_build = build;
	}

	/// <summary>B03 json_valid。PostgreSQL 16 以降の IS JSON 述語へ。</summary>
	public static JsonFunctionRule JsonValidForPostgre() =>
		new("B03-JsonValid", "json_valid", target => $"(({target}) IS JSON)");

	/// <summary>B04 json。PostgreSQL は jsonb へのキャスト。</summary>
	public static JsonFunctionRule JsonCastForPostgre() =>
		new("B04-JsonCast", "json", target => $"(({target})::jsonb)");

	/// <summary>B04 json。MariaDB は CAST(x AS JSON)。</summary>
	public static JsonFunctionRule JsonCastForMaria() =>
		new("B04-JsonCast", "json", target => $"CAST({target} AS JSON)");

	public string Id { get; }

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, _functionName, out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count != 1)
			return false;
		var target = context.TextOf(args[0].Start, args[0].End).Trim();
		if (target.Length == 0)
			return false;
		context.ReplaceRange(index, close - index + 1, _build(target));
		return true;
	}
}
