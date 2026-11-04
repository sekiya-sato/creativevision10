/*
# description
ScalarMinMaxRule は SQLite の「引数2個以上の max / min」を GREATEST / LEAST へ置き換えます（ルール A06）。

SQLite の max(a, b) は「引数のうち最大」を返すスカラー関数で、引数1個のときだけ集約関数になります。
MariaDB / PostgreSQL の MAX / MIN は集約関数だけなので、引数2個以上は GREATEST / LEAST に直します。
引数1個（集約）は書き換えません。

NULL の扱い: SQLite と MariaDB は引数に NULL があると NULL、PostgreSQL の GREATEST / LEAST は NULL を無視します。
CV10 の使用箇所は ifnull で NULL を潰した値に対して使っているため結果は変わりません。

# example
max(zan.Su - ifnull(x, 0), 0)  ->  GREATEST(zan.Su - ifnull(x, 0), 0)
max(a.Su)                      ->  max(a.Su)（集約はそのまま）
 */
namespace CvBase.Sql.Rules;

/// <summary>A06: 引数2個以上の max / min → GREATEST / LEAST</summary>
public sealed class ScalarMinMaxRule : ISqlRewriteRule {

	public string Id => "A06-ScalarMinMax";

	public bool Apply(SqlRewriteContext context, int index) {
		foreach (var (name, replacement) in new[] { ("max", "GREATEST"), ("min", "LEAST") }) {
			if (!context.TryMatchCall(index, name, out var open, out var close))
				continue;
			if (context.SplitArguments(open, close).Count < 2)
				return false;
			context.Replace(index, replacement);
			return true;
		}
		return false;
	}
}
