namespace CvAsset;

/*
// ジェネリック拡張メソッドの例
extension<T>(List<T> t) {
	public T GenericMethod(T value) => value;
}
null合体演算子（??）	a ?? b（aがnullならb、非nullならa）
null結合代入演算子（??=）	a ??= b（aがnullならbを代入、非nullならaのまま）
null条件メンバーアクセス演算子（?.）	a?.b（aがnullならnull、非nullならbにアクセス）
null条件要素アクセス演算子?[]	a?[b]（aがnullならnull、非nullならa[b]にアクセス）

ReadOnlySpan Span の優先
public static void Method(IEnumerable<int> values) { }
public static void Method(ReadOnlySpan<int> values) { }
*/


/// <summary>
/// 拡張メソッド
/// </summary>
public static class CommonExtentions {
	/// <summary>
	/// DateTimeの拡張メソッド
	/// </summary>
	extension(DateTime date0) {
		/// <summary>
		/// SQLiteでのNPocoからの保存書式に変換する yyyy-MM-dd HH:mm:ss 例)"DenDate between {0} and {1}",dt0.ToSqlDt(),dt1.ToSqlDt()
		/// [Convert to the save format for NPoco in SQLite: yyyy-MM-dd HH:mm:ss Example: "DenDate between {0} and {1}", dt0.ToSqlDt(), dt1.ToSqlDt()]
		/// </summary>
		/// <returns></returns>
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
		/// <returns></returns>
		public string ToDtStrDate() {
			return date0.ToString("yyyy/MM/dd");
		}
		/// <summary>
		/// yyyyMMdd書式へ変換
		/// [Convert to yyyyMMdd format]
		/// </summary>
		/// <returns></returns>
		public string ToDtStrDate2() {
			return date0.ToString("yyyyMMdd");
		}
		/// <summary>
		/// yyyy/MM/dd HH:mm:ss書式へ変換
		/// [Convert to yyyy/MM/dd HH:mm:ss format]
		/// </summary>
		/// <returns></returns>
		public string ToDtStrDateTime() {
			return date0.ToString("yyyy/MM/dd HH:mm:ss");
		}
		/// <summary>
		/// yyyy/MM/dd HH:mm書式へ変換
		/// [Convert to yyyy/MM/dd HH:mm format]
		/// </summary>
		/// <returns></returns>
		public string ToDtStrDateTime2() {
			return date0.ToString("yyyy/MM/dd HH:mm");
		}
		/// <summary>
		/// yyyyMMddHHmmss書式へ変換
		/// [Convert to yyyyMMddHHmmss format]
		/// </summary>
		/// <returns></returns>
		public string ToDtStrDateTimeShort() {
			return date0.ToString("yyyyMMddHHmmss");
		}
		/// <summary>
		/// HH:mm:ss書式へ変換
		/// [Convert to HH:mm:ss format]
		/// </summary>
		/// <returns></returns>
		public string ToDtStrTime() {
			return date0.ToString("HH:mm:ss");
		}
		/// <summary>
		/// 値が初期値か判定する(1901/01/01以前であれば初期値と判断)
		/// [Determine if the value is the default value (considered default if before 1901/01/01)]
		/// </summary>
		/// <returns></returns>
		public bool IsDefault() {
			return date0 < new DateTime(1901, 1, 2); // 念のため時間を考慮して判定 [Consider time for accuracy]
		}
		/// <summary>
		/// 日付部分が同じかどうかを判定
		/// [Determine if the date part is the same]
		/// </summary>
		/// <param name="date1"></param>
		/// <returns></returns>
		public bool IsEqualDate(DateTime date1) {
			return date0.Date == date1.Date;
		}
		/// <summary>
		/// 日付を表す文字列の範囲内に入っているかどうかを判定 d1yyyymmdd - d2yyyymmdd
		/// [Determine if the date is within the range of a string representing a date d1yyyymmdd - d2yyyymmdd]
		/// </summary>
		/// <param name="d1yyyymmdd"></param>
		/// <param name="d2yyyymmdd"></param>
		/// <returns></returns>
		public bool IsOkRange(string d1yyyymmdd, string d2yyyymmdd) {
			var longDate0 = long.Parse(date0.Date.ToDtStrDate2());
			var longD1 = long.Parse(d1yyyymmdd);
			if (longDate0 < longD1)
				return false;
			var longD2 = long.Parse(d2yyyymmdd);
			if (longD2 < longDate0)
				return false;
			return true;
		}
		/// <summary>
		/// UnixTimeを返す (逆は Common.GetUnixTime)
		/// [Return UnixTime]
		/// </summary>
		/// <returns></returns>
		public long ToUnixTime() {
			return (long)(date0.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
		}
		/// <summary>
		/// (閏)xx月xx日 形式の旧暦文字列へ変換
		/// </summary>
		/// <returns></returns>
		public string ToSimpleLunisolarStr() {
			var cal = new System.Globalization.JapaneseLunisolarCalendar();
			int year = cal.GetYear(date0);
			int month = cal.GetMonth(date0);
			int day = cal.GetDayOfMonth(date0);
			int leapMonth = cal.GetLeapMonth(year); // その年の閏月（なければ0）
													// 閏月の判定ロジック
													// このカレンダーでは閏月が独立した番号になるため調整が必要
			bool isLeap = (leapMonth > 0 && month == leapMonth);
			int displayMonth = (leapMonth > 0 && month >= leapMonth) ? month - 1 : month;

			string leapStr = isLeap ? "閏" : "";
			return $"{leapStr}{displayMonth}月{day}日";
		}
	}
	/// <summary>
	/// TimeSpanの拡張メソッド
	/// </summary>
	extension(TimeSpan span0) {
		/// <summary>
		/// わかりやすい文字列として返す
		/// </summary>
		/// <returns></returns>
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
		/// <returns></returns>
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

	}
}
