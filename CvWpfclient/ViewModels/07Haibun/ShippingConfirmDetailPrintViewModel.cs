using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 出荷指示明細書印刷。確定済み配分(<see cref="TranHaibun"/>)を仮想ヘッダキー
/// (<see cref="HaibunHeaderKey"/>)単位でグルーピングし、ピッキングリストとして印刷する。
/// 旧CV.netの「出荷指示明細書」に相当する（配分の新規実装4帳票の1本）。
/// <para>
/// <see cref="TranHaibun"/> はヘッダ実テーブルを持たず1行=1SKUのため、伝票の括りは
/// <see cref="HaibunHeaderKey"/>（DenDay+NouhinDay+Id_Soko+Id_Tenpo+Kubun+RelateNo1）で行う
/// （`CvBase/BaseDbHaibun.cs` 設計コメント参照）。
/// </para>
/// <para>
/// 帳票は既存の伝票入力画面（例: <c>ShukkaUriageInputViewModel</c>）と同じ
/// <c>{Prefix}_header.qfm</c> / <c>{Prefix}_detail.qfm</c> の2ファイル構成を踏襲する。
///  - header.qfm … ヘッダキー単位に集計した1行=1ページのサマリー（数量計・上代金額計、バーコード付き）
///  - detail.qfm … ヘッダキー単位に改ページするSKU明細（バーコード付き見出し）＋伝票計
/// <c>PrintPdfHelper.RunPrintPdfAsync</c> は1呼出につき1帳票しか出力できないため、
/// 画面の印刷ボタン(F6)は1つのまま、内部で header→detail の順に2回呼び出す。
/// </para>
/// </summary>
public sealed partial class ShippingConfirmDetailPrintViewModel : BaseReportViewModel {
	protected override string ReportTitle => "出荷指示明細書印刷";

	const string FormPrefix = "ShippingInstructionDetail";

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

	[ObservableProperty]
	public partial int SelectedKubun { get; set; } = -1;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenpoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TenpoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenDayFromText { get; set; } = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string DenDayToText { get; set; } = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	/// <summary>確定済みのみを対象にする。旧仕様「配分確定済み伝票のみ出力」を踏襲し既定ON。</summary>
	[ObservableProperty]
	public partial bool KakuteiOnly { get; set; } = true;

	// BaseReportViewModel の抽象契約を満たすための既定フォーム（DoOutputPdf自体はUIから使わない。DoPrintが本体）。
	protected override string FormFileName => $"{FormPrefix}_detail.qfm";

	/// <summary>倉庫選択ダイアログ（TenType=0）。選択されなければ null</summary>
	string? SelectSokoCode() => ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code")?.Code;

	[RelayCommand]
	void SelectSokoFrom() { var c = SelectSokoCode(); if (c != null) SokoCodeFrom = c; }

	[RelayCommand]
	void SelectSokoTo() { var c = SelectSokoCode(); if (c != null) SokoCodeTo = c; }

	[RelayCommand]
	void SelectTenpoFrom() { var c = SelectTokuiCode(); if (c != null) TenpoCodeFrom = c; }

	[RelayCommand]
	void SelectTenpoTo() { var c = SelectTokuiCode(); if (c != null) TenpoCodeTo = c; }

	[RelayCommand]
	void ClearConditions() {
		SelectedKubun = -1;
		SokoCodeFrom = string.Empty;
		SokoCodeTo = string.Empty;
		TenpoCodeFrom = string.Empty;
		TenpoCodeTo = string.Empty;
		DenDayFromText = DateTime.Now.AddMonths(-1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DenDayToText = DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		KakuteiOnly = true;
		Message = "検索条件をクリアしました";
	}

	/// <summary>
	/// 印刷実行。0件時は空白PDFを出さず警告して中止する。
	/// header.qfm（ヘッダサマリー）→ detail.qfm（SKU明細＋伝票計）の順に出力する。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoPrint(CancellationToken ct) {
		if (!TryParseDate(DenDayFromText, out var from)) return;
		if (!TryParseDate(DenDayToText, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("配分指示日の開始日が終了日より後になっています。", owner: ActiveWindow);
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			if (!await HasAnyDataAsync(from, to, ct)) {
				MessageEx.ShowWarningDialog("対象データがありません。", owner: ActiveWindow);
				Message = "対象データがありません。";
				return;
			}

			await RunPrintPdfAsync($"{FormPrefix}_header.qfm", null, BuildHeaderSqlParam(from, to), ct);
			await RunPrintPdfAsync($"{FormPrefix}_detail.qfm", null, BuildDetailSqlParam(from, to), ct);
		}
		catch (OperationCanceledException) {
			Message = "印刷を中断しました";
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	// BaseReportViewModel の抽象契約用。DoPrint が本体のため、UIからはこちらを使わない。
	protected override async Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFromText, out var from)) return null;
		if (!TryParseDate(DenDayToText, out var to)) return null;
		await Task.CompletedTask;
		return BuildDetailSqlParam(from, to);
	}

