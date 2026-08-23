using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 仕入伝票印刷。印刷範囲(仕入日/仕入先/倉庫/伝票NO/手入力NO/取引区分)を指定し、
/// ShiireSlipPrint.qfm へ SQL で明細1行=CSV1行のデータを渡して PDF 出力する。
/// レイアウトの標準は ShopBudgetReport(BaseReportViewModel 派生の印刷ダイアログ)に合わせる。
/// </summary>
public partial class ShiireSlipPrintViewModel : Helpers.BaseReportViewModel {
	protected override string ReportTitle => "仕入伝票印刷";
	protected override string FormFileName => "ShiireSlipPrint.qfm";

	/// <summary>取引区分の選択肢。Value=null は「全て」。</summary>
	public sealed record KubunOption(int? Value, string Name);

	// 仕入日(DenDay)範囲
	[ObservableProperty]
	public partial DateTime? DenDayFrom { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial DateTime? DenDayTo { get; set; } = DateTime.Today;

	// 仕入先コード範囲
	[ObservableProperty]
	public partial string ShiireCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShiireCodeTo { get; set; } = string.Empty;

	// 倉庫コード範囲
	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	// 伝票NO(Tran03Shiire.Id)範囲
	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	// 手入力NO(ManualNo)範囲
	[ObservableProperty]
	public partial string ManualNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ManualNoTo { get; set; } = string.Empty;

	// 取引区分(Kubun)
	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(null, "全て"),
		new((int)EnumShiire.Shiire, "仕入"),
		new((int)EnumShiire.Henpin, "仕入返品"),
		new((int)EnumShiire.Nebiki, "値引"),
		new((int)EnumShiire.Other, "その他"),
	];

	[ObservableProperty]
	public partial KubunOption SelectedKubun { get; set; }

	public ShiireSlipPrintViewModel() {
		SelectedKubun = KubunOptions[0];
	}

	[RelayCommand]
	void SelectShiireCodeFrom() {
		ShiireCodeFrom = SelectShiireCode() ?? ShiireCodeFrom;
	}

	[RelayCommand]
	void SelectShiireCodeTo() {
		ShiireCodeTo = SelectShiireCode() ?? ShiireCodeTo;
	}

	[RelayCommand]
	void SelectSokoCodeFrom() {
		SokoCodeFrom = SelectSokoCode() ?? SokoCodeFrom;
	}

	[RelayCommand]
	void SelectSokoCodeTo() {
		SokoCodeTo = SelectSokoCode() ?? SokoCodeTo;
	}

