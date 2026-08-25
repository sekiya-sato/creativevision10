/*
# description
StrftimeRule は `strftime(書式, 対象)` を対象DBの日付整形へ置き換えます（ルール B05）。

CV10 で使われている書式は実測で5種類だけです（`%Y%m` / `%Y%m%d` / `%w` / `%d` / `%s`）。
永続列は `yyyyMMdd` / `yyyyMM` の文字列のままなので、変換対象は物理型ではなく
「文字列日付を整形・曜日算出する式」です。

`%w` は SQLite が文字列 '0'〜'6' を返すため、比較（`WHEN '0' THEN`）に合わせて
文字列で返します。SQLiteでは `strftime('%w',x) + 6` のような暗黙の数値化が通りますが
PostgreSQL では通りません。該当は2箇所で、`cast(... as integer)` を足せば
SQLiteでも意味が変わらないため、ソース側で対応します（Phase 7）。

`%s` は常に算術に使われるため数値で返します。`'now'` を対象にする形だけを扱います。

# example
PostgreSQL: strftime('%Y%m', d)  ->  to_char((d)::date,'YYYYMM')
MariaDB   : strftime('%Y%m', d)  ->  DATE_FORMAT(d,'%Y%m')
 */
namespace CvBase.Sql.Rules;

/// <summary>B05: strftime の写像</summary>
public sealed class StrftimeRule : ISqlRewriteRule {

	/// <summary>書式ごとの生成。第2引数は対象式、第3引数は対象が現在時刻(<c>'now'</c>)か。</summary>
	readonly Dictionary<string, Func<string, bool, string?>> _formats;

	StrftimeRule(Dictionary<string, Func<string, bool, string?>> formats) {
		_formats = formats;
	}

	/// <summary>PostgreSQL向け。</summary>
	public static StrftimeRule ForPostgre() => new(new() {
		["%Y%m"] = (target, isNow) => isNow ? "to_char(now(),'YYYYMM')" : $"to_char(({target})::date,'YYYYMM')",
		["%Y%m%d"] = (target, isNow) => isNow ? "to_char(now(),'YYYYMMDD')" : $"to_char(({target})::date,'YYYYMMDD')",
		["%d"] = (target, isNow) => isNow ? "to_char(now(),'DD')" : $"to_char(({target})::date,'DD')",
		// SQLite と同じ '0'〜'6' の文字列を返す
		["%w"] = (target, isNow) => isNow
			? "extract(dow from now())::integer::text"
			: $"extract(dow from ({target})::date)::integer::text",
		// %s は常に算術に使われるため数値で返す
		["%s"] = (target, isNow) => isNow
			? "extract(epoch from now())::bigint"
			: $"extract(epoch from ({target})::timestamp)::bigint",
	});

	/// <summary>MariaDB向け。DATE_FORMAT は 'yyyy-MM-dd' 形式の文字列を解釈できる。</summary>
	public static StrftimeRule ForMaria() => new(new() {
		["%Y%m"] = (target, isNow) => isNow ? "DATE_FORMAT(NOW(),'%Y%m')" : $"DATE_FORMAT({target},'%Y%m')",
		["%Y%m%d"] = (target, isNow) => isNow ? "DATE_FORMAT(NOW(),'%Y%m%d')" : $"DATE_FORMAT({target},'%Y%m%d')",
		["%d"] = (target, isNow) => isNow ? "DATE_FORMAT(NOW(),'%d')" : $"DATE_FORMAT({target},'%d')",
		["%w"] = (target, isNow) => isNow
			? "CAST(DAYOFWEEK(NOW())-1 AS CHAR)"
			: $"CAST(DAYOFWEEK({target})-1 AS CHAR)",
		["%s"] = (target, isNow) => isNow ? "UNIX_TIMESTAMP()" : $"UNIX_TIMESTAMP({target})",
	});

	public string Id => "B05-Strftime";

	public bool Apply(SqlRewriteContext context, int index) {
		if (!context.TryMatchCall(index, "strftime", out var open, out var close))
			return false;
		var args = context.SplitArguments(open, close);
		if (args.Count < 2)
			return false;

		var format = SqlArgumentReader.SingleStringLiteral(context, args[0]);
		if (format == null || !_formats.TryGetValue(format, out var build))
			return false;

		var target = SqlArgumentReader.Text(context, args[1]);
		if (target.Length == 0)
			return false;

		var isNow = string.Equals(target, "'now'", StringComparison.OrdinalIgnoreCase);
		// 3引数以上は 'now','localtime' の形だけを許す。他の修飾子は意味が変わるため変換しない
		if (args.Count > 2) {
			if (!isNow)
				return false;
			for (var i = 2; i < args.Count; i++) {
				var modifier = SqlArgumentReader.SingleStringLiteral(context, args[i]);
				if (!string.Equals(modifier, "localtime", StringComparison.OrdinalIgnoreCase))
					return false;
			}
		}

		var replacement = build(target, isNow);
		if (replacement == null)
			return false;
		context.ReplaceRange(index, close - index + 1, replacement);
		return true;
	}
}
