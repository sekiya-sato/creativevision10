/*
# description
JsonBuildRule はJSONを組み立てる関数を写像します（ルール B04 系）。

対象は3つで、いずれも V*列（CodeNameView）の物理化と Jsub / Jcolsiz 配列の再構築で使われます。
使用箇所は CvDomainLogic / CvBase のサーバ側SQLだけで、クライアントSQLには現れません。

- `json_group_array(X)` : 配列へ畳む。PostgreSQL は `jsonb_agg`、MariaDB は `JSON_ARRAYAGG`。
- `json_object('k', v, ...)` : オブジェクトを作る。PostgreSQL は `jsonb_build_object`。MariaDB は同名。
- `json_set(target, '$.k', v, ...)` : プロパティを差し替える。PostgreSQL は `jsonb_set` を
  パスごとに入れ子にする。MariaDB は同名・同じ引数並び。

`json_set` の PostgreSQL 変換だけは引数の形に踏み込むため、パスが単一階層 `'$.Prop'` で
かつ「パスと値の対」が揃っている場合に限ります。それ以外は変換しません。

# example
PostgreSQL: json_group_array(json(X.v))            -> jsonb_agg(((X.v)::jsonb))
PostgreSQL: json_set(J.value, '$.Cd', @1)          -> jsonb_set(J.value, '{Cd}', to_jsonb(@1), true)
MariaDB   : json_group_array(json(X.v))            -> JSON_ARRAYAGG(CAST(X.v AS JSON))
 */
namespace CvBase.Sql.Rules;

/// <summary>B04系: 関数名だけを置き換えるJSON組み立て関数</summary>
public sealed class JsonRenameRule : ISqlRewriteRule {

	readonly string _functionName;
	readonly string _replacement;

	public JsonRenameRule(string id, string functionName, string replacement) {
		Id = id;
		_functionName = functionName;
		_replacement = replacement;
	}

	/// <summary>B04 json_group_array → PostgreSQL jsonb_agg</summary>
	public static JsonRenameRule GroupArrayForPostgre() =>
		new("B04-JsonGroupArray", "json_group_array", "jsonb_agg");

	/// <summary>B04 json_group_array → MariaDB JSON_ARRAYAGG</summary>
	public static JsonRenameRule GroupArrayForMaria() =>
		new("B04-JsonGroupArray", "json_group_array", "JSON_ARRAYAGG");

	/// <summary>B04 json_object → PostgreSQL jsonb_build_object</summary>
	public static JsonRenameRule ObjectForPostgre() =>
		new("B04-JsonObject", "json_object", "jsonb_build_object");

	public string Id { get; }

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, _functionName, out _, out _))
			return false;
		context.Replace(index, _replacement);
		return true;
	}
}

/// <summary>B04: json_set を PostgreSQL の jsonb_set 入れ子へ展開する</summary>
public sealed class JsonSetRule : ISqlRewriteRule {

	public string Id => "B04-JsonSet";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "json_set", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		// 対象 + (パス, 値) の対。引数は3個以上の奇数個でなければ形が読めない
		if (args.Count < 3 || args.Count % 2 == 0)
			return false;

		var expression = SqlArgumentReader.Text(context, args[0]);
		if (expression.Length == 0)
			return false;

		for (var i = 1; i < args.Count; i += 2) {
			var property = JsonPath.SingleLevelProperty(context, args[i]);
			if (property == null)
				return false;
			var value = SqlArgumentReader.Text(context, args[i + 1]);
			if (value.Length == 0)
				return false;
			// jsonb_set のパスは配列表記。値は jsonb でなければならないので to_jsonb を通す
			expression = $"jsonb_set({expression}, '{{{property}}}', to_jsonb({value}), true)";
		}
		context.ReplaceRange(index, close - index + 1, expression);
		return true;
	}
}
