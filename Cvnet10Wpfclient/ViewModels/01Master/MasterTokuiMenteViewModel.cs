using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewModels.Sub;
using Cvnet10Wpfclient.ViewServices;

namespace Cvnet10Wpfclient.ViewModels._01Master;

/// <summary>
/// 得意先マスターメンテ ViewModel
/// </summary>
public partial class MasterTokuiMenteViewModel : Helpers.BaseMenteViewModel<MasterTokui> {
	// ウィンドウタイトル
	[ObservableProperty]
	string title = "得意先マスターメンテ";

	protected override string? ListWhere => BuildSelectCodeWhere(selectCodeParam);
	protected override string? ListOrder => "Code";
	protected override int? ListMaxCount => selectCodeParam?.MaxCount;

	SelectParameter? selectCodeParam;

	// ---- 初期化 (BaseWindow が InitCommand を呼び出す) ----
	[RelayCommand]
	async Task Init() {
		await DoList(CancellationToken.None);
	}

	// ---- 確認メッセージ ----
	protected override string GetInsertConfirmMessage() =>
		$"追加しますか？ (CD={CurrentEdit.Code})";

	protected override string GetUpdateConfirmMessage() =>
		$"修正しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() =>
		$"削除しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})";

	// ---- 完了メッセージ ----
	protected override void AfterInsert(MasterTokui item) {
		Message = $"追加しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterUpdate(MasterTokui item) {
		Message = $"修正しました (CD={item.Code}, Id={item.Id})";
	}

	protected override void AfterDelete(MasterTokui removedItem) {
		Message = $"削除しました (CD={removedItem.Code}, Id={removedItem.Id})";
	}

	// ---- 一覧取得前にコード範囲ダイアログを表示 ----
	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		if (!TryShowSelectCodeDialog(selectCodeParam, "得意先", out var parameter)) {
			return new ValueTask<bool>(false);
		}

		selectCodeParam = parameter;
		return new ValueTask<bool>(true);
	}

	// ---- 担当者 (MasterShain) 選択 ----
	[RelayCommand]
	void DoSelectShain() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var shain = vm.Current as MasterShain;
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	// ---- 支払方法 (MasterMeisho) 選択 ----
	[RelayCommand]
	void DoSelectPayMethod() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='PAY'", "Code", startPos: CurrentEdit.Id_PayMethod);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		if (meisho == null) return;
		CurrentEdit.Id_PayMethod = meisho.Id;
		CurrentEdit.VPayMethod = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	// ---- 請求先 (MasterTokui 自テーブル) 選択 ----
	[RelayCommand]
	void DoSelectPaysaki() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterTokui), "", "Code", startPos: CurrentEdit.Id_Paysaki);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var tokui = vm.Current as MasterTokui;
		if (tokui == null) return;
		CurrentEdit.Id_Paysaki = tokui.Id;
		CurrentEdit.VPaysaki = new() { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}
}
