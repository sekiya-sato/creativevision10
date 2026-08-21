using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 発注書。仕入先へ渡す発注書を、発注ヘッダ（発注日・伝票NO・仕入先・掛率・合計）と明細で構成して印字する。
/// 単票形式で、ヘッダ項目は各明細行に同じ値を繰り返した CSV を渡し、qfm 側でヘッダ領域と明細領域に振り分ける。
///
/// 自社情報（社名・住所・TEL）は MasterSysman(Id=1) から取得してヘッダに載せる。
/// </summary>
public partial class HachuFormViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "発注書";
	protected override string FormFileName => "HachuForm.qfm";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	/// <summary>true=発注(Kubun=10)のみ / false=返品･値引も含める。</summary>
	[ObservableProperty]
	public partial bool IsHachuOnly { get; set; } = true;

	[RelayCommand]
	void SelectShiireCodeFrom() => ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;

	[RelayCommand]
	void SelectShiireCodeTo() => ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) {
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		if (from > to) {
			MessageEx.ShowWarningDialog("発注日の範囲が逆転しています。", owner: ActiveWindow);
			return Task.FromResult<QueryListSqlParam?>(null);
		}
		ct.ThrowIfCancellationRequested();

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}"
			+ BuildCodeRangeWhere(parameters, TranMeisaiSql.HeaderCode("VShiire"), ShiireCodeFrom, ShiireCodeTo);
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND h.Id >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND h.Id <= {noTo}";
		if (IsHachuOnly) where += " AND h.Kubun = 10";

		var kubunLabel = TranMeisaiSql.KubunLabel("h.Kubun",
			((int)EnumHachu.Hachu, "発注"),
			((int)EnumHachu.Tsuika, "追加発注"),
			((int)EnumHachu.Jido, "自動発注"),
			((int)EnumHachu.Henpin, "返品"),
			((int)EnumHachu.Nebiki, "値引"),
			((int)EnumHachu.Other, "その他"));

		// 旧cvnetのHachuForm.qfmはitem1..item57のCSV列を使用する。
		// item58/59はqfm側に定義されているが、旧data.txtには列がないため出力しない。
		// ヘッダ値は明細各行に繰り返し、qfm側で単票ヘッダと明細へ振り分ける。
		// RawExecCmdはSELECT列名をDictionaryキーにするため、全列に一意なitem別名を付ける。
		var sql = $@"
