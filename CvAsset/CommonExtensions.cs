using System.Text.RegularExpressions;

namespace CvAsset;

/// <summary>
/// 拡張メソッド
/// [Extension methods]
/// </summary>
public static class CommonExtensions {
	/// <summary>
	/// DateTimeの拡張メソッド
	/// </summary>
	extension(DateTime date0) {
		/// <summary>
		/// SQLiteでのNPocoからの保存書式に変換する yyyy-MM-dd HH:mm:ss 例)"DenDate between {0} and {1}",dt0.ToSqlDt(),dt1.ToSqlDt()
		/// [Convert to the save format for NPoco in SQLite: yyyy-MM-dd HH:mm:ss Example: "DenDate between {0} and {1}", dt0.ToSqlDt(), dt1.ToSqlDt()]
		/// </summary>
		public string ToSqlDt() {
			// ミリ秒まで見るには、HH:mm:ss.FFFF
			// [To include milliseconds, use HH:mm:ss.FFFF]
			return string.Format("'{0}'", date0.ToString("yyyy-MM-dd HH:mm:ss"));
		}

		public string ToDispStrMDhms() {
			return date0.ToString("M/d HH:mm:ss");
		}
		/// <summary>
		/// yyyy/MM/dd書式へ変換
		/// [Convert to yyyy/MM/dd format]
		/// </summary>
		public string ToDtStrDate() {
			return date0.ToString("yyyy/MM/dd");
		}
		/// <summary>
		/// yyyyMMdd書式へ変換
		/// [Convert to yyyyMMdd format]
		/// </summary>
		public string ToDtStrDate2() {
			return date0.ToString("yyyyMMdd");
		}
		/// <summary>
		/// yyyy/MM/dd HH:mm:ss書式へ変換
		/// [Convert to yyyy/MM/dd HH:mm:ss format]
		/// </summary>
		public string ToDtStrDateTime() {
			return date0.ToString("yyyy/MM/dd HH:mm:ss");
		}
		/// <summary>
		/// yyyy/MM/dd HH:mm書式へ変換
		/// [Convert to yyyy/MM/dd HH:mm format]
		/// </summary>
		public string ToDtStrDateTime2() {
			return date0.ToString("yyyy/MM/dd HH:mm");
		}
		/// <summary>
		/// yyyyMMddHHmmss書式へ変換
		/// [Convert to yyyyMMddHHmmss format]
		/// </summary>
		public string ToDtStrDateTimeShort() {
			return date0.ToString("yyyyMMddHHmmss");
		}
		/// <summary>
		/// HH:mm:ss書式へ変換
		/// [Convert to HH:mm:ss format]
		/// </summary>
		public string ToDtStrTime() {
			return date0.ToString("HH:mm:ss");
		}
		/// <summary>
		/// 値が初期値か判定する(1901/01/01以前であれば初期値と判断)
		/// [Determine if the value is the default value (considered default if before 1901/01/01)]
		/// </summary>
		public bool IsDefault() {
			return date0 < DefaultDateLimit; // 念のため時間を考慮して判定 [Consider time for accuracy]
		}
		/// <summary>
		/// 日付部分が同じかどうかを判定
		/// [Determine if the date part is the same]
		/// </summary>
		public bool IsEqualDate(DateTime date1) {
			return date0.Date == date1.Date;
		}
		/// <summary>
		/// 日付を表す文字列の範囲内に入っているかどうかを判定 d1yyyymmdd - d2yyyymmdd
		/// [Determine if the date is within the range of a string representing a date d1yyyymmdd - d2yyyymmdd]
		/// </summary>
		public bool IsOkRange(string d1yyyymmdd, string d2yyyymmdd) {
			if (!long.TryParse(date0.Date.ToDtStrDate2(), out var longDate0)) return false;
			if (!long.TryParse(d1yyyymmdd, out var longD1)) return false;
			if (!long.TryParse(d2yyyymmdd, out var longD2)) return false;
			return longDate0 >= longD1 && longDate0 <= longD2;
		}
		/// <summary>
		/// UnixTime（秒）を返す
		/// [Return UnixTime in seconds]
		/// </summary>
		/// <returns>UnixTime（秒） [UnixTime in seconds]</returns>
		public long ToUnixTime() {
			return (long)(date0.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
		}
		/// <summary>
		/// (閏)睦月朔日 のような和風月名・日名の旧暦文字列へ変換
		/// </summary>
		public string ToSimpleLunisolarStr() {
			var (isLeap, month, day) = GetLunisolarParts(date0);
			return $"{(isLeap ? "閏" : "")}{GetLunaMonthName(month)}{GetLunaDayName(day)}";
		}
		/// <summary>
		/// (閏)x月x日 形式（数字）の旧暦文字列へ変換
		/// </summary>
		public string ToSimpleLunisolarStrDigit() {
			var (isLeap, month, day) = GetLunisolarParts(date0);
			return $"{(isLeap ? "閏" : "")}{month}月{day}日";
		}
		/// <summary>
		/// 旧暦の名前を返す
		/// </summary>
		private static string GetLunaMonthName(int month) => month switch {
			1 => "睦月",
			2 => "如月",
			3 => "弥生",
			4 => "卯月",
			5 => "皐月",
			6 => "水無月",
			7 => "文月",
			8 => "葉月",
			9 => "長月",
			10 => "神無月",
			11 => "霜月",
			12 => "師走",
			_ => ""
		};
		/// <summary>
		/// 旧暦の日の名前を返す
		/// </summary>
		private static string GetLunaDayName(int day) => day switch {
			1 => "朔日",
			2 => "二日",
			3 => "三日",
			4 => "四日",
			5 => "五日",
			6 => "六日",
			7 => "七日",
			8 => "八日",
			9 => "九日",
			10 => "十日",
			11 => "十一日",
			12 => "十二日",
			13 => "十三日",
			14 => "十四日",
			15 => "十五日",
			16 => "十六日",
			17 => "十七日",
			18 => "十八日",
			19 => "十九日",
			20 => "二十日",
			21 => "廿一日",
			22 => "廿二日",
			23 => "廿三日",
			24 => "廿四日",
			25 => "廿五日",
			26 => "廿六日",
			27 => "廿七日",
			28 => "廿八日",
			29 => "廿九日",
			30 => "晦日",
			_ => ""
		};
		/// <summary>
		/// CRONの書式に合わせて、秒とミリ秒を切り捨てたDateTimeを返す
		/// </summary>
		public DateTime ToAdjustCronDateTime() {
			return new DateTime(date0.Year, date0.Month, date0.Day, date0.Hour, date0.Minute, 0);
		}

	}
	/// <summary>
	/// TimeSpanの拡張メソッド
	/// </summary>
	extension(TimeSpan span0) {
		/// <summary>
		/// わかりやすい文字列として返す
		/// </summary>
		public string ToStrSpan() {
			var date0 = new DateTime(0).Add(span0);
			if (span0.Days > 0)
				return date0.ToString("d日H時間m分s.FFF秒");
			else if (span0.Hours > 0)
				return date0.ToString("H時間m分s.FFF秒");
			else if (span0.Minutes > 0)
				return date0.ToString("m分s.FFF秒");
			else
				return date0.ToString("s.FFF秒");
		}
	}
	/// <summary>
	/// Stringの拡張メソッド
	/// </summary>
	extension(string str) {
		/// <summary>
		/// yyyyMM から yyyyMMddHHmmssの文字列を/と:で見た目を整える
		/// </summary>
		public string ToDateStr() {
			if (string.IsNullOrWhiteSpace(str)) return "";
			if (str.Length < 6) return "";
			if (str.Length < 8)
				return $"{str[..4]}/{str[4..6]}"; // yyyy/MM
			if (str.Length < 10)
				return $"{str[..4]}/{str[4..6]}/{str[6..8]}"; // yyyy/MM/dd
			if (str.Length < 12) // yyyy/MM/dd HH:00:00
				return $"{str[..4]}/{str[4..6]}/{str[6..8]} {str[8..10]}:00:00";
			if (str.Length < 14) // yyyy/MM/dd HH:mm:00
				return $"{str[..4]}/{str[4..6]}/{str[6..8]} {str[8..10]}:{str[10..12]}:00";
			return $"{str[..4]}/{str[4..6]}/{str[6..8]} {str[8..10]}:{str[10..12]}:{str[12..14]}"; // yyyy/MM/dd HH:mm:ss
		}
		public string DefaultIfEmpty(string defaultValue)
			=> string.IsNullOrEmpty(str) ? defaultValue : str;

