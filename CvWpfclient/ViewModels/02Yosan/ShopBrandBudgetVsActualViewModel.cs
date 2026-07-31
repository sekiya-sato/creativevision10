using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 店舗ブランド別予算実績対比。指定年月を月単位で集計し、店舗×ブランドの売上予算/実績・粗利予算/実績を対比する。
/// 予算は MasterYosanBrand(店舗×ブランド×日)を月合計。
/// 実績は Tran01Tenuri の明細(Jmeisai)を展開し、明細の商品Idから MasterShohin.Id_Brand を引いてブランドへ寄せる。
/// 粗利実績は「明細金額 - 明細下代」で算出する（下代=原価相当）。
/// </summary>
public partial class ShopBrandBudgetVsActualViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "店舗ブランド別予算実績対比";
	protected override string FormFileName => "ShopBrandBudgetVsActual.qfm";

	/// <summary>明細JSON(Jmeisai)からの値取り出し。ShiireSlipPrint と同じ抽出規則。</summary>
	const string M = "json_extract(m.value,";

	/// <summary>集計単位。</summary>
	public enum SummaryLevel {
		/// <summary>店舗×ブランド</summary>
		ShopBrand,
		/// <summary>ブランド計（全店合計）</summary>
		BrandOnly,
		/// <summary>店舗計（全ブランド合計）</summary>
		ShopOnly,
	}

	[ObservableProperty]
	public partial DateTime SelectedYearMonth { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial string SelectedYearMonthString { get; set; } = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	/// <summary>店舗×ブランドで出力する</summary>
	[ObservableProperty]
	public partial bool IsShopBrand { get; set; } = true;

	/// <summary>ブランド計（全店合計）で出力する</summary>
	[ObservableProperty]
	public partial bool IsBrandOnly { get; set; }

	/// <summary>店舗計（全ブランド合計）で出力する</summary>
	[ObservableProperty]
	public partial bool IsShopOnly { get; set; }

	/// <summary>出力対象。false=全て / true=予算または実績があるものだけ。</summary>
	[ObservableProperty]
	public partial bool IsActiveOnly { get; set; } = true;

	SummaryLevel Level =>
		IsBrandOnly ? SummaryLevel.BrandOnly :
		IsShopOnly ? SummaryLevel.ShopOnly :
		SummaryLevel.ShopBrand;

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

	[RelayCommand]
	void SelectBrandCodeFrom() {
		BrandCodeFrom = SelectBrandCode() ?? BrandCodeFrom;
	}

	[RelayCommand]
	void SelectBrandCodeTo() {
		BrandCodeTo = SelectBrandCode() ?? BrandCodeTo;
	}

	/// <summary>ブランド選択ダイアログ(MasterMeisho の BRD 区分)。選択されなければ null</summary>
	string? SelectBrandCode() =>
		ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseYearMonth(SelectedYearMonthString, out var yearMonth)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		SelectedYearMonth = yearMonth;
		ct.ThrowIfCancellationRequested();

		var (dateFrom, dateTo) = GetMonthRange(SelectedYearMonth);
		var yearMonthLabel = SelectedYearMonth.ToString("yy年MM月", CultureInfo.InvariantCulture);

		var level = Level;
		List<string> parameters = [];
		var shopWhere = BuildCodeRangeWhere(parameters, "Code", ShopCodeFrom, ShopCodeTo);
		// 店舗計（全ブランド合計）ではブランドを潰すため、ブランド範囲指定は適用しない。
		var brandWhere = level == SummaryLevel.ShopOnly
			? ""
			: BuildCodeRangeWhere(parameters, "Code", BrandCodeFrom, BrandCodeTo);

		// 集計キーは出力区分で変わる。店舗計ならブランドを、ブランド計なら店舗を潰す。
		var (shopKey, shopName, brandKey, brandName) = level switch {
			SummaryLevel.BrandOnly => ("''", "'全店'", "br.Code", "br.Name"),
			SummaryLevel.ShopOnly => ("sh.Code", "sh.Name", "''", "'全ﾌﾞﾗﾝﾄﾞ'"),
			_ => ("sh.Code", "sh.Name", "br.Code", "br.Name"),
		};

		// 店舗計はブランド不明（商品マスタ未一致）の売上も店舗合計へ含めたいので LEFT JOIN。
		// 店舗×ブランド／ブランド計はブランド範囲指定を効かせる必要があるので INNER JOIN。
		var brandJoin = level == SummaryLevel.ShopOnly ? "LEFT JOIN" : "JOIN";

		var activeOnlyWhere = IsActiveOnly
			? "WHERE uriYosan != 0 OR uriJisseki != 0 OR arariYosan != 0 OR arariJisseki != 0"
			: "";

		// 予算・実績のどちらか片方しか無い組合せも出したいので、キー集合を UNION で作ってから両者を突き合わせる。
		// dateFrom/dateTo は SelectedYearMonth 由来の yyyyMMdd 文字列でユーザ入力を含まないため直接埋め込む。
		var sql = $@"
WITH shops AS (
    SELECT Id, Code, Name FROM MasterTokui
    WHERE TenType = 6 {shopWhere}
),
brands AS (
    SELECT Id, Code, Name FROM MasterMeisho
    WHERE Kubun = 'BRD' {brandWhere}
),
budget AS (
    SELECT Id_Tenpo AS idTenpo, Id_Brand AS idBrand,
           SUM(UriYosan) AS uriYosan,
           SUM(ArariYosan) AS arariYosan
    FROM MasterYosanBrand
    WHERE DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
    GROUP BY Id_Tenpo, Id_Brand
),
actual_meisai AS (
    -- 明細を1行ずつ展開し、商品Idからブランドを相関副問合せで引く。
    -- json_valid で不正JSONをガードする（空文字などに json_extract を当てると例外になる）。
    SELECT
        h.Id_Tenpo AS idTenpo,
        COALESCE((
            SELECT s.Id_Brand FROM MasterShohin s
            WHERE s.Id = CAST(COALESCE({M}'$.Id_Shohin'), 0) AS INTEGER)
        ), 0) AS idBrand,
        CAST(COALESCE({M}'$.Kingaku'), 0) AS INTEGER) AS kingaku,
        CAST(COALESCE({M}'$.Gedai'), 0) AS INTEGER) AS gedai
    FROM Tran01Tenuri h, json_each(h.Jmeisai) m
    WHERE h.DenDay BETWEEN '{dateFrom}' AND '{dateTo}'
      AND json_valid(h.Jmeisai)
),
actual AS (
    SELECT
        idTenpo, idBrand,
        SUM(kingaku) AS uriJisseki,
        SUM(kingaku - gedai) AS arariJisseki
    FROM actual_meisai
    GROUP BY idTenpo, idBrand
),
keys AS (
    SELECT idTenpo, idBrand FROM budget
    UNION
    SELECT idTenpo, idBrand FROM actual
),
joined AS (
    SELECT
        {shopKey} AS shopCode,
        {shopName} AS shopName,
        {brandKey} AS brandCode,
        {brandName} AS brandName,
        COALESCE(b.uriYosan, 0) AS uriYosan,
        COALESCE(b.arariYosan, 0) AS arariYosan,
        COALESCE(a.uriJisseki, 0) AS uriJisseki,
        COALESCE(a.arariJisseki, 0) AS arariJisseki
    FROM keys k
    JOIN shops sh ON sh.Id = k.idTenpo
    {brandJoin} brands br ON br.Id = k.idBrand
    LEFT JOIN budget b ON b.idTenpo = k.idTenpo AND b.idBrand = k.idBrand
    LEFT JOIN actual a ON a.idTenpo = k.idTenpo AND a.idBrand = k.idBrand
),
grouped AS (
    SELECT
        shopCode, shopName, brandCode, brandName,
        SUM(uriYosan) AS uriYosan,
        SUM(arariYosan) AS arariYosan,
        SUM(uriJisseki) AS uriJisseki,
        SUM(arariJisseki) AS arariJisseki
    FROM joined
    GROUP BY shopCode, shopName, brandCode, brandName
)
SELECT
    '{yearMonthLabel}' AS yearMonth,
    shopCode, shopName,
    brandCode, brandName,
    uriYosan AS uriBudget,
    uriJisseki AS uriActual,
    uriJisseki - uriYosan AS uriDiff,
    CASE WHEN uriYosan != 0
         THEN ROUND(CAST(uriJisseki AS REAL) / uriYosan * 100, 1)
         ELSE 0 END AS uriRatio,
    arariYosan AS arariBudget,
    arariJisseki AS arariActual,
    CASE WHEN arariYosan != 0
         THEN ROUND(CAST(arariJisseki AS REAL) / arariYosan * 100, 1)
         ELSE 0 END AS arariRatio
FROM grouped
{activeOnlyWhere}
ORDER BY shopCode, brandCode";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
