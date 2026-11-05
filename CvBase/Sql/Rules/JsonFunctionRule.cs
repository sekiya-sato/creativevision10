/*
# description
JsonFunctionRule は引数1個のJSON関数を対象DBの表現へ置き換えます（ルール B03 / B04）。

対象は次の2つです。
- `json_valid(X)` : 不正JSONガード。PostgreSQL は 16 以降の `IS JSON` 述語を使う。
  MariaDB は同名関数があるため書き換え不要（NativeConstructIds で扱う）。
- `json(X)` : JSONとしての正規化。PostgreSQL は `::jsonb`、MariaDB は `JSON_COMPACT(X)`（CAST AS JSON が無い）。

不正JSONを空配列へ置換する `json_valid()` ガードが必要なため、変換漏れが起きると不正JSONで
例外になる。そのためこのルールは引数1個の形だけを厳密に照合し、
それ以外は変換せず未対応構文として報告させます。

# example
PostgreSQL: json_valid(h.Jmeisai)  ->  ((h.Jmeisai) IS JSON)
MariaDB   : json(X.value2)         ->  JSON_COMPACT(X.value2)
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

	/// <summary>
	/// B04 json。MariaDB は JSON_COMPACT(x)。
	/// MariaDB には CAST(x AS JSON) が無い（MySQL にはある）。JSON_COMPACT はJSON型として扱われるため、
	/// JSON_ARRAYAGG の要素が文字列へ二重にエスケープされない。
	/// </summary>
	public static JsonFunctionRule JsonCastForMaria() =>
		new("B04-JsonCast", "json", target => $"JSON_COMPACT({target})");

	/// <summary>
	/// B09 json_type。PostgreSQL は IS JSON 述語で 'array' / 'object' を返す（不正JSONでも例外にしない）。
	/// それ以外の値は jsonb_typeof の名前（'string' / 'number' 等）になり SQLite（'text' / 'integer' 等）と違うが、
	/// CV10 の使用箇所はすべて <c>json_type(x) = 'array'</c> の判定である。
	/// </summary>
	public static JsonFunctionRule JsonTypeForPostgre() =>
		new("B09-JsonType", "json_type", target =>
			$"(CASE WHEN ({target}) IS JSON ARRAY THEN 'array' WHEN ({target}) IS JSON OBJECT THEN 'object' " +
			$"WHEN ({target}) IS JSON THEN jsonb_typeof(({target})::jsonb) END)");

	/// <summary>
	/// B09 json_type。MariaDB の JSON_TYPE は大文字（'ARRAY'）を返すので小文字へ揃える。
	/// 変換後も同名関数が残るため、<c>LOWER(</c> の直後にあるものは変換済みとして再変換しない（変換の冪等性）。
	/// </summary>
	public static JsonFunctionRule JsonTypeForMaria() =>
		new("B09-JsonType", "json_type", target => $"LOWER(JSON_TYPE({target}))", skip: IsWrappedByLower);

	/// <summary>直前が <c>LOWER(</c> か</summary>
	static bool IsWrappedByLower(SqlRewriteContext context, int index) {
		var open = context.PrevCode(index);
		if (open < 0 || !context.Tokens[open].IsOperator("("))
			return false;
		var name = context.PrevCode(open);
		return name >= 0 && context.Tokens[name].IsWord("lower");
	}

	readonly Func<SqlRewriteContext, int, bool>? _skip;

	/// <param name="id">ルールID</param>
	/// <param name="functionName">SQLite側の関数名</param>
	/// <param name="build">引数の式から置換後の式を作る</param>
	/// <param name="skip">この位置では変換しない条件</param>
	public JsonFunctionRule(string id, string functionName, Func<string, string> build, Func<SqlRewriteContext, int, bool>? skip)
		: this(id, functionName, build) {
		_skip = skip;
	}

	public string Id { get; }

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, _functionName, out var open, out var close))
			return false;
		if (_skip != null && _skip(context, index))
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
