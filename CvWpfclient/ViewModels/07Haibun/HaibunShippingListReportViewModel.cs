/*
# description
配分出荷リスト印刷。確定済み・未確定を問わず配分(TranHaibun)を対象に、出力単位(伝票毎/商品毎/SKU毎/商品得意先毎)を
切り替えて印刷できる画面。旧CV.netの「配分出荷リスト」(通常形式4本)に相当する（配分の新規実装4帳票の最後の1本）。
マトリクス形式2種（色×サイズ、得意先×倉庫×色×サイズのクロス集計）は、qfmに動的列生成の仕組みが無く
利用実績を見て判断する方針のため今回のスコープ外（`Doc/spec/2026-09-12_帳票レイアウト対比と修正計画.md` 参照）。

TranHaibun はヘッダ実テーブルを持たず1行=1SKUのため、伝票の括りは HaibunHeaderKey
（DenDay+NouhinDay+Id_Soko+Id_Tenpo+Kubun+RelateNo1）で行う。

数量の解釈（`Doc/spec/2026-09-12_帳票レイアウト対比と修正計画.md` および TranHaibun のコメントに基づく）:
  - 予定数量 = Su
  - 確定数量 = KakuteiDay が空でなければ JitsuSu、未確定なら 0（JitsuSu は出荷実績のため）
  - 受注数 = RelateNo1 が指す元伝票(Tran12Jyuchu、Kubun=Juchu受注配分のときのみ)の明細JSON(Jmeisai)から
    同一SKU(Id_Shohin/Id_Col/Id_Siz)の Su を合算する。受注配分以外(初回配分・在庫配分など)は元伝票が無いため 0。

金額は要件に含まれないため出力しない（上代・下代は単価であり、必要になれば Su×Jodai/Gedai で計算する）。
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._07Haibun;

public sealed partial class HaibunShippingListReportViewModel : BaseReportViewModel {
	protected override string ReportTitle => "配分出荷リスト";

	public sealed record KubunFilterOption(int Value, string Name);

	/// <summary>区分の絞込（EnumHaibun）。-1は「すべて」</summary>
	public IReadOnlyList<KubunFilterOption> KubunOptions { get; } = [
		new(-1, "すべて"),
		new((int)EnumHaibun.Hatsukai, "初回配分"),
		new((int)EnumHaibun.Zaiko, "在庫配分"),
		new((int)EnumHaibun.Juchu, "受注配分"),
		new((int)EnumHaibun.Tokui, "得意先別配分"),
		new((int)EnumHaibun.ShopRequest, "店舗出荷依頼"),
		new((int)EnumHaibun.ZaikoHin, "在庫品配分"),
		new((int)EnumHaibun.Reservation, "取置"),
		new((int)EnumHaibun.IdoShiji, "移動指示"),
	];

	/// <summary>出力単位（画面表示名＝qfmファイル選択キー）</summary>
	public const string UnitDen = "伝票毎";
	public const string UnitHin = "商品毎";
	public const string UnitSku = "SKU毎";
	public const string UnitHinTok = "商品得意先毎";

	public IReadOnlyList<string> OutputUnitList { get; } = [UnitDen, UnitHin, UnitSku, UnitHinTok];

	/// <summary>伝票毎選択時のみ有効なソートキー</summary>
	public IReadOnlyList<string> SortKeyList { get; } = ["伝票キー", "得意先", "配分指示日", "納品日"];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSortKeyEnabled))]
	public partial string OutputUnit { get; set; } = UnitDen;

	public bool IsSortKeyEnabled => OutputUnit == UnitDen;

	[ObservableProperty]
	public partial string SortKey { get; set; } = "伝票キー";

	[ObservableProperty]
	public partial int SelectedKubun { get; set; } = -1;

	/// <summary>印刷区分。true=残のみ（未出荷 EndFlag=0） false=すべて</summary>
	[ObservableProperty]
	public partial bool ZanOnly { get; set; } = true;

	[ObservableProperty]
	public partial string DenDayFromText { get; set; } = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	/// <summary>納品日範囲。空欄可（指定なし）</summary>
	[ObservableProperty]
	public partial string NouhinDayFromText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NouhinDayToText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TokuiCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string BrandCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ItemCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenjiCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenjiCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MakerCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MakerCodeTo { get; set; } = string.Empty;

	protected override string FormFileName => OutputUnit switch {
		UnitHin => "HaibunShippingListHin.qfm",
		UnitSku => "HaibunShippingListSku.qfm",
		UnitHinTok => "HaibunShippingListHinTok.qfm",
		_ => "HaibunShippingListDen.qfm",
	};

	string? SelectSokoCode() => ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;
	string? SelectMeishoCode(string kubun) => ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{kubun}'", "Code")?.Code;

	[RelayCommand]
	void SelectSokoFrom() { var c = SelectSokoCode(); if (c != null) SokoCodeFrom = c; }

	[RelayCommand]
	void SelectSokoTo() { var c = SelectSokoCode(); if (c != null) SokoCodeTo = c; }

	[RelayCommand]
	void SelectTokuiFrom() { var c = SelectTokuiCode(); if (c != null) TokuiCodeFrom = c; }

	[RelayCommand]
	void SelectTokuiTo() { var c = SelectTokuiCode(); if (c != null) TokuiCodeTo = c; }

	[RelayCommand]
	void SelectShohinFrom() { var c = SelectShohinCode(); if (c != null) ShohinCodeFrom = c; }

	[RelayCommand]
	void SelectShohinTo() { var c = SelectShohinCode(); if (c != null) ShohinCodeTo = c; }

	[RelayCommand]
	void SelectBrandFrom() { var c = SelectMeishoCode(MasterMeisho.KubunBrand); if (c != null) BrandCodeFrom = c; }

	[RelayCommand]
	void SelectBrandTo() { var c = SelectMeishoCode(MasterMeisho.KubunBrand); if (c != null) BrandCodeTo = c; }

	[RelayCommand]
	void SelectItemFrom() { var c = SelectMeishoCode(MasterMeisho.KubunItem); if (c != null) ItemCodeFrom = c; }

	[RelayCommand]
	void SelectItemTo() { var c = SelectMeishoCode(MasterMeisho.KubunItem); if (c != null) ItemCodeTo = c; }

	[RelayCommand]
	void SelectTenjiFrom() { var c = SelectMeishoCode(MasterMeisho.KubunTenji); if (c != null) TenjiCodeFrom = c; }

	[RelayCommand]
	void SelectTenjiTo() { var c = SelectMeishoCode(MasterMeisho.KubunTenji); if (c != null) TenjiCodeTo = c; }

	[RelayCommand]
	void SelectMakerFrom() { var c = SelectMeishoCode(MasterMeisho.KubunMaker); if (c != null) MakerCodeFrom = c; }

	[RelayCommand]
	void SelectMakerTo() { var c = SelectMeishoCode(MasterMeisho.KubunMaker); if (c != null) MakerCodeTo = c; }

	[RelayCommand]
	void ClearConditions() {
		OutputUnit = UnitDen;
		SortKey = "伝票キー";
		SelectedKubun = -1;
		ZanOnly = true;
		DenDayFromText = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenDayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		NouhinDayFromText = string.Empty;
		NouhinDayToText = string.Empty;
		SokoCodeFrom = string.Empty;
		SokoCodeTo = string.Empty;
		TokuiCodeFrom = string.Empty;
		TokuiCodeTo = string.Empty;
		ShohinCodeFrom = string.Empty;
		ShohinCodeTo = string.Empty;
		BrandCodeFrom = string.Empty;
		BrandCodeTo = string.Empty;
		ItemCodeFrom = string.Empty;
		ItemCodeTo = string.Empty;
		TenjiCodeFrom = string.Empty;
		TenjiCodeTo = string.Empty;
		MakerCodeFrom = string.Empty;
		MakerCodeTo = string.Empty;
		Message = "検索条件をクリアしました";
	}

	/// <summary>
	/// 印刷用SQLを構築する。0件時は空白PDFを出さず警告して中止する（null を返す）。
	/// </summary>
	protected override async Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFromText, out var denFrom)) return null;
		if (!TryParseDate(DenDayToText, out var denTo)) return null;
		if (denFrom > denTo) {
			MessageEx.ShowWarningDialog("配分指示日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return null;
		}
		DateTime? nouhinFrom = null, nouhinTo = null;
		if (!string.IsNullOrWhiteSpace(NouhinDayFromText)) {
			if (!TryParseDate(NouhinDayFromText, out var f)) return null;
			nouhinFrom = f;
		}
		if (!string.IsNullOrWhiteSpace(NouhinDayToText)) {
			if (!TryParseDate(NouhinDayToText, out var t)) return null;
			nouhinTo = t;
		}
		if (nouhinFrom is not null && nouhinTo is not null && nouhinFrom > nouhinTo) {
			MessageEx.ShowWarningDialog("納品日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return null;
		}

		var (where, parameters) = BuildWhere(denFrom, denTo, nouhinFrom, nouhinTo);
		if (!await HasAnyDataAsync(where, parameters, ct)) {
			MessageEx.ShowWarningDialog("対象データがありません。", owner: ActiveWindow);
			Message = "対象データがありません。";
			return null;
		}

		var condition = AddSqlParameter(parameters, BuildConditionText(denFrom, denTo, nouhinFrom, nouhinTo));
		return OutputUnit switch {
			UnitHin => BuildHinSqlParam(where, parameters, condition),
			UnitSku => BuildSkuSqlParam(where, parameters, condition),
			UnitHinTok => BuildHinTokSqlParam(where, parameters, condition),
			_ => BuildDenSqlParam(where, parameters, condition),
		};
	}

	async Task<bool> HasAnyDataAsync(string where, List<string> parameters, CancellationToken ct) {
		var sql = $@"
SELECT h.Id
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
WHERE {where}
LIMIT 1";
		var rows = await CoreServiceClient.QuerySqlListAsync<object>(sql, parameters, ct);
		return rows.Count > 0;
	}

	/// <summary>
	/// 検索条件からWHERE句とバインドパラメータを組み立てる。エイリアスは h(TranHaibun) / soko / ten(得意先=Id_Tenpo) / sh(商品) 固定。
	/// </summary>
	(string where, List<string> parameters) BuildWhere(DateTime denFrom, DateTime denTo, DateTime? nouhinFrom, DateTime? nouhinTo) {
		List<string> parameters = [ToDenDay(denFrom), ToDenDay(denTo)];
		var where = "h.DenDay BETWEEN @0 AND @1";
		if (nouhinFrom is not null) {
			where += $" AND h.NouhinDay >= {AddSqlParameter(parameters, ToDenDay(nouhinFrom.Value))}";
		}
		if (nouhinTo is not null) {
			where += $" AND h.NouhinDay <= {AddSqlParameter(parameters, ToDenDay(nouhinTo.Value))}";
		}
		if (SelectedKubun >= 0) {
			where += $" AND h.Kubun = {AddSqlParameter(parameters, SelectedKubun)}";
		}
		if (ZanOnly) {
			where += " AND h.EndFlag = 0";
		}
		where += BuildCodeRangeWhere(parameters, "soko.Code", SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, "ten.Code", TokuiCodeFrom, TokuiCodeTo);
		where += BuildCodeRangeWhere(parameters, "sh.Code", ShohinCodeFrom, ShohinCodeTo);
		where += BuildCodeRangeWhere(parameters, JsonCd("sh.VBrand"), BrandCodeFrom, BrandCodeTo);
		where += BuildCodeRangeWhere(parameters, JsonCd("sh.VItem"), ItemCodeFrom, ItemCodeTo);
		where += BuildCodeRangeWhere(parameters, JsonCd("sh.VTenji"), TenjiCodeFrom, TenjiCodeTo);
		where += BuildCodeRangeWhere(parameters, JsonCd("sh.VMaker"), MakerCodeFrom, MakerCodeTo);
		return (where, parameters);
	}

	static string JsonCd(string column) =>
		$"ifnull(json_extract(case when json_valid({column}) then {column} else '{{}}' end,'$.Cd'),'')";

	/// <summary>
	/// 受注数SQL式。RelateNo1が指す元伝票(Tran12Jyuchu、受注配分のときのみ)の明細JSONから
	/// 同一SKU(Id_Shohin/Id_Col/Id_Siz)のSuを合算する。受注配分以外は0（元伝票が無い）。
	/// </summary>
	static string JuchuSuSql() => $@"(
SELECT ifnull(SUM(CAST(ifnull(json_extract(jm.value,'$.Su'),0) AS INTEGER)),0)
FROM {nameof(Tran12Jyuchu)} jy, json_each(jy.Jmeisai) jm
WHERE jy.Id = h.RelateNo1 AND h.Kubun = {(int)EnumHaibun.Juchu} AND json_valid(jy.Jmeisai)
AND CAST(ifnull(json_extract(jm.value,'$.Id_Shohin'),0) AS INTEGER) = h.Id_Shohin
AND CAST(ifnull(json_extract(jm.value,'$.Id_Col'),0) AS INTEGER) = h.Id_Col
AND CAST(ifnull(json_extract(jm.value,'$.Id_Siz'),0) AS INTEGER) = h.Id_Siz
)";

	/// <summary>確定数量：KakuteiDayが空でなければJitsuSu、未確定なら0（JitsuSuは出荷実績のため）</summary>
	const string KakuteiSuExpr = "(CASE WHEN ifnull(h.KakuteiDay,'') <> '' THEN h.JitsuSu ELSE 0 END)";

	/// <summary>伝票キー文字列（HaibunHeaderKeyの主要5列を連結、固定長27桁）。DenKey(2)/2のバーコードと同じ組立て</summary>
	static string KeyTextSql(string alias) =>
		$"({alias}.DenDay || cast({alias}.Kubun as text) || substr('000000'||{alias}.Id_Soko,-6,6) || substr('000000'||{alias}.Id_Tenpo,-6,6) || substr('000000'||{alias}.RelateNo1,-6,6))";

	static string KubunLabelSql(string alias) => $@"CASE {alias}.Kubun
WHEN 0 THEN '初回配分' WHEN 1 THEN '在庫配分' WHEN 2 THEN '受注配分' WHEN 3 THEN '得意先別配分'
WHEN 4 THEN '店舗出荷依頼' WHEN 5 THEN '在庫品配分' WHEN 6 THEN '取置' WHEN 7 THEN '移動指示'
ELSE cast({alias}.Kubun as text) END";

	const string FromJoin = $@"
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
LEFT JOIN {nameof(DerivedShohinColSiz)} sku ON sku.Id_Shohin = h.Id_Shohin AND sku.Id_Col = h.Id_Col AND sku.Id_Siz = h.Id_Siz";

	/// <summary>
	/// item1:配分指示日 item2:納品日 item3:倉庫CD名 item4:得意先CD名 item5:伝票キー(伝票計グループ)
	/// item6:区分名 item7:商品CD名 item8:色/サイズ item9:受注数 item10:予定数量 item11:確定数量 item12:抽出条件(総合計グループ)
	/// </summary>
	QueryListSqlParam BuildDenSqlParam(string where, List<string> parameters, string condition) {
		var order = SortKey switch {
			"得意先" => "ten.Code, ",
			"配分指示日" => "h.DenDay, ",
			"納品日" => "h.NouhinDay, ",
			_ => "",
		};
		var sql = $@"
SELECT
substr(h.DenDay,1,4)||'/'||substr(h.DenDay,5,2)||'/'||substr(h.DenDay,7,2) as item1,
CASE WHEN ifnull(h.NouhinDay,'')='' THEN '' ELSE substr(h.NouhinDay,1,4)||'/'||substr(h.NouhinDay,5,2)||'/'||substr(h.NouhinDay,7,2) END as item2,
{CodeNameDisplay.Sql("soko.Id", "soko.Code", "soko.Name")} as item3,
{CodeNameDisplay.Sql("ten.Id", "ten.Code", "ten.Name")} as item4,
{KeyTextSql("h")} as item5,
{KubunLabelSql("h")} as item6,
{CodeNameDisplay.Sql("sh.Id", "sh.Code", "sh.Name")} as item7,
trim(trim(ifnull(sku.Code_Col,'')||' '||ifnull(sku.Mei_Col,''))||' / '||trim(ifnull(sku.Code_Siz,'')||' '||ifnull(sku.Mei_Siz,''))) as item8,
{JuchuSuSql()} as item9,
h.Su as item10,
{KakuteiSuExpr} as item11,
{condition} as item12
{FromJoin}
WHERE {where}
ORDER BY {order}h.Id_Soko, h.Id_Tenpo, h.DenDay, h.Kubun, h.RelateNo1, h.Id";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// 品番毎(ブランド→アイテム→商品の階層で集計)。
	/// item1:ブランド(ブランド計グループ) item2:アイテム(アイテム計グループ) item3:商品CD名
	/// item4:受注数計 item5:予定数量計 item6:確定数量計 item7:抽出条件(総合計グループ)
	/// </summary>
	QueryListSqlParam BuildHinSqlParam(string where, List<string> parameters, string condition) {
		var sql = $@"
SELECT
{CodeNameDisplay.SqlFromVColumn("sh.VBrand")} as item1,
{CodeNameDisplay.SqlFromVColumn("sh.VItem")} as item2,
{CodeNameDisplay.Sql("sh.Id", "sh.Code", "sh.Name")} as item3,
SUM({JuchuSuSql()}) as item4,
SUM(h.Su) as item5,
SUM({KakuteiSuExpr}) as item6,
{condition} as item7
{FromJoin}
WHERE {where}
GROUP BY h.Id_Shohin
ORDER BY sh.Id_Brand, sh.Id_Item, sh.Code";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// SKU毎(ブランド→アイテムの階層、明細はSKU単位)。
	/// item1:ブランド(ブランド計グループ) item2:アイテム(アイテム計グループ) item3:商品CD名 item4:色/サイズ
	/// item5:受注数計 item6:予定数量計 item7:確定数量計 item8:抽出条件(総合計グループ)
	/// </summary>
	QueryListSqlParam BuildSkuSqlParam(string where, List<string> parameters, string condition) {
		var sql = $@"
SELECT
{CodeNameDisplay.SqlFromVColumn("sh.VBrand")} as item1,
{CodeNameDisplay.SqlFromVColumn("sh.VItem")} as item2,
{CodeNameDisplay.Sql("sh.Id", "sh.Code", "sh.Name")} as item3,
trim(trim(ifnull(sku.Code_Col,'')||' '||ifnull(sku.Mei_Col,''))||' / '||trim(ifnull(sku.Code_Siz,'')||' '||ifnull(sku.Mei_Siz,''))) as item4,
SUM({JuchuSuSql()}) as item5,
SUM(h.Su) as item6,
SUM({KakuteiSuExpr}) as item7,
{condition} as item8
{FromJoin}
WHERE {where}
GROUP BY h.Id_Shohin, h.Id_Col, h.Id_Siz
ORDER BY sh.Id_Brand, sh.Id_Item, sh.Code, sku.Code_Col, sku.Code_Siz";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// 商品得意先毎(ブランド→アイテムの階層、明細は商品×得意先単位)。
	/// item1:ブランド(ブランド計グループ) item2:アイテム(アイテム計グループ) item3:商品CD名 item4:得意先CD名
	/// item5:受注数計 item6:予定数量計 item7:確定数量計 item8:抽出条件(総合計グループ)
	/// </summary>
	QueryListSqlParam BuildHinTokSqlParam(string where, List<string> parameters, string condition) {
		var sql = $@"
SELECT
{CodeNameDisplay.SqlFromVColumn("sh.VBrand")} as item1,
{CodeNameDisplay.SqlFromVColumn("sh.VItem")} as item2,
{CodeNameDisplay.Sql("sh.Id", "sh.Code", "sh.Name")} as item3,
{CodeNameDisplay.Sql("ten.Id", "ten.Code", "ten.Name")} as item4,
SUM({JuchuSuSql()}) as item5,
SUM(h.Su) as item6,
SUM({KakuteiSuExpr}) as item7,
{condition} as item8
{FromJoin}
WHERE {where}
GROUP BY h.Id_Shohin, h.Id_Tenpo
ORDER BY sh.Id_Brand, sh.Id_Item, sh.Code, ten.Code";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	string BuildConditionText(DateTime denFrom, DateTime denTo, DateTime? nouhinFrom, DateTime? nouhinTo) {
		var kubun = SelectedKubun < 0 ? "すべて" : (KubunOptions.FirstOrDefault(x => x.Value == SelectedKubun)?.Name ?? SelectedKubun.ToString(CultureInfo.InvariantCulture));
		var nouhin = nouhinFrom is null && nouhinTo is null
			? "指定なし"
			: $"{(nouhinFrom?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし")}〜{(nouhinTo?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし")}";
		var soko = BuildRangeText(SokoCodeFrom, SokoCodeTo);
		var tokui = BuildRangeText(TokuiCodeFrom, TokuiCodeTo);
		var shohin = BuildRangeText(ShohinCodeFrom, ShohinCodeTo);
		var zan = ZanOnly ? "残のみ" : "すべて";
		return $"出力単位:{OutputUnit} 区分:{kubun} 印刷区分:{zan} 配分指示日:{denFrom:yyyy/MM/dd}〜{denTo:yyyy/MM/dd} 納品日:{nouhin} 倉庫:{soko} 得意先:{tokui} 商品:{shohin}";
	}

	static string BuildRangeText(string? from, string? to) {
		var f = (from ?? string.Empty).Trim();
		var t = (to ?? string.Empty).Trim();
		if (f.Length == 0 && t.Length == 0) return "指定なし";
		return $"{(f.Length == 0 ? "先頭" : f)}〜{(t.Length == 0 ? "最後" : t)}";
	}
}
