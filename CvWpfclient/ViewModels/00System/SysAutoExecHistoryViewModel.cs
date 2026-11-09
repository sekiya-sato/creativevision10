using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;

namespace CvWpfclient.ViewModels._00System;

internal partial class SysAutoExecHistoryViewModel : Helpers.BaseMenteViewModel<SysHistAutoexec> {
	[ObservableProperty]
	public partial string Title { get; set; } = "自動実行履歴";

	SimpleRangeParameter? selectParam;

	/// <summary>最新順に取得</summary>
	protected override string? ListOrder => "Id DESC";

	protected override int? ListMaxCount => selectParam?.MaxCount;

	protected override string? ListWhere => BuildWhereClause(selectParam);

	[RelayCommand]
	public async Task Init() {
		await DoList(CancellationToken.None);
	}

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var selWin = new Views.Sub.SimpleRangeParamView();
		if (selWin.DataContext is not SimpleRangeParamViewModel vm)
			return new ValueTask<bool>(true);
		vm.Initialize(selectParam ?? new SimpleRangeParameter { DisplayName = "自動実行履歴", DateLabel = "開始日", IsDateVisible = true, MaxCount = AppGlobal.Limit });
		if (ClientLib.ShowDialogView(selWin, this, true) != true) {
			selectParam = vm.Parameter;
			return new ValueTask<bool>(false);
		}
		selectParam = NormalizeParameter(vm.Parameter);
		return new ValueTask<bool>(true);
	}

	/// <summary>履歴テーブルは修正不可</summary>
	protected override bool CanUpdate() => false;

	/// <summary>履歴テーブルは削除不可</summary>
	protected override bool CanDelete() => false;

	protected override void AfterList(IList list) {
		Message = $"リスト取得しました (件数={list.Count}, 取得時間 {StartTime.ToDtStrTime()} // {GetListTime.ToStrSpan()})";
	}

	static string? BuildWhereClause(SimpleRangeParameter? param) {
		if (param == null) return null;

		List<string> clauses = [];
		if (param.FromId.HasValue) {
			clauses.Add($"Id >= {param.FromId.Value}");
		}
		if (param.ToId.HasValue) {
			clauses.Add($"Id <= {param.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(param.FromDate)) {
			clauses.Add($"StartTime >= '{EscapeSqlLiteral(param.FromDate)}000000'");
		}
		if (!string.IsNullOrWhiteSpace(param.ToDate)) {
			clauses.Add($"StartTime <= '{EscapeSqlLiteral(param.ToDate)}235959'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static SimpleRangeParameter NormalizeParameter(SimpleRangeParameter? param) {
		var normalized = new SimpleRangeParameter {
			FromId = param?.FromId,
			ToId = param?.ToId,
			FromDate = string.IsNullOrWhiteSpace(param?.FromDate) ? null : param.FromDate,
			ToDate = string.IsNullOrWhiteSpace(param?.ToDate) ? null : param.ToDate,
			MaxCount = param?.MaxCount,
			DisplayName = string.IsNullOrWhiteSpace(param?.DisplayName) ? "自動実行履歴" : param.DisplayName,
			DateLabel = "開始日",
			IsDateVisible = true
		};
		return normalized;
	}
}
