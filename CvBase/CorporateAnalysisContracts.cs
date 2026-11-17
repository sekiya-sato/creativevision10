using System.Globalization;

namespace CvBase;

/// <summary>
/// 全社分析の期間単位。既定値(0)と JSON 往復で値が変わらないよう、最もよく使う月を0にしている。
/// </summary>
public enum CorporateAnalysisUnit {
	/// <summary>計上月（自社締日による）。From/To は yyyyMM</summary>
	Month = 0,
	/// <summary>会計年度（期首月から12か月）。From/To は年度の開始年 yyyy</summary>
	Year = 1,
	/// <summary>日。From/To は yyyyMMdd</summary>
	Day = 2,
}

/// <summary>全社分析の内訳軸。商品マスタの属性で括る。</summary>
public enum CorporateAnalysisAxis {
	/// <summary>全社（1行に合計）</summary>
	None = 0,
	Brand = 1,
	Item = 2,
	Tenji = 3,
	Season = 4,
	Material = 5,
	/// <summary>商品（品番）。絞り込みの終端</summary>
	Shohin = 6,
}

/// <summary>商品軸で件数上限を掛ける前の並び順。</summary>
public enum CorporateAnalysisSortKey {
	/// <summary>売上金額の降順</summary>
	Sales = 0,
	/// <summary>期末在庫金額の降順（売れ残りの確認向け）</summary>
	ClosingStock = 1,
	/// <summary>発注金額の降順（発注過多の確認向け）</summary>
	Hachu = 2,
}

/// <summary>絞込1件。商品マスタの属性 Id（0=未設定）で絞る。</summary>
public sealed class CorporateAnalysisFilter {
	public CorporateAnalysisAxis Axis { get; set; }
	public long Id { get; set; }
	/// <summary>画面表示用の名称（サーバは使わない）</summary>
	public string Name { get; set; } = string.Empty;
}

/// <summary>全社分析の条件。Msg068_CorporateAnalysis で使う。</summary>
public sealed class CorporateAnalysisParameter {
	public CorporateAnalysisUnit Unit { get; set; }
	/// <summary>開始（年=yyyy / 月=yyyyMM / 日=yyyyMMdd）</summary>
	public string From { get; set; } = string.Empty;
	/// <summary>終了（年=yyyy / 月=yyyyMM / 日=yyyyMMdd）</summary>
	public string To { get; set; } = string.Empty;
	/// <summary>true=区切りごとの行（推移表） / false=期間全体を1区切り（内訳表）</summary>
	public bool ByBucket { get; set; }
	public CorporateAnalysisAxis GroupAxis { get; set; }
	public List<CorporateAnalysisFilter> Filters { get; set; } = [];
	/// <summary>商品軸の件数上限。0=既定(<see cref="CorporateAnalysisPeriod.DefaultLimit"/>)</summary>
	public int Limit { get; set; }
	public CorporateAnalysisSortKey SortKey { get; set; }
}

/// <summary>期間の1区切り。</summary>
public sealed class CorporateAnalysisBucket {
	/// <summary>区切りキー（年・月=開始計上月 yyyyMM / 日=yyyyMMdd / 期間全体=空文字）</summary>
	public string Key { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	/// <summary>実日付の開始 yyyyMMdd</summary>
	public string DayFrom { get; set; } = string.Empty;
	/// <summary>実日付の終了 yyyyMMdd</summary>
	public string DayTo { get; set; } = string.Empty;
	/// <summary>在庫集計の計上月（年・月単位のみ。日単位は空）</summary>
	public string MonthFrom { get; set; } = string.Empty;
	public string MonthTo { get; set; } = string.Empty;
	/// <summary>日数（在庫回転日数の計算用）</summary>
	public int Days { get; set; }
}

/// <summary>区切り×軸の集計1行。金額は税抜・原価は商品マスタの現在原価。</summary>
public sealed class CorporateAnalysisRow {
	public string BucketKey { get; set; } = string.Empty;
	/// <summary>軸の Id（全社は0。属性未設定も0）</summary>
	public long GroupId { get; set; }
	public string GroupCode { get; set; } = string.Empty;
	public string GroupName { get; set; } = string.Empty;
	public long OpeningStockQty { get; set; }
	public long OpeningStockAmount { get; set; }
	public long ClosingStockQty { get; set; }
	public long ClosingStockAmount { get; set; }
	public long HachuQty { get; set; }
	public long HachuAmount { get; set; }
	public long JuchuQty { get; set; }
	public long JuchuAmount { get; set; }
	public long ShiireQty { get; set; }
	public long ShiireAmount { get; set; }
	public long SalesQty { get; set; }
	public long SalesAmount { get; set; }
	public long SalesCost { get; set; }
	public long GrossProfit => SalesAmount - SalesCost;
}

/// <summary>全社分析の結果。</summary>
public sealed class CorporateAnalysisResult {
	public List<CorporateAnalysisBucket> Buckets { get; set; } = [];
	public List<CorporateAnalysisRow> Rows { get; set; } = [];
	/// <summary>件数上限で切る前の行数</summary>
	public int TotalCount { get; set; }
	/// <summary>集計に使った自社締日（99=末締め）</summary>
	public int ShimeBi { get; set; }
	/// <summary>集計に使った期首月(1〜12)</summary>
	public int FiscalStartMonth { get; set; }
}

/// <summary>全社分析の期間区切りの計算。サーバ（集計）とクライアント（入力検査）で共用する。</summary>
public static class CorporateAnalysisPeriod {
	public const int MaxMonths = 36;
	public const int MaxYears = 10;
	public const int MaxDays = 93;
	public const int DefaultLimit = 2000;
	public const int MaxLimit = 10000;

