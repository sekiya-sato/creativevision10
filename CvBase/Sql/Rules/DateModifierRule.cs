/*
# description
DateModifierRule は SQLite の `date(対象, 修飾子...)` を日付加減算へ置き換えます（ルール B07）。

CV10 で使われている修飾子は年・月・日の加減算だけで、次の2つの形に限られます。
1. リテラル形: `'-1 year'` / `'+1 month'` / `'-1 month'`
2. 連結形: `'+' || n || ' months'` / `'-' || 式 || ' days'`

連結形は修飾子そのものが式なので、末尾のリテラルから単位を確定し、
符号リテラルと単位リテラルの間を数値式として取り出します。
この2形以外の修飾子（`'start of month'`、`'weekday 0'` など）は変換しません。

修飾子が複数あるときは左から順に適用します。

# example
PostgreSQL: date(d, '-1 year')                 ->  (((d)::date - interval '1 year'))::date
MariaDB   : date(d, '+' || n || ' months')     ->  DATE_ADD(d, INTERVAL (n) MONTH)
 */
namespace CvBase.Sql.Rules;

/// <summary>B07: date() の日付加減算を写像する</summary>
public sealed class DateModifierRule : ISqlRewriteRule {

	/// <summary>SQLiteの単位語 → 対象DBの単位語</summary>
	static readonly Dictionary<string, string> _units = new(StringComparer.OrdinalIgnoreCase) {
		["year"] = "YEAR",
		["years"] = "YEAR",
		["month"] = "MONTH",
		["months"] = "MONTH",
		["day"] = "DAY",
		["days"] = "DAY",
	};

	/// <summary>(対象式, 数値式, 単位, 加算か) から置換後の式を作る</summary>
	readonly Func<string, string, string, bool, string> _build;

	DateModifierRule(Func<string, string, string, bool, string> build) {
		_build = build;
	}

	/// <summary>PostgreSQL向け。interval 演算の結果を date へ戻す。</summary>
	public static DateModifierRule ForPostgre() => new((target, amount, unit, isAdd) =>
		$"((({target})::date {(isAdd ? "+" : "-")} (({amount}) || ' {unit}')::interval)::date)");

	/// <summary>MariaDB向け。DATE_ADD / DATE_SUB は文字列日付を解釈できる。</summary>
	public static DateModifierRule ForMaria() => new((target, amount, unit, isAdd) =>
		$"{(isAdd ? "DATE_ADD" : "DATE_SUB")}({target}, INTERVAL ({amount}) {unit})");

	public string Id => "B07-DateModifier";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "date", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		// 修飾子が無い date(x) は3DBで解釈できるため触らない
		if (args.Count < 2)
			return false;

		var expression = SqlArgumentReader.Text(context, args[0]);
		if (expression.Length == 0)
			return false;

		for (var i = 1; i < args.Count; i++) {
			var modifier = ReadModifier(context, args[i]);
			if (modifier == null)
				return false;
			expression = _build(expression, modifier.Value.Amount, modifier.Value.Unit, modifier.Value.IsAdd);
		}
		context.ReplaceRange(index, close - index + 1, expression);
		return true;
	}

	/// <summary>修飾子1個を読む。対応できない形なら null。</summary>
	static (string Amount, string Unit, bool IsAdd)? ReadModifier(SqlRewriteContext context, (int Start, int End) argument) {
		// リテラル形 '-1 year'
		var literal = SqlArgumentReader.SingleStringLiteral(context, argument);
		if (literal != null)
			return ReadLiteralModifier(literal);
		// 連結形 '+' || n || ' months'
		return ReadConcatModifier(context, argument);
	}

	static (string Amount, string Unit, bool IsAdd)? ReadLiteralModifier(string literal) {
		var text = literal.Trim();
		if (text.Length < 3)
			return null;
		// 符号が無い修飾子（'start of month' / 'weekday 0' など）は対象外
		var isAdd = text[0] switch {
			'+' => true,
			'-' => false,
			_ => (bool?)null,
		};
		if (isAdd == null)
			return null;

		var body = text[1..].Trim();
		var space = body.IndexOf(' ', StringComparison.Ordinal);
		if (space <= 0)
			return null;
		if (!int.TryParse(body[..space], out var value) || value < 0)
			return null;
		var unitWord = body[(space + 1)..].Trim();
		return _units.TryGetValue(unitWord, out var unit)
			? (value.ToString(), unit, isAdd.Value)
			: null;
	}

	/// <summary>
	/// 連結形の修飾子を読む。次の2つの形を扱う。
	/// <list type="bullet">
	/// <item><c>'+' || 数値式 || ' months'</c> : 符号リテラルを明示する形</item>
	/// <item><c>数値式 || ' days'</c> : 符号を省く形。SQLiteは符号なしを加算として扱う</item>
	/// </list>
	/// 符号を省く形では加算として生成する。数値式が負の値を返す場合も、
	/// PostgreSQL の interval と MariaDB の DATE_ADD はどちらも負値を減算として扱うため結果は一致する。
	/// </summary>
	static (string Amount, string Unit, bool IsAdd)? ReadConcatModifier(SqlRewriteContext context, (int Start, int End) argument) {
		var codes = new List<int>();
		for (var i = argument.Start; i <= argument.End; i++) {
			if (context.Tokens[i].IsCode)
				codes.Add(i);
		}
		// 最低でも 式 || 単位 の3字句
		if (codes.Count < 3)
			return null;

		// 末尾は 単位リテラル で、その前は ||
		var last = context.Tokens[codes[^1]];
		if (last.Kind != SqlTokenKind.StringLiteral || !context.Tokens[codes[^2]].IsOperator("||"))
			return null;
		var unitWord = last.Text[1..^1].Trim();
		if (!_units.TryGetValue(unitWord, out var unit))
			return null;

		// 先頭が符号だけの文字列リテラル + || なら符号として取り込む
		var isAdd = true;
		var amountStart = 0;
		var first = context.Tokens[codes[0]];
		if (first.Kind == SqlTokenKind.StringLiteral && codes.Count >= 5 && context.Tokens[codes[1]].IsOperator("||")) {
			var sign = first.Text[1..^1].Trim();
			var parsed = sign switch { "+" => true, "-" => false, _ => (bool?)null };
			if (parsed == null)
				return null;
			isAdd = parsed.Value;
			amountStart = 2;
		}

		var amount = context.TextOf(codes[amountStart], codes[^3]).Trim();
		return amount.Length == 0 ? null : (amount, unit, isAdd);
	}
}
