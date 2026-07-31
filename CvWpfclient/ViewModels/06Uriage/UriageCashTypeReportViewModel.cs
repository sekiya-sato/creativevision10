using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 売上金種Viewer。店舗売上(Tran01Tenuri)の決済内訳を金種別に集計して画面表示する。
/// POSの締めと売上金額の突き合わせに使う。
///
/// 金種は Tran01Tenuri.JposPayment(JSON) に入っている POS の決済内訳
/// （現金 CashAmount / カード CardAmount / その他 OtherAmount / 釣銭 ChangeAmount）。
/// 集計は SQL の json_extract で行い、結果を SummaryUriKake の列へ割り当てて運ぶ
/// （Msg101 は任意列を返せないため。BaseQueryViewModel のコメント参照）。
///
/// 【注意】JposPayment はPOS経由で登録された売上にしか入らない。
/// 手入力の売上伝票では金種が空になるため、「金種計」と「売上金額」が一致しないことがある。
/// その差額を確認できるように、売上金額と金種計の両方と差額を並べて出す。
/// </summary>
public partial class UriageCashTypeReportViewModel : Helpers.BaseQueryViewModel {
	protected override string QueryTitle => "売上金種Viewer";

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = DateTime.Today.ToString("yyyy/MM/dd");

	[ObservableProperty]
	public partial string ShopCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShopCodeTo { get; set; } = string.Empty;

	/// <summary>集計単位。true=店舗×日 / false=店舗計。</summary>
	[ObservableProperty]
	public partial bool IsByDay { get; set; } = true;

	/// <summary>true=金種が未設定（手入力）の伝票も行に含める</summary>
	[ObservableProperty]
	public partial bool IncludeNoPayment { get; set; } = true;

	[ObservableProperty]
	public partial ObservableCollection<CashTypeRow> Rows { get; set; } = [];

	[ObservableProperty]
	public partial CashTypeRow? SelectedRow { get; set; }

	[ObservableProperty]
	public partial int RowCount { get; set; }

	[ObservableProperty]
	public partial int TotalKingaku { get; set; }

	[ObservableProperty]
	public partial int TotalPayment { get; set; }

	/// <summary>売上金額と金種計の差額合計。0でなければ金種未設定の伝票がある。</summary>
	[ObservableProperty]
	public partial int TotalDiff { get; set; }

	[RelayCommand]
	void SelectShopCodeFrom() => ShopCodeFrom = SelectShopCode() ?? ShopCodeFrom;

	[RelayCommand]
	void SelectShopCodeTo() => ShopCodeTo = SelectShopCode() ?? ShopCodeTo;

	protected override void OnClearConditions() {
		DenDayFrom = DateTime.Today.ToString("yyyy/MM/dd");
		DenDayTo = DateTime.Today.ToString("yyyy/MM/dd");
		ShopCodeFrom = string.Empty;
		ShopCodeTo = string.Empty;
		IsByDay = true;
		IncludeNoPayment = true;
		Rows = [];
		RowCount = 0;
		TotalKingaku = 0;
		TotalPayment = 0;
		TotalDiff = 0;
	}

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (!TryParseDate(DenDayFrom, out var from) || !TryParseDate(DenDayTo, out var to)) return;
		if (from > to) {
			MessageEx.ShowWarningDialog("売上日の範囲が逆転しています。", owner: ActiveWindow);
			return;
		}
		if (!TryGetMaxCount(out var maxCount)) return;

		List<string> parameters = [];
		var where = $"h.DenDay >= {AddSqlParameter(parameters, ToDenDay(from))}"
			+ $" AND h.DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, "ifnull(json_extract(h.VTenpo,'$.Cd'),'')", ShopCodeFrom, ShopCodeTo);
		if (!IncludeNoPayment) {
			where += " AND json_valid(h.JposPayment) AND h.JposPayment != ''";
		}

		// 金種の各項目。JposPayment が空/不正なら 0 とみなす（json_valid でガード）
		string Pay(string prop) =>
			$"CAST(CASE WHEN json_valid(h.JposPayment) THEN ifnull(json_extract(h.JposPayment,'$.{prop}'),0) ELSE 0 END AS INTEGER)";

		// 集計キー。店舗計のときは日付を潰す
		var dayKey = IsByDay ? "h.DenDay" : "''";

