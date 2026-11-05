/*
# description
PrintfRule は `printf(書式, 値...)` をゼロ埋め整形へ置き換えます（ルール B06）。

CV10 で使われている書式指定子は `%02d` / `%04d` のゼロ埋め10進数と、
請求番号・支払番号の組み立てに使う `%d`（整数）/ `%s`（文字列）です。
`%d` は整数の値だけを対象とします（小数の切り捨ては再現しない）。
NULL は SQLite と同じく `%d` を '0'、`%s` を空文字にします。
`printf('%04d-%02d-%02d', y, m, d)` のように区切り文字を挟む形もあるため、
書式を「リテラル部分」と「%0Nd / %d / %s」の列として解釈し、連結式を組み立てます。

指定子の個数と値引数の個数が一致しない場合、またそれ以外の指定子が含まれる場合は
変換しません。

# example
PostgreSQL: printf('%02d', n)                  ->  lpad((n)::text,2,'0')
MariaDB   : printf('%04d-%02d-%02d', y, m, d)  ->  CONCAT(LPAD(y,4,'0'),'-',LPAD(m,2,'0'),'-',LPAD(d,2,'0'))
MariaDB   : printf('%d-%s', n, s)               ->  CONCAT(COALESCE(n,0),'-',COALESCE(s,''))
 */
using System.Text;

namespace CvBase.Sql.Rules;

/// <summary>B06: printf のゼロ埋め整形を写像する</summary>
public sealed class PrintfRule : ISqlRewriteRule {

	readonly Func<string, int, string> _pad;
	readonly Func<string, char, string> _plain;
	readonly Func<IReadOnlyList<string>, string> _concat;

	PrintfRule(Func<string, int, string> pad, Func<string, char, string> plain, Func<IReadOnlyList<string>, string> concat) {
		_pad = pad;
		_plain = plain;
		_concat = concat;
	}

	/// <summary>PostgreSQL向け。</summary>
	public static PrintfRule ForPostgre() => new(
		(value, width) => $"lpad(({value})::text,{width},'0')",
		(value, spec) => spec == 'd' ? $"COALESCE(({value})::text,'0')" : $"COALESCE(({value})::text,'')",
		parts => $"({string.Join(" || ", parts)})");

	/// <summary>MariaDB向け。連結は sql_mode に依存しない CONCAT を使う。</summary>
	public static PrintfRule ForMaria() => new(
		(value, width) => $"LPAD({value},{width},'0')",
		(value, spec) => spec == 'd' ? $"COALESCE({value},0)" : $"COALESCE({value},'')",
		parts => parts.Count == 1 ? parts[0] : $"CONCAT({string.Join(", ", parts)})");

	public string Id => "B06-Printf";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "printf", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count < 2)
			return false;

		var format = SqlArgumentReader.SingleStringLiteral(context, args[0]);
		if (format == null)
			return false;
		var segments = ParseFormat(format);
		if (segments == null)
			return false;

		var specifierCount = segments.Count(s => s.IsSpecifier);
		if (specifierCount != args.Count - 1)
			return false;

		var parts = new List<string>();
		var argIndex = 1;
		foreach (var segment in segments) {
			if (segment.IsSpecifier) {
				var value = SqlArgumentReader.Text(context, args[argIndex++]);
				if (value.Length == 0)
					return false;
				parts.Add(segment.Width > 0 ? _pad(value, segment.Width) : _plain(value, segment.Spec));
			}
			else if (segment.Literal.Length > 0) {
				parts.Add(SqlArgumentReader.Quote(segment.Literal));
			}
		}
		if (parts.Count == 0)
			return false;

		context.ReplaceRange(index, close - index + 1, parts.Count == 1 ? parts[0] : _concat(parts));
		return true;
	}

	/// <summary>書式を「リテラル」と「%0Nd / %d / %s」の列へ分解する。未対応の指定子があれば null。</summary>
	static List<FormatSegment>? ParseFormat(string format) {
		var segments = new List<FormatSegment>();
		var literal = new StringBuilder();
		var i = 0;
		while (i < format.Length) {
			if (format[i] != '%') {
				literal.Append(format[i]);
				i++;
				continue;
			}
			// %% はリテラルの %
			if (i + 1 < format.Length && format[i + 1] == '%') {
				literal.Append('%');
				i += 2;
				continue;
			}
			// 幅指定なしの %d / %s
			if (i + 1 < format.Length && format[i + 1] is 'd' or 's') {
				if (literal.Length > 0) {
					segments.Add(new FormatSegment(literal.ToString(), 0));
					literal.Clear();
				}
				segments.Add(new FormatSegment(string.Empty, 0, format[i + 1]));
				i += 2;
				continue;
			}
			// それ以外は %0Nd だけを受け付ける
			if (i + 3 >= format.Length || format[i + 1] != '0')
				return null;
			var digits = 0;
			var j = i + 2;
			while (j < format.Length && char.IsAsciiDigit(format[j])) {
				digits = digits * 10 + (format[j] - '0');
				j++;
			}
			if (digits <= 0 || j >= format.Length || format[j] != 'd')
				return null;
			if (literal.Length > 0) {
				segments.Add(new FormatSegment(literal.ToString(), 0));
				literal.Clear();
			}
			segments.Add(new FormatSegment(string.Empty, digits, 'd'));
			i = j + 1;
		}
		if (literal.Length > 0)
			segments.Add(new FormatSegment(literal.ToString(), 0));
		return segments;
	}

	/// <param name="Literal">リテラル部分（指定子のときは空）</param>
	/// <param name="Width">ゼロ埋め幅（%0Nd のN。幅指定なしは0）</param>
	/// <param name="Spec">指定子の型文字（'d' / 's'）。リテラルのときは '\0'</param>
	readonly record struct FormatSegment(string Literal, int Width, char Spec = '\0') {
		public bool IsSpecifier => Spec != '\0';
	}
}
