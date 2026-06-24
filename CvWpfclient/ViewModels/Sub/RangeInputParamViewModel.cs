using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class RangeInputParamViewModel : Helpers.BaseMenteViewModel<TranAllHeader> {
	[ObservableProperty]
	SelectInputParameter parameter = new();

	public void Initialize(SelectInputParameter? param) {
		Parameter = param ?? new SelectInputParameter();
	}

	[RelayCommand]
	void Ok() {
		if (RequiresDirectTableCondition()) {
			MessageEx.ShowWarningDialog(BuildDirectTableConditionMessage(), owner: ActiveWindow);
			return;
		}
		ClientLib.ExitDialogResult(this, true);
	}

	bool RequiresDirectTableCondition() =>
		HasMeisaiJsonCondition() && !HasDirectTableCondition();

	bool HasMeisaiJsonCondition() =>
		Parameter.ShohinIds.Any(id => id > 0)
		|| !string.IsNullOrWhiteSpace(Parameter.InputBarcode)
		|| !string.IsNullOrWhiteSpace(Parameter.ShohinNameLike);

	bool HasDirectTableCondition() =>
		Parameter.FromId.HasValue
		|| Parameter.ToId.HasValue
		|| !string.IsNullOrWhiteSpace(Parameter.FromDate)
		|| !string.IsNullOrWhiteSpace(Parameter.ToDate)
		|| Parameter.ToriIds.Any(id => id > 0)
		|| Parameter.SokoIds.Any(id => id > 0);

	string BuildDirectTableConditionMessage() =>
		$"商品Id・入力バーコード・商品名を条件にする場合は、{BuildDirectTableConditionLabels()} から少なくとも1つは指定してください。";

	string BuildDirectTableConditionLabels() {
		List<string> labels = ["伝票No", "日付"];
		if (Parameter.IsToriVisible) {
			labels.Add(string.IsNullOrWhiteSpace(Parameter.ToriLabel) ? "店舗Id" : Parameter.ToriLabel);
		}
		labels.Add("倉庫Id");
		return string.Join("・", labels);
	}

	[RelayCommand]
	void DoSelectToriIds() {
		var where = Parameter.ToriSearchWhere ?? "TenType>=0";
		var selected = ShowMultiSelectDialog<MasterTokui>(typeof(MasterTokui), where, "Code", Parameter.ToriIds);
		if (selected == null) return;
		Parameter.ToriIds = [.. selected.Select(x => x.Id)];
		Parameter.ToriIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearToriIds() {
		Parameter.ToriIds = [];
		Parameter.ToriIdsText = "未選択";
	}

	[RelayCommand]
	void DoSelectSokoIds() {
		var selected = ShowMultiSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", Parameter.SokoIds);
		if (selected == null) return;
		Parameter.SokoIds = [.. selected.Select(x => x.Id)];
		Parameter.SokoIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearSokoIds() {
		Parameter.SokoIds = [];
		Parameter.SokoIdsText = "未選択";
	}

	[RelayCommand]
	void DoSelectShohinIds() {
		var selected = ShowMultiSelectDialog<MasterShohin>(typeof(MasterShohin), string.Empty, "Code", Parameter.ShohinIds);
		if (selected == null) return;
		Parameter.ShohinIds = [.. selected.Select(x => x.Id)];
		Parameter.ShohinIdsText = BuildShohinSelectedText(selected);
	}

	[RelayCommand]
	void ClearShohinIds() {
		Parameter.ShohinIds = [];
		Parameter.ShohinIdsText = "未選択";
	}

	static string BuildSelectedText(IReadOnlyList<MasterTokui> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	static string FormatSelectedItem(MasterTokui item) {
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

	static string BuildShohinSelectedText(IReadOnlyList<MasterShohin> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatShohinItem))}";
	}

	static string FormatShohinItem(MasterShohin item) {
		var label = JoinCodeName(item.Code, item.Name);
		if (label.Length == 0) return item.Id.ToString(CultureInfo.InvariantCulture);
		return $"{item.Id} {label}";
	}
}
