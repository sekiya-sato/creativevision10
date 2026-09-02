using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 店舗予算表（旧CVnet「予算 : 予算表」 program_crs / isqlqfw00.aspx の "_30_yosan" 相当）。
///
/// 旧システムは HC$WKS_HAN01 というワークテーブルへ「日付×店舗×ブランド」の器を作り、
/// 売上・予算・客数・前年売上を順に UPDATE で貼り付けてから仕上げQUERYを流していた。
/// cv10 では同じ組み立てを CTE (calendar → shops → budget/sales → han01 → cum) で再現している。
/// 帳票は printform/ShopBudgetReport.qfm（旧 cvnet30prn_yosan.qfm のコピー）で、
/// item1〜item27 が下記 SELECT の27列と1対1に対応するため、列の順序と個数は変更しないこと。
///
/// 旧システムに存在するが cv10 に対応するテーブル・列がないものは空欄/0固定にしている。
/// ・社販売上 … 旧 取引区分 mod 10 = 4。cv10 の Tran01Tenuri.Kubun には社販がないため常に0
/// ・客数     … 旧 HC$TRAN_KYAKU。cv10 相当の Tran04PosSeisan.KyakuSu は未運用のため常に0
/// ・既存比   … 旧 得意先マスタ「開始日」から既存店/新店を判定。cv10 の MasterTokui に開店日相当がないため空欄
/// ・商品外除外 … 旧 商品区分FLG=0 の明細絞り込み。cv10 は売上明細がJSON列のため集計に使えず未対応
/// </summary>
public partial class ShopBudgetReportViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店舗予算表";
	protected override string FormFileName => "ShopBudgetReport.qfm";

	/// <summary>曜日名。旧 DECODE(CD03,'1','日',...) と同じ並び（SQLite strftime('%w') は 0=日）</summary>
	static readonly string[] YoubiNames = ["日", "月", "火", "水", "木", "金", "土"];

	/// <summary>qfm の日付フィールドが解釈できる書式。yyyyMMdd では日付として読まれず空白印字になる</summary>
	const string QfmDateFormat = "yyyy/MM/dd";

	/// <summary>見出しの前年開始日・終了日の書式（旧 st_dt.ToString("yyyy/MM/dd(ddd)") と同じ）</summary>
	const string PrevDateLabelFormat = "yyyy/MM/dd(ddd)";

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>出力区分。true=店舗別（旧 OptionButton2=0） / false=全店（旧 OptionButton2=3）</summary>
	[ObservableProperty]
	public partial bool IsByShop { get; set; } = true;

	/// <summary>前年比。true=日付対比（旧 WeekFlg=0） / false=曜日対比（旧 WeekFlg=1）</summary>
	[ObservableProperty]
	public partial bool IsDateComparison { get; set; } = true;

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	[RelayCommand]
	void SelectShopCodeFrom() {
		ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;
	}

	[RelayCommand]
	void SelectShopCodeTo() {
		ShopCodeTo = SelectShopCode() ?? ShopCodeTo;
	}

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(SelectedYearMonthString, out var yearMonth)) return Task.FromResult<QueryListSqlParam?>(null);
		SelectedYearMonth = yearMonth;
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(SelectedYearMonth);
		var daysInMonth = DateTime.DaysInMonth(SelectedYearMonth.Year, SelectedYearMonth.Month);
		var monthStart = new DateTime(SelectedYearMonth.Year, SelectedYearMonth.Month, 1);
		var monthEnd = new DateTime(SelectedYearMonth.Year, SelectedYearMonth.Month, daysInMonth);

		// 旧: 見出しの前年開始日・終了日は MIN/MAX(CD98)（日付対比の終了日だけは月末の12ヶ月前）。
		// 日ごとの前年日付と同じ式なので、ここで直接算出して埋め込む。
		var jp = CultureInfo.GetCultureInfo("ja-JP");
		var prevStart = PrevYearDate(monthStart, IsDateComparison) ?? monthStart.AddMonths(-12);
		var prevEnd = IsDateComparison ? monthEnd.AddMonths(-12) : PrevYearDate(monthEnd, false)!.Value;

		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "t.Code", ShopCodeFrom, ShopCodeTo);

		// dateFrom/dateTo とカレンダーは SelectedYearMonth 由来でユーザ入力を含まないため直接埋め込む。
		var sql = $@"