		// 結果は SummaryUriKake の列へ割り当てて運ぶ:
		//   DenMonth=日付 / Uriage=売上金額 / Cash=現金 / Fee=カード / Other=その他 / Densai=釣銭
		//   TotalSales=伝票数 / Offset=金種計 / Balance=差額 / Id_Tokui=店舗Id
        var sql = $@"
SELECT
    0 AS Id, 0 AS Vdc, 0 AS Vdu,
    h.Id_Tenpo                  AS Id_Tokui,
    {dayKey}                    AS DenMonth,
    COUNT(*)                    AS TotalSales,
    SUM(h.KingakuTotal)         AS Uriage,
    SUM({Pay("CashAmount")})    AS Cash,
    SUM({Pay("CardAmount")})    AS Fee,
    SUM({Pay("OtherAmount")})   AS Other,
    SUM({Pay("ChangeAmount")})  AS Densai,
    SUM({Pay("CashAmount")} + {Pay("CardAmount")} + {Pay("OtherAmount")} - {Pay("ChangeAmount")}) AS Offset,
    SUM(h.KingakuTotal)
      - SUM({Pay("CashAmount")} + {Pay("CardAmount")} + {Pay("OtherAmount")} - {Pay("ChangeAmount")}) AS Balance,
    0 AS TotalIn, 0 AS Henpin, 0 AS Nebiki, 0 AS Tax
FROM Tran01Tenuri h
WHERE {where}
GROUP BY h.Id_Tenpo, {dayKey}
ORDER BY {dayKey}, h.Id_Tenpo
LIMIT {maxCount}";

		var agg = await QuerySqlListAsync<SummaryUriKake>(sql, parameters, ct);
		if (agg.Count == 0) {
			Rows = [];
			RowCount = 0;
			TotalKingaku = 0;
			TotalPayment = 0;
			TotalDiff = 0;
			Message = "該当する売上がありません";
			return;
		}

		var shopMap = await LoadShopsAsync(agg.Select(x => x.Id_Tokui), ct);

		ObservableCollection<CashTypeRow> rows = [];
		foreach (var a in agg) {
			shopMap.TryGetValue(a.Id_Tokui, out var shop);
			rows.Add(new CashTypeRow {
				DenDayLabel = FormatDay(a.DenMonth),
				ShopCode = shop?.Code ?? string.Empty,
				ShopName = shop?.Name ?? string.Empty,
				DenCount = (int)a.TotalSales,
				Kingaku = (int)a.Uriage,
				CashAmount = (int)a.Cash,
				CardAmount = (int)a.Fee,
				OtherAmount = (int)a.Other,
				ChangeAmount = (int)a.Densai,
				PaymentTotal = (int)a.Offset,
				Diff = (int)a.Balance,
			});
		}

		Rows = rows;
		RowCount = rows.Count;
		TotalKingaku = rows.Sum(x => x.Kingaku);
		TotalPayment = rows.Sum(x => x.PaymentTotal);
		TotalDiff = rows.Sum(x => x.Diff);
		SelectedRow = rows.FirstOrDefault();
		Message = TotalDiff == 0
			? $"{DateTime.Now:MM/dd HH:mm:ss} {RowCount:N0}件 売上金額と金種計が一致しています（{TotalKingaku:N0}）"
			: $"{DateTime.Now:MM/dd HH:mm:ss} {RowCount:N0}件 売上 {TotalKingaku:N0} / 金種計 {TotalPayment:N0} / 差額 {TotalDiff:N0}（金種未設定の伝票があります）";
	}

	async Task<Dictionary<long, MasterTokui>> LoadShopsAsync(IEnumerable<long> shopIds, CancellationToken ct) {
		var ids = shopIds.Distinct().ToList();
		if (ids.Count == 0) return [];
		// Id は内部生成値なので IN 句へ直接埋め込む
		var list = await QuerySqlListAsync<MasterTokui>($@"
SELECT Id, Vdc, Vdu, Code, Name FROM MasterTokui WHERE Id IN ({string.Join(",", ids)})", [], ct);
		return list.ToDictionary(x => x.Id);
	}

	static string FormatDay(string? yyyymmdd) =>
		yyyymmdd is { Length: 8 }
			? $"{yyyymmdd[..4]}/{yyyymmdd[4..6]}/{yyyymmdd[6..]}"
			: "(期間計)";
}

/// <summary>売上金種Viewerの1行</summary>
public sealed class CashTypeRow {
	public string DenDayLabel { get; set; } = string.Empty;
	public string ShopCode { get; set; } = string.Empty;
	public string ShopName { get; set; } = string.Empty;
	public int DenCount { get; set; }
	/// <summary>伝票の売上金額合計</summary>
	public int Kingaku { get; set; }
	public int CashAmount { get; set; }
	public int CardAmount { get; set; }
	public int OtherAmount { get; set; }
	/// <summary>釣銭（金種計から差し引く）</summary>
	public int ChangeAmount { get; set; }
	/// <summary>金種計 = 現金 + カード + その他 − 釣銭</summary>
	public int PaymentTotal { get; set; }
	/// <summary>売上金額 − 金種計。0でなければ金種未設定の伝票がある。</summary>
	public int Diff { get; set; }
}
