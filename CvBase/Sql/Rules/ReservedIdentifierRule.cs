/*
# description
ReservedIdentifierRule は、対象DBの予約語と衝突する列名を引用します（ルール A03）。

CV10 のテーブル定義で予約語と衝突する列名は `Offset`（Summary系4テーブル）と
`Sql`（MasterConfig系）だけです。列名の集合は49テーブルで閉じているため、
固定の一覧で対応できます。

PostgreSQL は非引用識別子を小文字へ畳むため、引用する際も小文字にします
（`CvBasePostgre` がDDLを小文字で作るのと揃える）。MariaDB はバッククォートで
大文字小文字をそのまま保ちます。

`LIMIT n OFFSET m` の `OFFSET` を列名と誤認しないよう、直後が数値・パラメータの場合は
句のキーワードとみなして引用しません。

# example
PostgreSQL: ifnull(s.Offset, 0)  ->  ifnull(s."offset", 0)
MariaDB   : ifnull(s.Offset, 0)  ->  ifnull(s.`Offset`, 0)
 */
namespace CvBase.Sql.Rules;

/// <summary>A03: 予約語と衝突する列名を引用する</summary>
public sealed class ReservedIdentifierRule : ISqlRewriteRule {

	/// <summary>
	/// 引用が必要な列名。
	/// <para>
	/// <c>Offset</c> は PostgreSQL / MariaDB の双方で予約語。
	/// <c>Sql</c> は MariaDB の予約語。両DBで引用しても実行結果は変わらないため一覧を共有する。
	/// <c>Size</c> / <c>Status</c> は双方で非予約語なので対象にしない。
	/// </para>
	/// </summary>
	public static IReadOnlyList<string> ReservedColumnNames { get; } = ["Offset", "Sql"];

	readonly HashSet<string> _targets = new(ReservedColumnNames, StringComparer.OrdinalIgnoreCase);
	readonly Func<string, string> _quote;

	/// <param name="quote">列名を引用する関数</param>
	public ReservedIdentifierRule(Func<string, string> quote) {
		_quote = quote;
	}

	/// <summary>PostgreSQL向け。非引用識別子が小文字へ畳まれるため小文字で引用する。</summary>
	public static ReservedIdentifierRule ForPostgre() =>
		new(name => $"\"{name.ToLowerInvariant()}\"");

	/// <summary>MariaDB向け。バッククォートで元の綴りを保つ。</summary>
	public static ReservedIdentifierRule ForMaria() =>
		new(name => $"`{name}`");

	public string Id => "A03-ReservedIdent";

	public bool Apply(SqlRewriteContext context, int index) {
		var token = context.Tokens[index];
		if (token.Kind != SqlTokenKind.Word || !_targets.Contains(token.Text))
			return false;

		// LIMIT n OFFSET m の句キーワードは列名ではない
		var next = context.NextCode(index);
		if (next >= 0) {
			var following = context.Tokens[next];
			if (following.Kind is SqlTokenKind.Number or SqlTokenKind.Parameter)
				return false;
		}
		// 関数呼び出しの形なら列名ではない
		if (next >= 0 && context.Tokens[next].IsOperator("("))
			return false;

		context.Replace(index, _quote(token.Text));
		return true;
	}
}