WITH calendar(denDay, dayValue, youbi, prevDenDay, prevExtraDenDay, prevDayLabel) AS (
{BuildCalendarValues(monthStart, daysInMonth)}
),
shops AS (
    -- 旧: HC$MASTER_YO_TENPO を店舗でGROUP BYし、得意先マスタに存在するものだけを出力対象にする
    --     （画面の「※予算を組んだ店舗のみを出力します」に対応）
    SELECT t.Id, t.Code, t.Name
    FROM MasterTokui t
    WHERE t.TenType = 6 {shopWhere}
      AND EXISTS (
          SELECT 1 FROM MasterYosanBrand y
          WHERE y.Id_Tenpo = t.Id AND y.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
      )
),
budget AS (
    -- 旧3: 予算くっつけ SU00（cv10 はブランド別予算をブランド横断で合計する）
    SELECT Id_Tenpo, DenDay, SUM(UriYosan) AS su00
    FROM MasterYosanBrand
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
    GROUP BY Id_Tenpo, DenDay
),
tenuri AS (
    -- 当年と前年の突き合わせ日をまとめて1回だけ読む。
    -- sgn は旧 DECODE(TRUNC(取引区分/10),1,1,2,-1,0)（10番台=売上/20番台=返品）。
    -- 整数除算はDB間で意味差が出るため、範囲比較で同じ判定にしている。
    SELECT Id_Tenpo, DenDay, Kubun, KingakuTotal, SuTotal,
           CASE WHEN Kubun BETWEEN 10 AND 19 THEN 1 WHEN Kubun BETWEEN 20 AND 29 THEN -1 ELSE 0 END AS sgn
    FROM Tran01Tenuri
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
       OR DenDay IN (SELECT prevDenDay FROM calendar WHERE prevDenDay IS NOT NULL
                     UNION SELECT prevExtraDenDay FROM calendar WHERE prevExtraDenDay IS NOT NULL)
),
sales AS (
    -- 旧2: 売上金額・区分別売上金額くっつけ SU01〜SU05,SU20
    SELECT Id_Tenpo, DenDay,
        SUM(CASE WHEN Kubun % 10 = 4 THEN 0 ELSE sgn * KingakuTotal END) AS su01,
        SUM(CASE WHEN Kubun % 10 = 4 THEN sgn * KingakuTotal ELSE 0 END) AS su02,
        SUM(CASE WHEN Kubun % 10 = 0 THEN sgn * KingakuTotal ELSE 0 END) AS su03,
        SUM(CASE WHEN Kubun % 10 = 1 THEN sgn * KingakuTotal ELSE 0 END) AS su04,
        SUM(sgn * KingakuTotal) AS su05,
        SUM(sgn * SuTotal) AS su20
    FROM tenuri
    GROUP BY Id_Tenpo, DenDay
),
han01 AS (
    -- 旧1: HC$WKS_HAN01 の器（日付×店舗）。予算のない日も行を作るため CROSS JOIN する。
    -- 旧5+#32839: 前年売上 SU07 は前年同日 + （前年が閏年で2/28に当たる場合の2/29分）
    SELECT
        s.Code, s.Name, c.denDay, c.dayValue, c.youbi, c.prevDayLabel,
        CASE WHEN c.prevDenDay IS NULL THEN 0 ELSE 1 END AS prevValid,
        COALESCE(b.su00, 0) AS su00,
        COALESCE(sa.su01, 0) AS su01,
        COALESCE(sa.su02, 0) AS su02,
        COALESCE(sa.su03, 0) AS su03,
        COALESCE(sa.su04, 0) AS su04,
        COALESCE(sa.su05, 0) AS su05,
        COALESCE(sa.su20, 0) AS su20,
        0 AS su06,
        COALESCE(p1.su01, 0) + COALESCE(p2.su01, 0) AS su07raw
    FROM shops s
    CROSS JOIN calendar c
    LEFT JOIN budget b ON b.Id_Tenpo = s.Id AND b.DenDay = c.denDay
    LEFT JOIN sales sa ON sa.Id_Tenpo = s.Id AND sa.DenDay = c.denDay
    LEFT JOIN sales p1 ON p1.Id_Tenpo = s.Id AND p1.DenDay = c.prevDenDay
    LEFT JOIN sales p2 ON p2.Id_Tenpo = s.Id AND p2.DenDay = c.prevExtraDenDay
),
cum AS (
    -- 旧6: 累計計算 SU11(売上累計)/SU10(予算累計・円)/SU17(前年売上累計)
    SELECT h.*,
        SUM(su01) OVER (PARTITION BY Code ORDER BY denDay) AS su11,
        SUM(su00) OVER (PARTITION BY Code ORDER BY denDay) AS su10,
        SUM(su07raw) OVER (PARTITION BY Code ORDER BY denDay) AS su17raw
    FROM han01 h
)";

		// 旧8: 仕上げQUERY。列順は qfm の item1〜item27 に対応する。
		// 前年比は旧 comp_str00（日次: 売上/前年売上）。旧クライアントは wrk_para[8] を空文字で渡すため累計版は使われない。
		// 率は旧 TRUNC(x*100,1) に合わせ、1000倍してINTEGERへ切り捨てて10で割る（SQLiteのCASTは0方向切り捨て）。
		if (IsByShop) {
			sql += $@"
SELECT
    '{monthStart.ToString(QfmDateFormat, CultureInfo.InvariantCulture)}' AS nengetsu,
    0 AS tsukiYosan,
    Code AS tenpoCode,
    Name AS tenpoName,
    'zzzzzzzzzz' AS brandCode,
    '合計' AS brandName,
    CASE WHEN prevValid = 1 THEN su07raw ELSE 0 END AS zennenUri,
    CASE WHEN prevValid = 1 THEN su17raw ELSE 0 END AS zennenRui,
    CASE WHEN prevValid = 1 AND su07raw <> 0
         THEN CAST(su01 * 1000.0 / su07raw AS INTEGER) / 10.0 ELSE 0 END AS zennenHi,
    dayValue AS hizuke,
    youbi,
    su01 AS uriage,
    su11 AS uriageRui,
    CAST(su00 / 1000.0 AS INTEGER) AS yosanSen,
    CAST(su10 / 1000.0 AS INTEGER) AS yosanRuiSen,
    su11 - su10 AS yosanSai,
    CASE WHEN su10 <> 0 THEN CAST(su11 * 1000.0 / su10 AS INTEGER) / 10.0 ELSE 0 END AS yosanHi,
    su02 AS shahanUri,
    su03 AS properUri,
    su04 AS saleUri,
    su05 AS souUri,
    '' AS kizonHi,
    su06 AS kyakusu,
    su20 AS uriTensu,
    prevDayLabel AS zennenHizuke,
    '{prevStart.ToString(PrevDateLabelFormat, jp)}' AS zennenFrom,
    '{prevEnd.ToString(PrevDateLabelFormat, jp)}' AS zennenTo
FROM cum
ORDER BY Code, denDay";
		}
		else {
			sql += $@"
SELECT
    '{monthStart.ToString(QfmDateFormat, CultureInfo.InvariantCulture)}' AS nengetsu,
    0 AS tsukiYosan,
    '' AS tenpoCode,
    '全店' AS tenpoName,
    '' AS brandCode,
    '' AS brandName,
    SUM(CASE WHEN prevValid = 1 THEN su07raw ELSE 0 END) AS zennenUri,
    SUM(CASE WHEN prevValid = 1 THEN su17raw ELSE 0 END) AS zennenRui,
    CASE WHEN SUM(CASE WHEN prevValid = 1 THEN su07raw ELSE 0 END) <> 0
         THEN CAST(SUM(su01) * 1000.0 / SUM(CASE WHEN prevValid = 1 THEN su07raw ELSE 0 END) AS INTEGER) / 10.0
         ELSE 0 END AS zennenHi,
    MAX(dayValue) AS hizuke,
    MAX(youbi) AS youbi,
    SUM(su01) AS uriage,
    SUM(su11) AS uriageRui,
    SUM(CAST(su00 / 1000.0 AS INTEGER)) AS yosanSen,
    SUM(CAST(su10 / 1000.0 AS INTEGER)) AS yosanRuiSen,
    SUM(su11 - su10) AS yosanSai,
    CASE WHEN SUM(su10) <> 0 THEN CAST(SUM(su11) * 1000.0 / SUM(su10) AS INTEGER) / 10.0 ELSE 0 END AS yosanHi,
    SUM(su02) AS shahanUri,
    SUM(su03) AS properUri,
    SUM(su04) AS saleUri,
    SUM(su05) AS souUri,
    '' AS kizonHi,
    SUM(su06) AS kyakusu,
    SUM(su20) AS uriTensu,
    MAX(prevDayLabel) AS zennenHizuke,
    '{prevStart.ToString(PrevDateLabelFormat, jp)}' AS zennenFrom,
    '{prevEnd.ToString(PrevDateLabelFormat, jp)}' AS zennenTo
FROM cum
GROUP BY denDay
ORDER BY denDay";
		}

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}

	/// <summary>
	/// 旧 HC$WKS_HAN01 の日付側（CD00/CD03/CD98）を VALUES 句として組み立てる。
	/// 前年日付は閏年・曜日対比でOracle関数依存の分岐が入るため、SQLiteで再現せずC#側で確定させる。
	/// </summary>
	string BuildCalendarValues(DateTime monthStart, int daysInMonth) {
		var jp = CultureInfo.GetCultureInfo("ja-JP");
		var sb = new StringBuilder();
		for (var day = 0; day < daysInMonth; day++) {
			var date = monthStart.AddDays(day);
			var prev = PrevYearDate(date, IsDateComparison);
			// #32839: 前年が閏年で前年日付が2/28のとき、前年2/29分を2/28に含める（日付対比のみ）
			var prevExtra = IsDateComparison && prev is { Month: 2, Day: 28 } && DateTime.IsLeapYear(prev.Value.Year)
				? prev.Value.AddDays(1)
				: (DateTime?)null;
			sb.Append(day == 0 ? "    VALUES (" : ",\n           (");
			sb.Append(CultureInfo.InvariantCulture, $"'{date:yyyyMMdd}'");
			sb.Append(CultureInfo.InvariantCulture, $",'{date.ToString(QfmDateFormat, CultureInfo.InvariantCulture)}'");
			sb.Append(CultureInfo.InvariantCulture, $",'{YoubiNames[(int)date.DayOfWeek]}'");
			sb.Append(prev is null ? ",NULL" : $",'{prev.Value:yyyyMMdd}'");
			sb.Append(prevExtra is null ? ",NULL" : $",'{prevExtra.Value:yyyyMMdd}'");
			sb.Append(prev is null ? ",''" : $",'{prev.Value.ToString(PrevDateLabelFormat, jp)}'");
			sb.Append(')');
		}
		return sb.ToString();
	}

	/// <summary>
	/// 旧 CD98（前年突き合わせ日）を求める。
	/// 日付対比は前年の同月同日（閏年で存在しない 2/29 は旧 IS_DATE 判定で対象外になるため null）。
	/// 曜日対比は旧 NEXT_DAY(ADD_ADJMONTH(日付,-12), 日付の曜日) で、12ヶ月前の翌日以降で最初の同曜日。
	/// </summary>
	static DateTime? PrevYearDate(DateTime date, bool isDateComparison) {
		if (isDateComparison) {
			var year = date.Year - 1;
			return date.Day > DateTime.DaysInMonth(year, date.Month) ? null : new DateTime(year, date.Month, date.Day);
		}
		var baseDate = date.AddMonths(-12);
		var offset = (((int)date.DayOfWeek - (int)baseDate.DayOfWeek + 6) % 7) + 1;
		return baseDate.AddDays(offset);
	}
}