WITH header AS (
    SELECT
        h.*,
        ifnull(si.PostalCode,'') AS shiirePostalCode,
        trim(ifnull(si.Address1,'') || ifnull(si.Address2,'') || ifnull(si.Address3,'')) AS shiireAddress,
        ifnull(si.Tel,'') AS shiireTel,
        CASE
            WHEN ifnull(si.Id_Paysaki,0) > 0 THEN ifnull(ps.Code,ifnull(si.Code,''))
            ELSE ifnull(si.Code,'')
        END AS paysakiCode,
        ifnull(so.Code,'') AS sokoCode,
        ifnull(so.Name,'') AS sokoMasterName,
        ifnull(so.PostalCode,'') AS sokoPostalCode,
        trim(ifnull(so.Address1,'') || ifnull(so.Address2,'') || ifnull(so.Address3,'')) AS sokoAddress,
        ifnull(so.Tel,'') AS sokoTel,
        ifnull(sys.Name,'') AS sysName,
        ifnull(sys.PostalCode,'') AS sysPostalCode,
        trim(ifnull(sys.Address1,'') || ifnull(sys.Address2,'') || ifnull(sys.Address3,'')) AS sysAddress,
        ifnull(sys.Tel,'') AS sysTel
    FROM Tran13Hachu h
    LEFT JOIN MasterShiire si ON si.Id = h.Id_Shiire
    LEFT JOIN MasterShiire ps ON ps.Id = si.Id_Paysaki
    LEFT JOIN MasterTokui so ON so.Id = h.Id_Soko
    LEFT JOIN MasterSysman sys ON sys.Id = 1
    WHERE {where}
)
SELECT
    {TranMeisaiSql.HeaderCode("VShiire")} AS item1,                    /* 取引先CD1 */
    {TranMeisaiSql.HeaderName("VShiire")} AS item2,                    /* 仕入先名 */
    paysakiCode AS item3,                                                /* 支払先CD */
    h.Id AS item4,                                                       /* SEQ_NO */
    {TranMeisaiSql.Num("No")} AS item5,                                /* 行NO */
    ifnull(h.DenDay,'') AS item6,                                       /* 在庫計上日 */
    '19010101' AS item7,                                                 /* 掛計上日 */
    ifnull(h.NouhinDay,'') AS item8,                                    /* 納品日 */
    h.Rate AS item9,                                                     /* 掛率1 */
    {TranMeisaiSql.HeaderCode("VShain")} AS item10,                    /* 入力社員CD */
    {TranMeisaiSql.HeaderName("VShain")} AS item11,                    /* 入力社員名 */
    h.SuTotal AS item12,                                                 /* 数量合計 */
    h.KingakuTotal AS item13,                                           /* 明細金額合計 */
    h.JodaiTotal AS item14,                                             /* 上代合計 */
    '請求時一括' AS item15,                                               /* 消費税 */
    h.KingakuTotal AS item16,                                           /* 総合計 */
    ifnull(h.Memo,'') AS item17,                                        /* メモ */
    {TranMeisaiSql.Str("Code_Shohin")} AS item18,                      /* 商品CD */
    {TranMeisaiSql.Str("Mei_Shohin")} AS item19,                       /* 商品名 */
    {TranMeisaiSql.Str("Code_Col")} AS item20,                         /* 色CD */
    {TranMeisaiSql.Str("Mei_Col")} AS item21,                          /* 色名 */
    {TranMeisaiSql.Str("Code_Siz")} AS item22,                         /* サイズCD */
    {TranMeisaiSql.Str("Mei_Siz")} AS item23,                          /* サイズ名 */
    {TranMeisaiSql.Num("Su")} AS item24,                               /* 数量 */
    {TranMeisaiSql.Num("Tanka")} AS item25,                            /* 単価 */
    {TranMeisaiSql.Num("Kingaku")} AS item26,                          /* 金額 */
    {TranMeisaiSql.Num("Jodai")} AS item27,                            /* 上代単価 */
    {TranMeisaiSql.Num("Su")} * {TranMeisaiSql.Num("Jodai")} AS item28, /* 上代金額 */
    {TranMeisaiSql.Num("No")} AS item29,                               /* 順 */
    '' AS item30,                                                       /* 伝票印字1 */
    '' AS item31,                                                       /* 伝票印字2 */
    '' AS item32,                                                       /* 伝票印字3 */
    '' AS item33,                                                       /* 伝票印字4 */
    printf('%08d', h.Id) AS item34,                                    /* BARCODE */
    h.Kubun AS item35,                                                  /* 取引区分 */
    sokoCode AS item36,                                                 /* 入庫先CD */
    coalesce(nullif({TranMeisaiSql.HeaderName("VSoko")},''), sokoMasterName) AS item37, /* 入庫先名 */
    sokoPostalCode AS item38,                                          /* 入庫先郵便番号 */
    sokoAddress AS item39,                                             /* 入庫先住所 */
    sokoTel AS item40,                                                 /* 入庫先TEL */
    '' AS item41,                                                      /* 入庫先FAX */
    {kubunLabel} AS item42,                                            /* 取引区分名 */
    sysName AS item43,                                                 /* 自社名 */
    sysPostalCode AS item44,                                           /* 郵便番号 */
    sysAddress AS item45,                                              /* 住所 */
    sysTel AS item46,                                                  /* TEL */
    '' AS item47,                                                      /* FAX */
    shiirePostalCode AS item48,                                        /* 得意先郵便番号 */
    shiireAddress AS item49,                                           /* 得意先住所 */
    shiireTel AS item50,                                               /* 得意先TEL */
    '' AS item51,                                                      /* 得意先FAX */
    '' AS item52,                                                      /* 固定文字 */
    '' AS item53,                                                      /* メーカー品番 */
    '' AS item54,                                                      /* 受注番号 */
    '' AS item55,                                                      /* 単位 */
    {TranMeisaiSql.Str("Mei_Shohin")} AS item56,                      /* 明細名称 */
    {TranMeisaiSql.Str("Memo")} AS item57                            /* 明細メモ */
FROM header h, {TranMeisaiSql.From}
WHERE {TranMeisaiSql.Guard}
ORDER BY h.Id, {TranMeisaiSql.Num("No")}";

		return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
	}
}
