using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;
using Cvnet10Wpfclient.Views.Sub;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterEndCustomerMenteViewModel : Helpers.BaseMenteViewModel<MasterEndCustomer> {
	[ObservableProperty]
	string title = "顧客マスターメンテ";

	protected override string? ListWhere => BuildRangeWhere();
	protected override string? ListOrder => "Code";

	string? rangeFromId;
	string? rangeToId;
	string? rangeFromCode;
	string? rangeToCode;
	string? rangeName;
	string? rangeMaxCount;

	protected override int? ListMaxCount => string.IsNullOrWhiteSpace(rangeMaxCount) ? null : int.TryParse(rangeMaxCount, out var mc) ? mc : null;

	[RelayCommand]
	async Task Init() {
		await DoList(CancellationToken.None);
	}

	protected override string GetInsertConfirmMessage() =>
		$"追加しますか？ (CD={CurrentEdit.Code})";

	protected override string GetUpdateConfirmMessage() =>
		$"修正しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() =>
		$"削除しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	protected override void AfterInsert(MasterEndCustomer item) {
		Message = $"追加しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterUpdate(MasterEndCustomer item) {
		Message = $"修正しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterDelete(MasterEndCustomer removedItem) {
		Message = $"削除しました (CD={removedItem.Code}, Id={removedItem.Id})";
	}

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var selWin = new SelectCodeView();
		if (selWin.DataContext is not SelectCodeViewModel vm) {
			return new ValueTask<bool>(true);
		}
		vm.Initialize(rangeFromId, rangeToId, rangeFromCode, rangeToCode, "顧客", rangeName);
		var dialogResult = ClientLib.ShowDialogView(selWin, this, true);
		if (dialogResult != true) {
			return new ValueTask<bool>(false);
		}
		rangeFromId = string.IsNullOrWhiteSpace(vm.FromId) ? null : vm.FromId;
		rangeToId = string.IsNullOrWhiteSpace(vm.ToId) ? null : vm.ToId;
		rangeFromCode = string.IsNullOrWhiteSpace(vm.FromCode) ? null : vm.FromCode;
		rangeToCode = string.IsNullOrWhiteSpace(vm.ToCode) ? null : vm.ToCode;
		rangeName = string.IsNullOrWhiteSpace(vm.Name) ? null : vm.Name;
		rangeMaxCount = string.IsNullOrWhiteSpace(vm.MaxCount) ? null : vm.MaxCount;
		return new ValueTask<bool>(true);
	}

	[RelayCommand]
	void DoSelectTenpo() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "TenType=6", "Code");
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterTokui;
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	string? BuildRangeWhere() {
		var clauses = new List<string>();
		if (!string.IsNullOrWhiteSpace(rangeFromId)) {
			if (long.TryParse(rangeFromId, out var fromIdVal)) {
				clauses.Add($"Id >= {fromIdVal}");
			}
		}
		if (!string.IsNullOrWhiteSpace(rangeToId)) {
			if (long.TryParse(rangeToId, out var toIdVal)) {
				clauses.Add($"Id <= {toIdVal}");
			}
		}
		if (!string.IsNullOrWhiteSpace(rangeFromCode)) {
			clauses.Add($"Code >= '{Escape(rangeFromCode)}'");
		}
		if (!string.IsNullOrWhiteSpace(rangeToCode)) {
			clauses.Add($"Code <= '{Escape(rangeToCode)}'");
		}
		if (!string.IsNullOrWhiteSpace(rangeName)) {
			clauses.Add($"Name LIKE '%{Escape(rangeName)}%'");
		}
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static string Escape(string value) => value.Replace("'", "''");
}
