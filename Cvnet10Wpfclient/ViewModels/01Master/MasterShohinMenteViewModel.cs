using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterShohinMenteViewModel : Helpers.BaseMenteViewModel<MasterShohin> {
	[ObservableProperty]
	string title = "商品マスターメンテ";

	protected override string? ListWhere => BuildSelectCodeWhere(selectCodeParam);
	protected override string? ListOrder => "Code";

	SelectCodeParameter? selectCodeParam;

	protected override int? ListMaxCount => selectCodeParam?.MaxCount;

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

	protected override void AfterInsert(MasterShohin item) {
		Message = $"追加しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterUpdate(MasterShohin item) {
		Message = $"修正しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterDelete(MasterShohin removedItem) {
		Message = $"削除しました (CD={removedItem.Code}, Id={removedItem.Id})";
	}

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		if (!TryShowSelectCodeDialog(selectCodeParam, "商品", out var parameter)) {
			return new ValueTask<bool>(false);
		}

		selectCodeParam = parameter;
		return new ValueTask<bool>(true);
	}
}