		/// <summary>
		/// SqlDepends: __serverdate__() と __serverimg__() / __serverimgshain__() で記述されたSQL文の部分を、それぞれ日付式と画像パスへ変換する
		/// </summary>
		public string ReplaceServerSqlQuery() {
			var replaced = ServerDateRegex.Replace(str,
				match => $"strftime('%Y%m%d%H%M%S',datetime(({match.Groups[1].Value} - 621355968000000000) / 10000000, 'unixepoch','localtime'))");
			var imgReplaced = ReplaceServerImg(replaced, ServerImgLiteralRegex, ServerImgExpressionRegex, "img");
			return ReplaceServerImg(imgReplaced, ServerImgshainLiteralRegex, ServerImgshainExpressionRegex, "imgshain");
		}
	}
	/// <summary>
	/// 画像名のリテラル指定・式指定を、folder 配下の画像パスへ変換する（リテラルを先に処理する）
	/// </summary>
	private static string ReplaceServerImg(string sql, Regex literalRegex, Regex expressionRegex, string folder) {
		var literalReplaced = literalRegex.Replace(sql, match => $"'{folder}/{match.Groups[1].Value}.jpg'");
		return expressionRegex.Replace(literalReplaced, match => {
			var imageNameExpression = match.Groups[1].Value.Trim();
			return $"case when ifnull({imageNameExpression}, '') = '' then '' else '{folder}/' || {imageNameExpression} || '.jpg' end";
		});
	}
	/// <summary>
	/// 旧暦の閏月かどうか・表示月・日を返す
	/// </summary>
	private static (bool IsLeap, int Month, int Day) GetLunisolarParts(DateTime date) {
		var cal = new System.Globalization.JapaneseLunisolarCalendar();
		int year = cal.GetYear(date);
		int month = cal.GetMonth(date);
		int day = cal.GetDayOfMonth(date);
		int leapMonth = cal.GetLeapMonth(year); // その年の閏月（なければ0）
		// このカレンダーでは閏月が独立した番号になるため、閏月以降は月番号を1つ戻す
		bool isLeap = leapMonth > 0 && month == leapMonth;
		int displayMonth = (leapMonth > 0 && month >= leapMonth) ? month - 1 : month;
		return (isLeap, displayMonth, day);
	}
	/// <summary>初期値判定の境界（1901/01/02 より前を初期値とみなす）</summary>
	private static readonly DateTime DefaultDateLimit = new(1901, 1, 2);
	private static readonly Regex ServerDateRegex = new Regex(@"__serverdate__\(([^)]+)\)", RegexOptions.Compiled);
	private static readonly Regex ServerImgLiteralRegex = new Regex(@"__serverimg__\('([^']+)'\)", RegexOptions.Compiled);
	private static readonly Regex ServerImgExpressionRegex = new Regex(@"__serverimg__\(([^)]+)\)", RegexOptions.Compiled);
	private static readonly Regex ServerImgshainLiteralRegex = new Regex(@"__serverimgshain__\('([^']+)'\)", RegexOptions.Compiled);
	private static readonly Regex ServerImgshainExpressionRegex = new Regex(@"__serverimgshain__\(([^)]+)\)", RegexOptions.Compiled);
}

public static class DynamicCsvExtensions {
	public static void WriteDynamicCsv<T>(this IEnumerable<T> records, TextWriter writer, bool includeHeader = false)
		where T : IDictionary<string, object> {
		using var enumerator = records.GetEnumerator();
		if (!enumerator.MoveNext())
			return;

		var first = enumerator.Current;
		if (includeHeader) {
			writer.WriteLine(CsvText.BuildLine(first.Keys));
		}

		do {
			var line = string.Join(",", enumerator.Current.Values.Select(v => CsvText.EscapeField(v?.ToString())));
			writer.WriteLine(line);
		} while (enumerator.MoveNext());
	}
}
