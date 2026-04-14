using CodeShare;

namespace CvWpfclient.Helpers;

public static class PostalAddressSearchHelper {
	public static async Task SearchAndApplyAsync(object viewModel, string postalCode, Action<PostalAddressItem> applyAddress) {
		var owner = ClientLib.GetActiveView(viewModel);
		var postalAddressService = AppGlobal.GetGrpcService<IPostalAddressService>();
		if (postalAddressService == null) {
			MessageEx.ShowErrorDialog("郵便番号検索サービスを取得できません。", owner: owner);
			return;
		}

		try {
			var result = await postalAddressService.SearchByPostalCodeAsync(postalCode ?? string.Empty);
			if (!result.IsSuccess) {
				MessageEx.ShowErrorDialog(result.Message, owner: owner);
				return;
			}

			if (result.Items.Count != 1) {
				MessageEx.ShowWarningDialog("該当住所が複数見つかりました。郵便番号を確認してください。", owner: owner);
				return;
			}

			applyAddress(result.Items[0]);
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"郵便番号検索に失敗しました: {ex.Message}", owner: owner);
		}
	}
}
