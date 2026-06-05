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
	string title = "自動実行履歴";

	AutoExecHistorySelectParameter? selectParam;

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
		var selWin = new Views.Sub.AutoExecHistoryParamMiniView();
		if (selWin.DataContext is not AutoExecHistoryParamMiniViewModel vm)
			return new ValueTask<bool>(true);
		vm.Initialize(selectParam ?? new AutoExecHistorySelectParameter { DisplayName = "自動実行履歴", MaxCount = 400 });
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

	static string? BuildWhereClause(AutoExecHistorySelectParameter? param) {
		if (param == null) return null;

		List<string> clauses = [];
		if (param.FromId.HasValue) {
			clauses.Add($"Id >= {param.FromId.Value}");
		}
		if (param.ToId.HasValue) {
			clauses.Add($"Id <= {param.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(param.FromStartTime)) {
			clauses.Add($"StartTime >= '{EscapeSqlLiteral(param.FromStartTime)}000000'");
		}
		if (!string.IsNullOrWhiteSpace(param.ToStartTime)) {
			clauses.Add($"StartTime <= '{EscapeSqlLiteral(param.ToStartTime)}235959'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static AutoExecHistorySelectParameter NormalizeParameter(AutoExecHistorySelectParameter? param) {
		var normalized = new AutoExecHistorySelectParameter {
			FromId = param?.FromId,
			ToId = param?.ToId,
			FromStartTime = string.IsNullOrWhiteSpace(param?.FromStartTime) ? null : param.FromStartTime,
			ToStartTime = string.IsNullOrWhiteSpace(param?.ToStartTime) ? null : param.ToStartTime,
			MaxCount = param?.MaxCount,
			DisplayName = string.IsNullOrWhiteSpace(param?.DisplayName) ? "自動実行履歴" : param.DisplayName
		};
		// デフォルト件数を400に設定
		if (!normalized.MaxCount.HasValue) {
			normalized.MaxCount = 400;
		}
		return normalized;
	}
}
