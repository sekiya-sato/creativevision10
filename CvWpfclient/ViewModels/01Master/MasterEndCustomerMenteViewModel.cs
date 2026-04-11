using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterEndCustomerMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterEndCustomer> {
	[ObservableProperty]
	string title = "顧客マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Rank", "VTenpo"];

	protected override string? SelectCodeDisplayName => "顧客";

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	[RelayCommand]
	void DoSelectTenpo() {
		var meisho = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code", startPos: CurrentEdit.Id_Tenpo);
		CurrentEdit.Id_Tenpo = meisho?.Id ?? 0;
		CurrentEdit.VTenpo = new() { Sid = meisho?.Id ?? 0, Cd = meisho?.Code ?? "", Mei = meisho?.Name ?? "" };
	}

	[RelayCommand]
	async Task SearchPostalCode() {
		var owner = ClientLib.GetActiveView(this);
		var postalAddressService = App.AppHost?.Services.GetService<IPostalAddressService>();
		if (postalAddressService == null) {
			MessageEx.ShowErrorDialog("郵便番号検索サービスを取得できません。", owner: owner);
			return;
		}

		try {
			var result = await postalAddressService.SearchByPostalCodeAsync(CurrentEdit.PostalCode ?? string.Empty);
			if (!result.IsSuccess) {
				MessageEx.ShowErrorDialog(result.Message, owner: owner);
				return;
			}

			if (result.Items.Count != 1) {
				MessageEx.ShowWarningDialog("該当住所が複数見つかりました。郵便番号を確認してください。", owner: owner);
				return;
			}

			var item = result.Items[0];
			CurrentEdit.PostalCode = item.PostalCode;
			CurrentEdit.Address1 = item.Address1;
			CurrentEdit.Address2 = item.Address2;
			CurrentEdit.Address3 = item.Address3;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"郵便番号検索に失敗しました: {ex.Message}", owner: owner);
		}
	}

}
