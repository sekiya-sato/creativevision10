using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Base;

namespace Cvnet10Wpfclient.ViewModels._01Master;

/// <summary>
/// 得意先マスターメンテ ViewModel
/// </summary>
public partial class MasterTokuiMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterTokui> {
	[ObservableProperty]
	string title = "得意先マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["TenType"];

	protected override string? SelectCodeDisplayName => "得意先";

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	// ---- 担当者 (MasterShain) 選択 ----
	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		CurrentEdit.VShain = new() { Sid = shain.Id, Cd = shain.Code ?? "", Mei = shain.Name ?? "" };
	}

	// ---- 支払方法 (MasterMeisho) 選択 ----
	[RelayCommand]
	void DoSelectPayMethod() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='PAY'", "Code", startPos: CurrentEdit.Id_PayMethod);
		if (meisho == null) return;
		CurrentEdit.Id_PayMethod = meisho.Id;
		CurrentEdit.VPayMethod = new() { Sid = meisho.Id, Cd = meisho.Code ?? "", Mei = meisho.Name ?? "" };
	}

	// ---- 請求先 (MasterTokui 自テーブル) 選択 ----
	[RelayCommand]
	void DoSelectPaysaki() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "", "Code", startPos: CurrentEdit.Id_Paysaki);
		if (tokui == null) return;
		CurrentEdit.Id_Paysaki = tokui.Id;
		CurrentEdit.VPaysaki = new() { Sid = tokui.Id, Cd = tokui.Code ?? "", Mei = tokui.Name ?? "" };
	}
}
