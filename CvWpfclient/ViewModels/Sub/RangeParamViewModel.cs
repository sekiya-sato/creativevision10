using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class RangeParamViewModel : Helpers.BaseViewModel {
	Type? selectType;
	Type? toriSelectType;
	Type? additionalIds1SelectType;
	Type? additionalIds2SelectType;
	string selectWhere = string.Empty;
	string selectOrder = "Code";
	string toriSelectWhere = string.Empty;
	string toriSelectOrder = "Code";
	string additionalIds1SelectWhere = string.Empty;
	string additionalIds1SelectOrder = "Code";
	string additionalIds2SelectWhere = string.Empty;
	string additionalIds2SelectOrder = "Code";
	List<CvBase.BaseDbClass>? additionalIds1LocalData;

	[ObservableProperty]
	public partial SelectParameter Parameter { get; set; } = new();

	public bool IsCodeNameFilterVisible { get; private set; }
	public bool IsAdditionalIds1Enabled => additionalIds1SelectType != null || additionalIds1LocalData != null;
	public bool IsAdditionalIds2Enabled => additionalIds2SelectType != null;
	public double AdditionalIds1RowOpacity => IsAdditionalIds1Enabled ? 1.0 : 0.45;
	public double AdditionalIds2RowOpacity => IsAdditionalIds2Enabled ? 1.0 : 0.45;

	public void Initialize(
		SelectParameter? param,
		Type? tableType = null,
		string where = "",
		string order = "Code",
		Type? toriTableType = null,
		string toriWhere = "",
		string toriOrder = "Code",
		Type? additionalIds1TableType = null,
		string additionalIds1Label = "複数Id 1",
		string additionalIds1Where = "",
		string additionalIds1Order = "Code",
		string? additionalIds1Column = null,
		IEnumerable<CvBase.BaseDbClass>? additionalIds1LocalData = null,
		Type? additionalIds2TableType = null,
		string additionalIds2Label = "複数Id 2",
		string additionalIds2Where = "",
		string additionalIds2Order = "Code",
		string? additionalIds2Column = null,
		bool? isCodeNameFilterVisible = null) {
		selectType = tableType;
		selectWhere = where;
		selectOrder = order;
		toriSelectType = toriTableType;
		toriSelectWhere = toriWhere;
		toriSelectOrder = toriOrder;
		additionalIds1SelectType = additionalIds1TableType;
		additionalIds1SelectWhere = additionalIds1Where;
		additionalIds1SelectOrder = additionalIds1Order;
		this.additionalIds1LocalData = additionalIds1LocalData?.ToList();
		additionalIds2SelectType = additionalIds2TableType;
		additionalIds2SelectWhere = additionalIds2Where;
		additionalIds2SelectOrder = additionalIds2Order;
		IsCodeNameFilterVisible = isCodeNameFilterVisible ?? (tableType != null && typeof(CvBase.Share.IBaseCodeName).IsAssignableFrom(tableType));

		var ensuredParameter = EnsureParameter(param ?? new SelectParameter());
		if (ShouldApplyAdditionalLabel(additionalIds1Label, "複数Id 1", ensuredParameter.AdditionalIds1Label)) {
			ensuredParameter.AdditionalIds1Label = additionalIds1Label;
		}
		if (ShouldApplyAdditionalLabel(additionalIds2Label, "複数Id 2", ensuredParameter.AdditionalIds2Label)) {
			ensuredParameter.AdditionalIds2Label = additionalIds2Label;
		}
		if (additionalIds1TableType != null || this.additionalIds1LocalData != null) {
			ensuredParameter.AdditionalIds1Column = string.IsNullOrWhiteSpace(additionalIds1Column) ? ensuredParameter.AdditionalIds1Column : additionalIds1Column;
		}
		if (additionalIds2TableType != null) {
			ensuredParameter.AdditionalIds2Column = string.IsNullOrWhiteSpace(additionalIds2Column) ? ensuredParameter.AdditionalIds2Column : additionalIds2Column;
		}

		Parameter = ensuredParameter;
		OnPropertyChanged(nameof(IsAdditionalIds1Enabled));
		OnPropertyChanged(nameof(IsAdditionalIds2Enabled));
		OnPropertyChanged(nameof(AdditionalIds1RowOpacity));
		OnPropertyChanged(nameof(AdditionalIds2RowOpacity));
		OnPropertyChanged(nameof(IsCodeNameFilterVisible));
	}

	static bool ShouldApplyAdditionalLabel(string label, string defaultLabel, string currentLabel) =>
		!string.IsNullOrWhiteSpace(label)
			&& (!string.Equals(label, defaultLabel, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(currentLabel));

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

	[RelayCommand]
	void DoSelectAdditionalIds1() {
		if (additionalIds1LocalData != null) {
			var selWin = new Views.Sub.SelectMultiWinView();
			if (selWin.DataContext is not SelectMultiWinViewModel vm) return;
			vm.SetLocalData(additionalIds1LocalData, title: Parameter.AdditionalIds1Label, selectedIds: Parameter.AdditionalIds1);
			if (ClientLib.ShowDialogView(selWin, this) != true) return;
			var selectedRows = vm.ListData?.Where(row => row.IsSelected).ToList() ?? [];
			Parameter = Parameter with {
				AdditionalIds1 = [.. selectedRows.Select(row => row.Id).Where(id => id >= 0).Distinct()],
				AdditionalIds1Text = BuildSelectedText(selectedRows)
			};
			return;
		}

		if (additionalIds1SelectType == null) return;

		var result = SelectIds(additionalIds1SelectType, additionalIds1SelectWhere, additionalIds1SelectOrder, Parameter.AdditionalIds1);
		if (result == null) return;

		Parameter = Parameter with {
			AdditionalIds1 = result.Value.ids,
			AdditionalIds1Text = result.Value.text
		};
	}

	[RelayCommand]
	void ClearAdditionalIds1() {
		Parameter = Parameter with {
			AdditionalIds1 = [],
			AdditionalIds1Text = "未選択"
		};
	}

	[RelayCommand]
	void DoSelectAdditionalIds2() {
		if (additionalIds2SelectType == null) return;

		var result = SelectIds(additionalIds2SelectType, additionalIds2SelectWhere, additionalIds2SelectOrder, Parameter.AdditionalIds2);
		if (result == null) return;

		Parameter = Parameter with {
			AdditionalIds2 = result.Value.ids,
			AdditionalIds2Text = result.Value.text
		};
	}

	[RelayCommand]
	void ClearAdditionalIds2() {
		Parameter = Parameter with {
			AdditionalIds2 = [],
			AdditionalIds2Text = "未選択"
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
		if (parameter.AdditionalIds1.Count == 0) {
			parameter.AdditionalIds1Text = "未選択";
		}
		else if (string.IsNullOrWhiteSpace(parameter.AdditionalIds1Text) || parameter.AdditionalIds1Text == "未選択") {
			parameter.AdditionalIds1Text = $"{parameter.AdditionalIds1.Count}件";
		}
		if (parameter.AdditionalIds2.Count == 0) {
			parameter.AdditionalIds2Text = "未選択";
		}
		else if (string.IsNullOrWhiteSpace(parameter.AdditionalIds2Text) || parameter.AdditionalIds2Text == "未選択") {
			parameter.AdditionalIds2Text = $"{parameter.AdditionalIds2.Count}件";
		}
		return parameter;
	}

	(List<long> ids, string text)? SelectIds(Type tableType, string where, string order, IReadOnlyList<long> selectedIds) {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, selectedIds: selectedIds, startPos: selectedIds.FirstOrDefault());
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;

		var selectedRows = vm.ListData?.Where(row => row.IsSelected).ToList() ?? [];
		return ([.. selectedRows.Select(row => row.Id).Where(id => id > 0).Distinct()], BuildSelectedText(selectedRows));
	}

	static string BuildSelectedText(IReadOnlyList<SelectMultiWinItem> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatSelectedItem(SelectMultiWinItem item) =>
		CodeNameDisplay.Format(item.Id, item.Code, item.Name);
}
