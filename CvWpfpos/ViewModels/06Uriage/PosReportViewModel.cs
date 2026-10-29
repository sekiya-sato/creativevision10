using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Services;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

public enum PosReportType {
	DailySummary,
	ProductRanking,
	StaffSummary
}

public sealed class ReportTypeItem(PosReportType value, string displayName) {
	public PosReportType Value { get; } = value;
	public string DisplayName { get; } = displayName;
	public override string ToString() => DisplayName;
}

public sealed class ReportRow {
	public string Key1 { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public int Count { get; set; }
	public long Quantity { get; set; }
	public long Amount { get; set; }
}

/// <summary>
/// 各種レポート画面ViewModel。
/// 日次サマリー・商品別ランキング・担当者別の3種類のレポートを
/// QueryListAsync&lt;Tran01Tenuri&gt; で取得しクライアント側で集計する。
/// </summary>
public partial class PosReportViewModel : ObservableObject {
	private readonly PosSettings settings;
	private readonly PosGrpcClient client;

	public IReadOnlyList<ReportTypeItem> ReportTypeItems { get; } =
	[
		new(PosReportType.DailySummary, "日次サマリー"),
		new(PosReportType.ProductRanking, "商品別ランキング"),
		new(PosReportType.StaffSummary, "担当者別")
	];

	[ObservableProperty] public partial PosReportType SelectedReportType { get; set; } = PosReportType.DailySummary;
	[ObservableProperty] public partial DateTime FromDate { get; set; } = DateTime.Today.AddDays(-7);
	[ObservableProperty] public partial DateTime ToDate { get; set; } = DateTime.Today;
	[ObservableProperty] public partial ObservableCollection<ReportRow> ReportRows { get; set; } = [];
	[ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
	[ObservableProperty] public partial bool IsBusy { get; set; }

	public event EventHandler? RequestClose;

	public PosReportViewModel() : this(AppGlobal.Settings, AppGlobal.Client) { }

	public PosReportViewModel(PosSettings settings, PosGrpcClient client) {
		this.settings = settings;
		this.client = client;
	}

	[RelayCommand]
	private async Task Init(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Load(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

	private async Task LoadAsync(CancellationToken cancellationToken) {
		IsBusy = true;
		try {
			var fromDay = FromDate.ToString("yyyyMMdd");
			var toDay = ToDate.AddDays(1).ToString("yyyyMMdd");
			var sales = await client.QueryListAsync<Tran01Tenuri>(
				"DenDay>=@0 and DenDay<@1 and Id_Tenpo=@2", "DenDay,Id",
				[fromDay, toDay, settings.StoreId.ToString()], 10000, cancellationToken);

			ReportRows.Clear();
			switch (SelectedReportType) {
				case PosReportType.DailySummary:
					GenerateDailySummary(sales);
					break;
				case PosReportType.ProductRanking:
					GenerateProductRanking(sales);
					break;
				case PosReportType.StaffSummary:
					GenerateStaffSummary(sales);
					break;
			}

			StatusMessage = $"レポートを生成しました（{sales.Count} 件の伝票から）。";
		}
		catch (OperationCanceledException) { StatusMessage = "レポート生成を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"レポート生成エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	private void GenerateDailySummary(List<Tran01Tenuri> sales) {
		var groups = sales.GroupBy(s => s.DenDay)
			.Select(g => new ReportRow {
				Key1 = g.Key,
				Label = $"{g.Key.AsSpan(0, 4)}/{g.Key.AsSpan(4, 2)}/{g.Key.AsSpan(6, 2)}",
				Count = g.Count(),
				Quantity = g.Sum(s => s.SuTotal * s.CalcFlag),
				Amount = g.Sum(s => s.Total * s.CalcFlag)
			})
			.OrderBy(r => r.Key1);
		foreach (var row in groups) ReportRows.Add(row);
	}

	private void GenerateProductRanking(List<Tran01Tenuri> sales) {
		var lines = sales.SelectMany(s => s.Jmeisai ?? [], (header, line) => (header, line))
			.Where(t => t.line != null);
		var groups = lines
			.GroupBy(t => new { t.line.Code_Shohin, t.line.Mei_Shohin })
			.Select(g => new ReportRow {
				Key1 = g.Key.Code_Shohin,
				Label = g.Key.Mei_Shohin,
				Count = g.Count(),
				Quantity = g.Sum(t => t.line.Su * t.header.CalcFlag),
				Amount = g.Sum(t => t.line.Kingaku * t.header.CalcFlag)
			})
			.OrderByDescending(r => r.Amount)
			.Take(100);
		foreach (var row in groups) ReportRows.Add(row);
	}

	private void GenerateStaffSummary(List<Tran01Tenuri> sales) {
		var lines = sales.SelectMany(s => s.Jmeisai ?? [], (header, line) => (header, line))
			.Where(t => t.line != null);
		var groups = lines
			.GroupBy(t => new { t.line.Code_Shain, t.line.Mei_Shain })
			.Select(g => new ReportRow {
				Key1 = g.Key.Code_Shain,
				Label = g.Key.Mei_Shain,
				Count = g.Count(),
				Quantity = g.Sum(t => t.line.Su * t.header.CalcFlag),
				Amount = g.Sum(t => t.line.Kingaku * t.header.CalcFlag)
			})
			.OrderByDescending(r => r.Amount);
		foreach (var row in groups) ReportRows.Add(row);
	}

	[RelayCommand]
	private void Exit() => RequestClose?.Invoke(this, EventArgs.Empty);
}
