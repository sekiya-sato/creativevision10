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

	public static string MergeAddress3(string? currentAddress1, string? currentAddress2, string? currentAddress3, PostalAddressItem item) {
		var currentAddress12 = $"{currentAddress1 ?? string.Empty}{currentAddress2 ?? string.Empty}";
		var currentAddress3Value = currentAddress3 ?? string.Empty;
		var currentFullAddress = $"{currentAddress12}{currentAddress3Value}";
		var itemAddress12 = $"{item.Address1}{item.Address2}";
		var address3Modify = currentFullAddress.StartsWith(itemAddress12, StringComparison.Ordinal)
			? currentFullAddress[itemAddress12.Length..]
			: currentAddress3Value;

		if (!string.IsNullOrEmpty(item.Address3) && address3Modify.Contains(item.Address3, StringComparison.Ordinal)) {
			return currentAddress3Value;
		}

		if (string.IsNullOrWhiteSpace(address3Modify)) {
			return item.Address3;
		}

		if (string.IsNullOrWhiteSpace(item.Address3)) {
			return address3Modify;
		}

		return $"{item.Address3} {address3Modify}";
	}
}