	async Task<bool> HasAnyDataAsync(DateTime from, DateTime to, CancellationToken ct) {
		var (where, parameters) = BuildWhere(from, to);
		var sql = $@"
SELECT h.*
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
WHERE {where}
LIMIT 1";
		var rows = await CoreServiceClient.QuerySqlListAsync<TranHaibun>(sql, parameters, ct);
		return rows.Count > 0;
	}

	/// <summary>
	/// 検索条件からWHERE句とバインドパラメータを組み立てる。TranHaibunのエイリアスは h、
	/// 出庫倉庫/出荷先の絞込に使うマスタJOINのエイリアスは soko / ten を前提にする。
	/// </summary>
	(string where, List<string> parameters) BuildWhere(DateTime from, DateTime to) {
		List<string> parameters = [ToDenDay(from), ToDenDay(to)];
		var where = "h.DenDay BETWEEN @0 AND @1";
		if (SelectedKubun >= 0) {
			where += $" AND h.Kubun = {SqlWhere.AddParameter(parameters, SelectedKubun)}";
		}
		if (KakuteiOnly) {
			where += " AND ifnull(h.KakuteiDay,'') <> ''";
		}
		where += SqlWhere.CodeRange(parameters, "soko.Code", SokoCodeFrom, SokoCodeTo);
		where += SqlWhere.CodeRange(parameters, "ten.Code", TenpoCodeFrom, TenpoCodeTo);
		return (where, parameters);
	}

	/// <summary>
	/// header.qfm 用SQL。HaibunHeaderKey単位に集計した1行=1ページのサマリー。
	/// SELECT列順は item1..item12 と一致させる（下記コメント参照）。
	/// item1:配分指示日 item2:伝票キー(バーコード) item3:出庫部門CD item4:出庫部門名
	/// item5:入庫部門CD item6:入庫部門名 item7:担当者CD item8:担当者名 item9:区分名
	/// item10:数量計 item11:上代金額計 item12:抽出条件
	/// </summary>
	QueryListSqlParam BuildHeaderSqlParam(DateTime from, DateTime to) {
		var (where, parameters) = BuildWhere(from, to);
		var condition = SqlWhere.AddParameter(parameters, BuildConditionText(from, to));
		var sql = $@"
SELECT
substr(g.DenDay,1,4)||'/'||substr(g.DenDay,5,2)||'/'||substr(g.DenDay,7,2) as item1,
{KeyTextSql("g")} as item2,
ifnull(g.SokoCode,'') as item3,
ifnull(g.SokoName,'') as item4,
ifnull(g.TenCode,'') as item5,
ifnull(g.TenName,'') as item6,
ifnull(shain.Code,'') as item7,
ifnull(shain.Name,'') as item8,
{KubunLabelSql("g")} as item9,
g.SuTotal as item10,
g.JodaiTotal as item11,
{condition} as item12
FROM (
	SELECT
		h.DenDay, h.NouhinDay, h.Id_Soko, h.Id_Tenpo, h.Kubun, h.RelateNo1,
		soko.Code as SokoCode, soko.Name as SokoName,
		ten.Code as TenCode, ten.Name as TenName,
		MAX(h.Id_Shain) as Id_Shain,
		SUM(h.Su) as SuTotal,
		SUM(h.Su * h.Jodai) as JodaiTotal
	FROM {nameof(TranHaibun)} h
	LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
	LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
	WHERE {where}
	GROUP BY h.DenDay, h.NouhinDay, h.Id_Soko, h.Id_Tenpo, h.Kubun, h.RelateNo1
) g
LEFT JOIN {nameof(MasterShain)} shain ON shain.Id = g.Id_Shain
ORDER BY g.Id_Soko, g.Id_Tenpo, g.DenDay, g.Kubun, g.RelateNo1";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// detail.qfm 用SQL。TranHaibun 1行=1SKUのままHaibunHeaderKeyでグルーピングできるよう
	/// キー列を先頭にORDER BYし、qfm側は伝票キー(item2)の値変化でグループ改ページする。
	/// item1:配分指示日 item2:伝票キー(バーコード) item3:出庫部門CD item4:出庫部門名
	/// item5:入庫部門CD item6:入庫部門名 item7:担当者CD item8:担当者名
	/// item9:商品CD item10:商品名 item11:色CD item12:色名 item13:サイズCD item14:サイズ名
	/// item15:JANコード item16:数量 item17:上代単価 item18:上代金額(数量×上代単価) item19:抽出条件
	/// </summary>
	QueryListSqlParam BuildDetailSqlParam(DateTime from, DateTime to) {
		var (where, parameters) = BuildWhere(from, to);
		var condition = SqlWhere.AddParameter(parameters, BuildConditionText(from, to));
		var sql = $@"
SELECT
substr(h.DenDay,1,4)||'/'||substr(h.DenDay,5,2)||'/'||substr(h.DenDay,7,2) as item1,
{KeyTextSql("h")} as item2,
ifnull(soko.Code,'') as item3,
ifnull(soko.Name,'') as item4,
ifnull(ten.Code,'') as item5,
ifnull(ten.Name,'') as item6,
ifnull(shain.Code,'') as item7,
ifnull(shain.Name,'') as item8,
ifnull(sh.Code,'') as item9,
ifnull(sh.Name,'') as item10,
ifnull(sku.Code_Col,'') as item11,
ifnull(sku.Mei_Col,'') as item12,
ifnull(sku.Code_Siz,'') as item13,
ifnull(sku.Mei_Siz,'') as item14,
ifnull(nullif(sku.Jan1,''), ifnull(h.JanCode,'')) as item15,
h.Su as item16,
h.Jodai as item17,
(h.Su * h.Jodai) as item18,
{condition} as item19
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(MasterTokui)} soko ON soko.Id = h.Id_Soko
LEFT JOIN {nameof(MasterTokui)} ten ON ten.Id = h.Id_Tenpo
LEFT JOIN {nameof(MasterShain)} shain ON shain.Id = h.Id_Shain
LEFT JOIN {nameof(MasterShohin)} sh ON sh.Id = h.Id_Shohin
LEFT JOIN {nameof(DerivedShohinColSiz)} sku ON sku.Id_Shohin = h.Id_Shohin AND sku.Id_Col = h.Id_Col AND sku.Id_Siz = h.Id_Siz
WHERE {where}
ORDER BY h.Id_Soko, h.Id_Tenpo, h.DenDay, h.Kubun, h.RelateNo1, h.Id";
		return new QueryListSqlParam(typeof(object), sql, [.. parameters]);
	}