	/// <summary>
	/// 条件から区切り一覧を作る。<paramref name="byBucket"/>=false なら期間全体を1区切り(Key空文字)にまとめる。
	/// 不正な条件は <see cref="ArgumentException"/>。
	/// </summary>
	public static List<CorporateAnalysisBucket> Build(CorporateAnalysisUnit unit, string from, string to, bool byBucket, int shime, int fiscalStartMonth) {
		var list = unit switch {
			CorporateAnalysisUnit.Year => BuildYears(from, to, shime, fiscalStartMonth),
			CorporateAnalysisUnit.Day => BuildDays(from, to),
			_ => BuildMonths(from, to, shime),
		};
		if (byBucket) {
			return list;
		}
		var first = list[0];
		var last = list[^1];
		return [new CorporateAnalysisBucket {
			Key = string.Empty,
			Label = list.Count == 1 ? first.Label : $"{first.Label} 〜 {last.Label}",
			DayFrom = first.DayFrom,
			DayTo = last.DayTo,
			MonthFrom = first.MonthFrom,
			MonthTo = last.MonthTo,
			Days = list.Sum(x => x.Days),
		}];
	}

	static List<CorporateAnalysisBucket> BuildMonths(string from, string to, int shime) {
		var f = ParseMonth(from, "開始年月");
		var t = ParseMonth(to, "終了年月");
		var count = (t.Year - f.Year) * 12 + t.Month - f.Month + 1;
		if (count < 1) throw new ArgumentException("年月の範囲が逆転しています。");
		if (count > MaxMonths) throw new ArgumentException($"月単位は最大{MaxMonths}か月までです。");
		var list = new List<CorporateAnalysisBucket>();
		for (var i = 0; i < count; i++) {
			var ym = f.AddMonths(i).ToString("yyyyMM", CultureInfo.InvariantCulture);
			var p = ClosingMonthCalculator.GetPeriod(ym, shime);
			list.Add(new CorporateAnalysisBucket {
				Key = ym, Label = $"{ym[..4]}/{ym[4..]}",
				DayFrom = p.DayFrom, DayTo = p.DayTo, MonthFrom = ym, MonthTo = ym, Days = DaysOf(p.DayFrom, p.DayTo),
			});
		}
		return list;
	}

	static List<CorporateAnalysisBucket> BuildYears(string from, string to, int shime, int fiscalStartMonth) {
		if (fiscalStartMonth is < 1 or > 12) fiscalStartMonth = 1;
		var f = ParseYear(from, "開始年度");
		var t = ParseYear(to, "終了年度");
		if (t < f) throw new ArgumentException("年度の範囲が逆転しています。");
		if (t - f + 1 > MaxYears) throw new ArgumentException($"年単位は最大{MaxYears}年度までです。");
		var list = new List<CorporateAnalysisBucket>();
		for (var y = f; y <= t; y++) {
			var start = new DateTime(y, fiscalStartMonth, 1);
			var mFrom = start.ToString("yyyyMM", CultureInfo.InvariantCulture);
			var mTo = start.AddMonths(11).ToString("yyyyMM", CultureInfo.InvariantCulture);
			var p = ClosingMonthCalculator.GetPeriodRange(mFrom, mTo, shime);
			list.Add(new CorporateAnalysisBucket {
				Key = mFrom, Label = $"{y}年度({mFrom[..4]}/{mFrom[4..]}〜{mTo[..4]}/{mTo[4..]})",
				DayFrom = p.DayFrom, DayTo = p.DayTo, MonthFrom = mFrom, MonthTo = mTo, Days = DaysOf(p.DayFrom, p.DayTo),
			});
		}
		return list;
	}

	static List<CorporateAnalysisBucket> BuildDays(string from, string to) {
		var f = ParseDay(from, "開始日");
		var t = ParseDay(to, "終了日");
		if (t < f) throw new ArgumentException("日付の範囲が逆転しています。");
		var count = (t - f).Days + 1;
		if (count > MaxDays) throw new ArgumentException($"日単位は最大{MaxDays}日までです。");
		var list = new List<CorporateAnalysisBucket>();
		for (var d = f; d <= t; d = d.AddDays(1)) {
			var key = d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			list.Add(new CorporateAnalysisBucket {
				Key = key, Label = d.ToString("yyyy/MM/dd(ddd)", CultureInfo.GetCultureInfo("ja-JP")),
				DayFrom = key, DayTo = key, Days = 1,
			});
		}
		return list;
	}

	static int DaysOf(string dayFrom, string dayTo) => (ParseDay(dayTo, "終了日") - ParseDay(dayFrom, "開始日")).Days + 1;

	static DateTime ParseMonth(string value, string label) =>
		DateTime.TryParseExact((value ?? string.Empty) + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d : throw new ArgumentException($"{label}が不正です: {value}");

	static int ParseYear(string value, string label) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1900 and <= 2999 && value.Length == 4
			? y : throw new ArgumentException($"{label}が不正です: {value}");

	static DateTime ParseDay(string value, string label) =>
		DateTime.TryParseExact(value ?? string.Empty, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d : throw new ArgumentException($"{label}が不正です: {value}");
}
