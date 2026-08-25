/*
# description
JuliandayDiffRule は `julianday(A) - julianday(B)` を日数差へ置き換えます（ルール B08）。

対象は「関数」ではなく「減算の並び」です。CV10 での `julianday` の使い方は
納期遅延日数などの日数差だけで、実測16箇所（3ファイル）が全てこの形です。
単独の `julianday(x)` はユリウス日そのものを意味し、他DBに等価な式が無いため変換しません
（未対応構文として報告されます）。

# example
PostgreSQL: julianday(a) - julianday(b)  ->  (((a)::date - (b)::date))
MariaDB   : julianday(a) - julianday(b)  ->  DATEDIFF(a, b)
 */
namespace CvBase.Sql.Rules;

/// <summary>B08: julianday の減算を日数差へ写像する</summary>
public sealed class JuliandayDiffRule : ISqlRewriteRule {

	readonly Func<string, string, string> _build;

	JuliandayDiffRule(Func<string, string, string> build) {
		_build = build;
	}

	/// <summary>PostgreSQL向け。date 同士の減算は日数(integer)になる。</summary>
	public static JuliandayDiffRule ForPostgre() =>
		new((from, to) => $"((({from})::date - (({to})::date)))");

	/// <summary>MariaDB向け。</summary>
	public static JuliandayDiffRule ForMaria() =>
		new((from, to) => $"DATEDIFF({from}, {to})");

	public string Id => "B08-Julianday";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!TryReadCallArgument(context, index, out var firstClose, out var first))
			return false;
		var minus = context.NextCode(firstClose);
		if (minus < 0 || !context.Tokens[minus].IsOperator("-"))
			return false;
		var second = context.NextCode(minus);
		if (second < 0 || !TryReadCallArgument(context, second, out var secondClose, out var subtrahend))
			return false;

		context.ReplaceRange(index, secondClose - index + 1, _build(first, subtrahend));
		return true;
	}

	/// <summary><c>julianday(X)</c> の形なら閉じ括弧位置と引数の式を返す。</summary>
	static bool TryReadCallArgument(SqlRewriteContext context, int index, out int closeIndex, out string argument) {
		closeIndex = -1;
		argument = string.Empty;
		if (!context.TryMatchCall(index, "julianday", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count != 1)
			return false;
		var text = SqlArgumentReader.Text(context, args[0]);
		if (text.Length == 0)
			return false;
		closeIndex = close;
		argument = text;
		return true;
	}
}