	/// <summary>
	/// 伝票キー文字列（CODE39対応: 数字のみ、固定長27桁）。
	/// DenDay(8) + Kubun(1) + Id_Soko(6桁0埋め) + Id_Tenpo(6桁0埋め) + RelateNo1(6桁0埋め)。
	/// HaibunHeaderKey の主要5列（NouhinDayを除く）を連結し、バーコードと見出しの両方に使う。
	/// </summary>
	static string KeyTextSql(string alias) =>
		$"({alias}.DenDay || cast({alias}.Kubun as text) || substr('000000'||{alias}.Id_Soko,-6,6) || substr('000000'||{alias}.Id_Tenpo,-6,6) || substr('000000'||{alias}.RelateNo1,-6,6))";

	static string KubunLabelSql(string alias) => $@"CASE {alias}.Kubun
WHEN 0 THEN '初回配分' WHEN 1 THEN '在庫配分' WHEN 2 THEN '受注配分' WHEN 3 THEN '得意先別配分'
WHEN 4 THEN '店舗出荷依頼' WHEN 5 THEN '在庫品配分' WHEN 6 THEN '取置' WHEN 7 THEN '移動指示'
ELSE cast({alias}.Kubun as text) END";

	string BuildConditionText(DateTime from, DateTime to) {
		var kubun = SelectedKubun < 0 ? "すべて" : (KubunOptions.FirstOrDefault(x => x.Value == SelectedKubun)?.Name ?? SelectedKubun.ToString(CultureInfo.InvariantCulture));
		var soko = BuildRangeText(SokoCodeFrom, SokoCodeTo);
		var tenpo = BuildRangeText(TenpoCodeFrom, TenpoCodeTo);
		var kakutei = KakuteiOnly ? "確定済みのみ" : "全て";
		return $"区分:{kubun} 配分指示日:{from:yyyy/MM/dd}〜{to:yyyy/MM/dd} 出庫倉庫:{soko} 出荷先:{tenpo} {kakutei}";
	}

	static string BuildRangeText(string? from, string? to) {
		var f = (from ?? string.Empty).Trim();
		var t = (to ?? string.Empty).Trim();
		if (f.Length == 0 && t.Length == 0) return "指定なし";
		return $"{(f.Length == 0 ? "先頭" : f)}〜{(t.Length == 0 ? "最後" : t)}";
	}
}
