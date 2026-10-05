using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHT 出荷指示明細書印刷（旧 SubDlg_08prn_hht02。旧は HC$TRAN_STDHHT を印刷する仮画面）。
/// <para>
/// CV10 では受信済み HHT データ（TranVulcanHht）を区分・日付・店舗・伝票NOで絞り、伝票NO単位の明細書にする。
/// 帳票は移動明細書と同じ IdoDetailBook.qfm（旧 cvnet60prn02.qfm）を使い、SELECT は34列の並びで出す。
/// 出庫元/入庫先の向きは HHT データ更新（HhtProcessUpdateMap）の伝票作成と揃える。
/// 印刷済みの管理はしない（旧画面にも更新処理はない）。
/// </para>
/// </summary>
public sealed partial class ShippingConfirmDetailPrintViewModel : BaseReportViewModel {
	protected override string ReportTitle => "出荷指示明細書印刷";

	protected override string? FormFileName => BaseIdoDetailBookPrintViewModel<Tran10IdoOut>.IdoDetailBookForm;

	public sealed record KubunOption(int Value, string Name);

	/// <summary>対象の区分（TranVulcanHht.Type0）。売上・棚卸・客数など物の移動を伴わない区分は対象外</summary>
	static readonly KubunOption[] TargetKubun = [
		new(3, "入庫"), new(4, "出庫"), new(5, "仕入"), new(6, "仕入返品"), new(11, "移動"),
	];

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [new(-1, "すべて"), .. TargetKubun];

	[ObservableProperty]
	public partial int SelectedKubun { get; set; } = -1;

	[ObservableProperty]
	public partial string ShopCode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = Today();

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = Today();

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	static string Today() => DateTime.Today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[RelayCommand]
	void SelectShop() => ShopCode = SelectTokuiCode() ?? ShopCode;

	[RelayCommand]
	void ClearConditions() {
		SelectedKubun = -1;
		ShopCode = DenNoFrom = DenNoTo = string.Empty;
		DenDayFrom = DenDayTo = Today();
		Message = "検索条件をクリアしました";
	}

	/// <summary>
	/// 前0を除いたコード。HHT の店舗/取引先/担当者は前0埋めで、マスタのコードと照合するときに揃える
	/// （HhtProcessUpdateMap.NormalizeCode と同じ扱い）。ltrim の2引数形は MariaDB に無いため replace で書く。
	/// </summary>
	static string NoZero(string column) => $"replace(ltrim(replace(trim({column}),'0',' ')),' ','0')";

	static string TokuiName(string column) =>
		$"ifnull((select t.Name from MasterTokui t where {NoZero("t.Code")} = {NoZero(column)} limit 1),'')";

	static string ShiireName(string column) =>
		$"ifnull((select s.Name from MasterShiire s where {NoZero("s.Code")} = {NoZero(column)} limit 1),'')";

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("日付の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}

		List<string> parameters = [];
		var kubunList = SelectedKubun >= 0 ? SelectedKubun.ToString(CultureInfo.InvariantCulture) : string.Join(",", TargetKubun.Select(k => k.Value));
		var where = $"v.Type0 IN ({kubunList})"
			+ $" AND v.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))} AND v.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		if (!string.IsNullOrWhiteSpace(ShopCode)) {
			where += $" AND {NoZero("v.Shop")} = {NoZero(AddSqlParameter(parameters, ShopCode.Trim()))}";
		}
		const string DenNo = "cast(trim(v.DenNo) as integer)";
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND {DenNo} >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND {DenNo} <= {noTo}";

		var kubunName = TranMeisaiSql.KubunLabel("v.Type0", [.. TargetKubun.Select(k => (k.Value, k.Name))]);
		// 出庫元: 入庫(3)・仕入(5)は取引先、それ以外は店舗 / 入庫先: 入庫(3)・仕入(5)は店舗、それ以外は取引先
		const string InSide = "v.Type0 IN (3,5)";
		// 商品の引当は HHT データ更新（HhtProcessUpdateMap）と揃え、上段+下段の一致を優先し、なければ上段が Jan1/Jan2/Jan3 のいずれかに一致する SKU とする
		var sql = $@"
SELECT
    {kubunName} || '明細書'                      AS title,
    ''                                           AS tel,
    CASE WHEN {InSide} THEN v.ToriSaki ELSE v.Shop END AS outCode,
    CASE v.Type0 WHEN 5 THEN {ShiireName("v.ToriSaki")} WHEN 3 THEN {TokuiName("v.ToriSaki")} ELSE {TokuiName("v.Shop")} END AS outName,
    CASE WHEN {InSide} THEN v.Shop ELSE v.ToriSaki END AS inCode,
    CASE v.Type0 WHEN 6 THEN {ShiireName("v.ToriSaki")} WHEN 3 THEN {TokuiName("v.Shop")} WHEN 5 THEN {TokuiName("v.Shop")} ELSE {TokuiName("v.ToriSaki")} END AS inName,
    v.DenDay                                     AS denDay,
    {DenNo}                                      AS denNo,
    ifnull(s.Code,'')                            AS shohinCode,
    ''                                           AS genkaFlg,
    ifnull(d.Mei_Col,'')                         AS colName,
    ifnull(d.Mei_Siz,'')                         AS sizName,
    v.Su                                         AS su,
    v.Jan1                                       AS jan1,
    ifnull(s.Name,'')                            AS shohinName,
    v.Su * v.Tanka                               AS kingaku,
    v.Tanto                                      AS tantoCode,
    ifnull((select e.Name from MasterShain e where {NoZero("e.Code")} = {NoZero("v.Tanto")} limit 1),'') AS tantoName,
    v.Jan2                                       AS jan2,
    ''                                           AS genka,
    '' AS address3, '' AS fax, '' AS sokoFullName, '' AS postalCode, '' AS address1, '' AS address2,
    printf('%08d', {DenNo})                      AS denNoBarcode,
    v.Tanka                                      AS tanka,
    ''                                           AS orderNo,
    ''                                           AS nouhinDay,
    ''                                           AS memo,
    'JAN1段目'                                   AS janTitle1,
    'JAN2段目'                                   AS janTitle2,
    0                                            AS genkaKbn
FROM TranVulcanHht v
LEFT JOIN DerivedShohinColSiz d ON d.Id = ifnull(
    (select min(x.Id) from DerivedShohinColSiz x where trim(ifnull(v.Jan2,'')) <> '' and x.Jan1 = trim(v.Jan1) and x.Jan2 = trim(v.Jan2)),
    (select min(x.Id) from DerivedShohinColSiz x where x.Jan1 = trim(v.Jan1) or x.Jan2 = trim(v.Jan1) or x.Jan3 = trim(v.Jan1)))
LEFT JOIN MasterShohin s ON s.Id = d.Id_Shohin
WHERE {where}
ORDER BY v.Type0, {DenNo}, v.Tanto, v.HhtNo, v.Serial, v.LineNo";
		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
