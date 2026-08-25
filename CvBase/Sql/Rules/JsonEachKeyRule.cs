/*
# description
JsonEachKeyRule は `json_each` の別名に対する `.key` 参照を行番号列へ読み替えます（ルール B02 の一部）。

SQLite の `json_each` は配列要素の並び順を `key` 列で返します。CV10 では
`order by cast(J.key as integer)` として、配列を作り直すときに元の要素順を保つために使っています。

他DBでは行番号列を明示的に作る必要があり（PostgreSQL は `WITH ORDINALITY`、
MariaDB は `FOR ORDINALITY`）、さらに `key` は MariaDB の予約語なので別の列名にします。
このルールはその列名へ参照側を合わせます。

対象は「SQL内で `.key` を参照している json_each の別名」だけです。
無関係なテーブルの `key` という列名は書き換えません。

# example
J.key  ->  J.jkey     （FROM句側は JsonEachRule が行番号列を作る）
 */
namespace CvBase.Sql.Rules;

/// <summary>B02: json_each 別名の .key 参照を行番号列名へ読み替える</summary>
public sealed class JsonEachKeyRule : ISqlRewriteRule {

	public string Id => "B02-JsonEach";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.Tokens[index].IsWord("key"))
			return false;
		var dot = context.PrevCode(index);
		if (dot < 0 || !context.Tokens[dot].IsOperator("."))
			return false;
		var owner = context.PrevCode(dot);
		if (owner < 0 || context.Tokens[owner].Kind != SqlTokenKind.Word)
			return false;
		// json_each の別名で、かつ .key を参照しているものだけを対象にする
		if (!context.OrdinalityAliases.Contains(context.Tokens[owner].Text))
			return false;
		context.Replace(index, SqlRewriteContext.OrdinalityColumn);
		return true;
	}
}
