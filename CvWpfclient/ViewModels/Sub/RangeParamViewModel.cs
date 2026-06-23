using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class RangeParamViewModel : Helpers.BaseViewModel {
	Type? selectType;
	Type? toriSelectType;
	string selectWhere = string.Empty;
	string selectOrder = "Code";
	string toriSelectWhere = string.Empty;
	string toriSelectOrder = "Code";

	[ObservableProperty]
	SelectParameter parameter = new();

	public void Initialize(SelectParameter? param, Type? tableType = null, string where = "", string order = "Code", Type? toriTableType = null, string toriWhere = "", string toriOrder = "Code") {
		selectType = tableType;
		selectWhere = where;
		selectOrder = order;
		toriSelectType = toriTableType;
		toriSelectWhere = toriWhere;
		toriSelectOrder = toriOrder;
		Parameter = EnsureParameter(param ?? new SelectParameter());
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}

	[RelayCommand]
	void DoSelectIds() {
		if (selectType == null) return;

		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
		vm.SetParam(selectType, selectWhere, selectOrder, selectedIds: Parameter.Ids, startPos: Parameter.Ids.FirstOrDefault());
		if (ClientLib.ShowDialogView(selWin, this) != true) return;

		var selectedRows = vm.ListData?.Where(row => row.IsSelected).ToList() ?? [];
		Parameter = Parameter with {
			Ids = [.. selectedRows.Select(row => row.Id).Where(id => id > 0).Distinct()],
			IdsText = BuildSelectedText(selectedRows)
		};
	}

	[RelayCommand]
	void ClearIds() {
		Parameter = Parameter with {
			Ids = [],
			IdsText = "未選択"
		};
	}

	[RelayCommand]
	void DoSelectToriIds() {
		if (toriSelectType == null) return;

		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
		vm.SetParam(toriSelectType, toriSelectWhere, toriSelectOrder, selectedIds: Parameter.ToriIds, startPos: Parameter.ToriIds.FirstOrDefault());
		if (ClientLib.ShowDialogView(selWin, this) != true) return;

		var selectedRows = vm.ListData?.Where(row => row.IsSelected).ToList() ?? [];
		Parameter = Parameter with {
			ToriIds = [.. selectedRows.Select(row => row.Id).Where(id => id > 0).Distinct()],
			ToriIdsText = BuildSelectedText(selectedRows)
		};
	}

	[RelayCommand]
	void ClearToriIds() {
		Parameter = Parameter with {
			ToriIds = [],
			ToriIdsText = "未選択"
		};
	}

	static SelectParameter EnsureParameter(SelectParameter parameter) {
		if (string.IsNullOrWhiteSpace(parameter.IdsDisplayName)) {
			parameter.IdsDisplayName = parameter.DisplayName;
		}

		if (parameter.Ids.Count == 0) {
			parameter.IdsText = "未選択";
		}
		else if (string.IsNullOrWhiteSpace(parameter.IdsText) || parameter.IdsText == "未選択") {
			parameter.IdsText = $"{parameter.Ids.Count}件";
		}
		if (parameter.ToriIds.Count == 0) {
			parameter.ToriIdsText = "未選択";
		}
		else if (string.IsNullOrWhiteSpace(parameter.ToriIdsText) || parameter.ToriIdsText == "未選択") {
			parameter.ToriIdsText = $"{parameter.ToriIds.Count}件";
		}
		return parameter;
	}

	static string BuildSelectedText(IReadOnlyList<SelectMultiWinItem> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	static string FormatSelectedItem(SelectMultiWinItem item) {
		var label = JoinCodeName(item.Code, item.Name);
		if (label.Length == 0) return item.Id.ToString();
		return $"{item.Id} {label}";
	}

	static string JoinCodeName(string? code, string? name) {
		var cd = code?.Trim() ?? string.Empty;
		var mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}
