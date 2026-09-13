/*
# description
GroupConcatRule は SQLite の `group_concat` を対象DBの集約関数へ写像します（ルール A05）。

対象は引数1個・2個の素の形だけです。
- 1引数 `group_concat(X)` : 既定セパレータは `,`。
  MariaDB は同名関数で既定セパレータも `,` のため綴りを合わせるだけの写像。
  PostgreSQL は同名関数が無いため `string_agg((X)::text, ',')` へ。
- 2引数 `group_concat(X, sep)` : セパレータ指定。
  PostgreSQL は `string_agg((X)::text, sep)`（string_agg は text 型必須なのでキャストする）。
  MariaDB はカンマ区切りのままだと第2引数が別の集約対象と解釈されるため、
  必ず `GROUP_CONCAT(X SEPARATOR sep)` の形にする。

SQLite は `group_concat(DISTINCT X)` と、3.44以降の集約内 `ORDER BY` も受け付けます。
これらは各DBで書き方が違い（PostgreSQL は `string_agg(DISTINCT X::text, sep)` のように
DISTINCT がキャストの外へ出る）、素直な写像にならないため**変換しません**。
変換しなければ PostgreSQL では未対応構文として報告され、Strictモードなら例外で気付けます。
MariaDB は同名関数を持ち `A05-GroupConcat` を NativeConstructIds へ載せている都合上、
これらの形も報告されません。ただし開発時の自己検査（SqlDialectGuard）は
PostgreSQL と MariaDB の両方を見るため、PostgreSQL 側の報告で気付けます。

実際の使用は CvWpfclient/ViewModels/01Master/MasterShainMenteViewModel.cs の
印刷用SQLの2引数形1箇所のみ。並び順はSQLiteも対象DBも非決定なので、
順序保証は追わない（B04-JsonGroupArray と同じ扱い）。

# example
PostgreSQL: group_concat(X.Line, ' / ')  ->  string_agg((X.Line)::text, ' / ')
MariaDB   : group_concat(X.Line, ' / ')  ->  GROUP_CONCAT(X.Line SEPARATOR ' / ')
PostgreSQL: group_concat(X.Line)         ->  string_agg((X.Line)::text, ',')
MariaDB   : group_concat(X.Line)         ->  無変換（同名関数・同じ既定セパレータ）
 */
namespace CvBase.Sql.Rules;

/// <summary>A05: group_concat を対象DBの集約関数へ写像する</summary>
public sealed class GroupConcatRule : ISqlRewriteRule {

	readonly Func<string, string?, string?> _build;

	GroupConcatRule(Func<string, string?, string?> build) {
		_build = build;
	}

	/// <summary>PostgreSQL向け。1引数・2引数のいずれも string_agg へ写像する。</summary>
	public static GroupConcatRule ForPostgre() => new((expression, separator) =>
		$"string_agg(({expression})::text, {separator ?? SqlArgumentReader.Quote(",")})");

	/// <summary>
	/// MariaDB向け。2引数だけを SEPARATOR 形へ写像する。
	/// 1引数は同名関数・同じ既定セパレータのため変換しない（呼び出し元が NativeConstructIds で扱う）。
	/// </summary>
	public static GroupConcatRule ForMaria() => new((expression, separator) =>
		separator == null ? null : $"GROUP_CONCAT({expression} SEPARATOR {separator})");

	public string Id => "A05-GroupConcat";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "group_concat", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count is < 1 or > 2)
			return false;
		// DISTINCT・集約内 ORDER BY は素直な写像にならないので変換しない（未対応構文として報告させる）
		for (var i = open + 1; i < close; i++) {
			var inner = context.Tokens[i];
			if (inner.IsWord("distinct") || inner.IsWord("order"))
				return false;
		}

		var expression = SqlArgumentReader.Text(context, args[0]);
		if (expression.Length == 0)
			return false;

		string? separator = null;
		if (args.Count == 2) {
			separator = SqlArgumentReader.Text(context, args[1]);
			if (separator.Length == 0)
				return false;
		}

		var replacement = _build(expression, separator);
		if (replacement == null)
			return false;
		context.ReplaceRange(index, close - index + 1, replacement);
		return true;
	}
}