	/// <summary>倉庫選択ダイアログ(TenType=0)。選択されなければ null</summary>
	string? SelectSokoCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		// BuildPrintSqlParam は入力不正時に警告を出して null を返す
		return Task.FromResult(BuildPrintSqlParam());
	}

	// 明細JSON(Jmeisai)の各値。ShiireInput の明細印刷SQLと同じ抽出規則。
	const string M = "json_extract(m.value,";
	// 取引区分(Kubun)ラベル。ShiireInput.KubunLabel と同義(ここでは h. 修飾)。
	const string KubunLabel = "case h.Kubun when 10 then '仕入' when 20 then '仕入返品' when 30 then '値引' when 99 then 'その他' else cast(h.Kubun as text) end";

	/// <summary>
	/// ShiireSlipPrint.qfm の item1..item46 に対応する 46 列を、明細1行=CSV1行で SELECT する。
	/// 列順が qfm の itemN 順(datasrc)と一致することが前提。参照されない item は '' プレースホルダで詰める。
	/// </summary>
	QueryListSqlParam? BuildPrintSqlParam() {
		List<string> parameters = [];
		List<string> where = [];

		// 仕入日範囲(yyyyMMdd へ正規化)
		if (DenDayFrom is DateTime fromDt) {
			where.Add($"h.DenDay >= {AddSqlParameter(parameters, fromDt.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		}
		if (DenDayTo is DateTime toDt) {
			where.Add($"h.DenDay <= {AddSqlParameter(parameters, toDt.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}");
		}

		// 仕入先コード範囲(MasterShiire.Code)
		if (!string.IsNullOrWhiteSpace(ShiireCodeFrom)) {
			where.Add($"si.Code >= {AddSqlParameter(parameters, ShiireCodeFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(ShiireCodeTo)) {
			where.Add($"si.Code <= {AddSqlParameter(parameters, ShiireCodeTo.Trim())}");
		}

		// 倉庫コード範囲(MasterTokui.Code)
		if (!string.IsNullOrWhiteSpace(SokoCodeFrom)) {
			where.Add($"so.Code >= {AddSqlParameter(parameters, SokoCodeFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(SokoCodeTo)) {
			where.Add($"so.Code <= {AddSqlParameter(parameters, SokoCodeTo.Trim())}");
		}

		// 伝票NO範囲(Tran03Shiire.Id / 数値なので直接埋め込み)
		if (!string.IsNullOrWhiteSpace(DenNoFrom)) {
			if (!long.TryParse(DenNoFrom.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { WarnInvalidNumber("伝票NO(開始)"); return null; }
			where.Add($"h.Id >= {n}");
		}
		if (!string.IsNullOrWhiteSpace(DenNoTo)) {
			if (!long.TryParse(DenNoTo.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { WarnInvalidNumber("伝票NO(終了)"); return null; }
			where.Add($"h.Id <= {n}");
		}

		// 手入力NO範囲(ManualNo / 文字列比較)
		if (!string.IsNullOrWhiteSpace(ManualNoFrom)) {
			where.Add($"h.ManualNo >= {AddSqlParameter(parameters, ManualNoFrom.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(ManualNoTo)) {
			where.Add($"h.ManualNo <= {AddSqlParameter(parameters, ManualNoTo.Trim())}");
		}

		// 取引区分(Kubun)
		if (SelectedKubun?.Value is int kubun) {
			where.Add($"h.Kubun = {kubun}");
		}

		var whereClause = where.Count > 0 ? "where " + string.Join(" and ", where) : "";

		// 明細の数値(数量/単価/金額/上代)
		var su = $"cast(ifnull({M}'$.Su'),0) as int)";
		var tanka = $"cast(ifnull({M}'$.Tanka'),0) as int)";
		var kingaku = $"cast(ifnull({M}'$.Kingaku'),0) as int)";
		var jodai = $"cast(ifnull({M}'$.Jodai'),0) as int)";

		// 内側: 伝票ヘッダ + 仕入先/倉庫/自社の住所を結合(明細展開前)。
		// 名称と住所で取得元が異なるのは意図的な仕様:
		//   名称(item5/6の仕入先名・item16の倉庫名) … 伝票のV*列(VShiire/VSoko)から取る = 伝票作成時点の名称
		//   住所・電話・郵便番号            … マスタをJOINして取る = 現行値(伝票側に保持していないため)
		// Tran系のV*列は改名時に伝播しない監査値であるため、名称は時点値のまま出す。
		// 詳細は .omo/20260727_master_vcolumn_sync_design.md を参照。
		var header = $@"
select h.*,
	ifnull(si.PostalCode,'') siZip,
	trim(ifnull(si.Address1,'') || ifnull(si.Address2,'') || ifnull(si.Address3,'')) siAddr,
	ifnull(si.Tel,'') siTel,
	ifnull(so.PostalCode,'') soZip,
	trim(ifnull(so.Address1,'') || ifnull(so.Address2,'') || ifnull(so.Address3,'')) soAddr,
	ifnull(so.Tel,'') soTel,
	ifnull(sys.Name,'') sysName,
	ifnull(sys.PostalCode,'') sysZip,
	trim(ifnull(sys.Address1,'') || ifnull(sys.Address2,'') || ifnull(sys.Address3,'')) sysAddr,
	ifnull(sys.Tel,'') sysTel
from Tran03Shiire h
left join MasterShiire si on si.Id = h.Id_Shiire
left join MasterTokui so on so.Id = h.Id_Soko
left join MasterSysman sys on sys.Id = 1
{whereClause}
";

		// 外側: 明細(Jmeisai)を json_each で1行ずつ展開し、qfm の item1..item46 順に SELECT。
		// RawExecCmdはSELECT列名をDictionaryキーにするため、全列に一意なitem別名を付ける。
		var sql = $@"
select
	h.Id as item1,															/* item1  伝票No */
	ifnull(h.KakeDay,'') as item2,											/* item2  発送日(掛計上日) */
	ifnull(h.DenDay,'') as item3,											/* item3  仕入日 */
	'' as item4,															/* item4  (予備) */
	ifnull(json_extract(h.VShiire,'$.Mei'),'') as item5,						/* item5  仕入先名 */
	ifnull(json_extract(h.VShiire,'$.Cd'),'') as item6,						/* item6  仕入先CD */
	h.Rate as item7,														/* item7  掛率 */
	'' as item8,															/* item8  (未使用) */
	'' as item9,															/* item9  (未使用) */
	ifnull(json_extract(h.VShain,'$.Mei'),'') as item10,						/* item10 入力者名 */
	h.siZip as item11,														/* item11 仕入先〒 */
	h.siAddr as item12,														/* item12 仕入先住所 */
	h.siTel as item13,														/* item13 仕入先TEL */
	'' as item14,															/* item14 仕入先FAX(該当項目なし) */
	'' as item15,															/* item15 (未使用) */
	ifnull(json_extract(h.VSoko,'$.Mei'),'') as item16,						/* item16 入庫倉庫名 */
	h.soZip as item17,														/* item17 倉庫〒 */
	h.soAddr as item18,														/* item18 倉庫住所 */
	h.soTel as item19,														/* item19 倉庫TEL */
	'' as item20,															/* item20 倉庫FAX(該当項目なし) */
	h.sysName as item21,													/* item21 自社名 */
	h.sysZip as item22,														/* item22 自社〒 */
	h.sysAddr as item23,														/* item23 自社住所 */
	h.sysTel as item24,														/* item24 自社TEL */
	'' as item25,															/* item25 自社FAX(該当項目なし) */
	ifnull({M}'$.Code_Shohin'),'') as item26,									/* item26 商品CD */
	ifnull({M}'$.Mei_Shohin'),'') as item27,									/* item27 商品名 */
	ifnull({M}'$.Code_Col'),'') as item28,										/* item28 色CD */
	ifnull({M}'$.Mei_Col'),'') as item29,										/* item29 色名 */
	ifnull({M}'$.Code_Siz'),'') as item30,										/* item30 サイズCD */
	ifnull({M}'$.Mei_Siz'),'') as item31,										/* item31 サイズ名 */
	sum({su}) over (partition by h.Id) as item32,								/* item32 数量合計 */
	sum({kingaku}) over (partition by h.Id) as item33,							/* item33 金額合計 */
	sum(({su}) * ({jodai})) over (partition by h.Id) as item34,				/* item34 上代合計 */
	'請求時一括' as item35,													/* item35 消費税(仕入は請求時一括のため固定表示) */
	sum({kingaku}) over (partition by h.Id) as item36,							/* item36 総合計(=金額合計、伝票単位の消費税は持たない) */
	{su} as item37,															/* item37 数量 */
	{tanka} as item38,														/* item38 単価 */
	{kingaku} as item39,													/* item39 金額 */
	{jodai} as item40,														/* item40 上代 */
	({su}) * ({jodai}) as item41,												/* item41 上代合計(行) */
	cast(ifnull({M}'$.No'),0) as int) as item42,								/* item42 明細No */
	'商品仕入' as item43,													/* item43 伝票処理区分 */
	{KubunLabel} as item44,													/* item44 取引区分 */
	ifnull(h.Memo,'') as item45,											/* item45 備考 */
	{KubunLabel} as item46													/* item46 伝票種別(qfm で""伝票""を付加) */
from ({header}) h, json_each(h.Jmeisai) m
where m.value is not null
order by h.Id, cast(ifnull({M}'$.No'),0) as int)
";

		return new QueryListSqlParam(typeof(Tran03Shiire), sql, [.. parameters]);
	}

	static bool TryToYmd(string value, out string ymd) {
		ymd = string.Empty;
		var v = value.Trim();
		var formats = new[] { "yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd", "yyyy-MM-dd", "yyyy-M-d" };
		if (!DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) {
			return false;
		}
		ymd = d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	void WarnInvalidDate(string label) =>
		MessageEx.ShowWarningDialog($"{label}は yyyy/MM/dd 形式で入力してください。", owner: ClientLib.GetActiveView(this));

	void WarnInvalidNumber(string label) =>
		MessageEx.ShowWarningDialog($"{label}は数値で入力してください。", owner: ClientLib.GetActiveView(this));
}
