/*
# description
IfnullRule は SQLite の `ifnull(a,b)` を `coalesce(a,b)` へ置き換えます（ルール A01）。

CV10 のクライアントSQLで最も多いSQLite固有構文で、実測 503箇所（80ファイル）あります。
MariaDB は同名関数を持つためこのルールは不要で、PostgreSQL だけが対象です。

引数の並びは `ifnull` と `coalesce` で同じなので、関数名の字句1つを差し替えるだけで済みます。
文字列リテラルやコメントの中の `ifnull` は字句解析で語として扱われないため書き換わりません。

# example
select ifnull(a, 0) from T   ->   select coalesce(a, 0) from T
 */
namespace CvBase.Sql.Rules;

/// <summary>A01: ifnull → coalesce（PostgreSQL向け）</summary>
public sealed class IfnullRule : ISqlRewriteRule {

	public string Id => "A01-Ifnull";

	public bool Apply(SqlRewriteContext context, int index) {
		// 関数呼び出しの形であることを確認する。括弧が伴わない `ifnull` という名前の列は書き換えない
		if (!context.TryMatchCall(index, "ifnull", out _, out _))
			return false;
		context.Replace(index, "coalesce");
		return true;
	}
}
