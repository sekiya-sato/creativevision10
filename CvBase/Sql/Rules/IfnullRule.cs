/*
# description
IfnullRule は SQLite の `ifnull(a,b)` を `coalesce(a,b)` へ置き換えます（ルール A01）。

CV10 のクライアントSQLで最も多いSQLite固有構文で、実測 503箇所（80ファイル）あります。
MariaDB は同名関数を持つためこのルールは不要で、PostgreSQL だけが対象です。

引数の並びは `ifnull` と `coalesce` で同じなので、通常は関数名の字句1つを差し替えるだけで済みます。
文字列リテラルやコメントの中の `ifnull` は字句解析で語として扱われないため書き換わりません。

ただし `ifnull(json_extract(x,'$.P'), 0)` のように「JSONから取り出した値」と「数値リテラル」の組だけは
型を合わせます。PostgreSQL の `->>` はテキストを返し、`coalesce(text, 0)` は型エラーになるためです
（SQLite は json_extract が数値をそのまま返すので問題にならない）。JSON側を numeric へ変換し、
空文字は SQLite の `cast('' as integer)` = 0 と同じになるよう `nullif(…,'')` で NULL に寄せます。
走査は右から左なので、この時点で第1引数の json_extract は B01 で `->>` へ変換済みです。

# example
select ifnull(a, 0) from T                       ->   select coalesce(a, 0) from T
ifnull(((m.value)::jsonb ->> 'Su'), 0)           ->   coalesce(nullif(((m.value)::jsonb ->> 'Su'),'')::numeric, 0)
 */
using System.Text.RegularExpressions;

namespace CvBase.Sql.Rules;

/// <summary>A01: ifnull → coalesce（PostgreSQL向け）</summary>
public sealed partial class IfnullRule : ISqlRewriteRule {

	public string Id => "A01-Ifnull";

	/// <summary>B01(PostgreSQL)が作る JSON テキスト取り出しの形 `((x)::jsonb ->> 'Prop')`</summary>
	[GeneratedRegex(@"^\(\(.+\)::jsonb ->> '\w+'\)$", RegexOptions.Singleline)]
	private static partial Regex JsonTextExtract();

	/// <summary>数値リテラル（符号・小数を含む）</summary>
	[GeneratedRegex(@"^-?\d+(\.\d+)?$")]
	private static partial Regex NumericLiteral();

	public bool Apply(SqlRewriteContext context, int index) {
		// 関数呼び出しの形であることを確認する。括弧が伴わない `ifnull` という名前の列は書き換えない
		if (!context.TryMatchCall(index, "ifnull", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count == 2) {
			var value = context.TextOf(args[0].Start, args[0].End).Trim();
			var fallback = context.TextOf(args[1].Start, args[1].End).Trim();
			if (NumericLiteral().IsMatch(fallback) && JsonTextExtract().IsMatch(value)) {
				context.ReplaceRange(index, close - index + 1, $"coalesce(nullif({value},'')::numeric, {fallback})");
				return true;
			}
		}
		context.Replace(index, "coalesce");
		return true;
	}
}
