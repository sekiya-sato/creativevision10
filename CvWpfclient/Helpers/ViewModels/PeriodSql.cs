/*
# description
PeriodSql は「日別 / 週別 / 月別」で集計軸を切り替える帳票のための SQL 断片を作るヘルパーです。

売上分析系の帳票は同じデータを日・週・月のどれかで括って出すものが多く、
そのたびに strftime の書式を書き分けると取り違えるため集約しました。

伝票日付は yyyyMMdd の8桁文字列なので、strftime へ渡す前に yyyy-MM-dd へ整形します。

# example
var unit = PeriodUnit.Week;
var sql = $@"
select {PeriodSql.Label("h.DenDay", unit)} periodLabel, sum(h.KingakuTotal) kingaku
from Tran01Tenuri h
group by {PeriodSql.Key("h.DenDay", unit)}
order by {PeriodSql.Key("h.DenDay", unit)}";
 */
namespace CvWpfclient.Helpers;

/// <summary>集計の期間単位</summary>
internal enum PeriodUnit {
	Day,
	Week,
	Month,
}

internal static class PeriodSql {
	/// <summary>yyyyMMdd 8桁を strftime が扱える yyyy-MM-dd へ整形する。</summary>
	internal static string ToDate(string column) =>
		$"(substr({column},1,4) || '-' || substr({column},5,2) || '-' || substr({column},7,2))";

	/// <summary>
	/// 並び替え・GROUP BY 用のキー。昇順に並べれば時系列になる。
	/// 週は ISO 週ではなく「その週の月曜日の日付」を使う（年をまたぐ週の並びが崩れないため）。
	/// </summary>
	internal static string Key(string column, PeriodUnit unit) => unit switch {
		PeriodUnit.Day => column,
		// strftime('%w') は 0=日曜。月曜起点にするため日曜を7として扱う。
		PeriodUnit.Week => $"date({ToDate(column)}, '-' || ((strftime('%w', {ToDate(column)}) + 6) % 7) || ' days')",
		_ => $"substr({column},1,6)",
	};

	/// <summary>印字用の期間ラベル。</summary>
	internal static string Label(string column, PeriodUnit unit) => unit switch {
		PeriodUnit.Day => $"(substr({column},1,4) || '/' || substr({column},5,2) || '/' || substr({column},7,2))",
		PeriodUnit.Week => $"(replace({Key(column, unit)}, '-', '/') || '週')",
		_ => $"(substr({column},1,4) || '/' || substr({column},5,2))",
	};

	/// <summary>曜日の日本語1文字。日別集計のときだけ意味がある。</summary>
	internal static string Youbi(string column) => $@"CASE strftime('%w', {ToDate(column)})
        WHEN '0' THEN '日' WHEN '1' THEN '月' WHEN '2' THEN '火'
        WHEN '3' THEN '水' WHEN '4' THEN '木' WHEN '5' THEN '金' WHEN '6' THEN '土' END";
}
