using CodeShare;
using CvWpfclient.ViewModels.Sub;
using CvWpfclient.Views.Sub;

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

			if (result.Items.Count == 0) {
				MessageEx.ShowWarningDialog("該当住所が見つかりませんでした。郵便番号を確認してください。", owner: owner);
				return;
			}

			PostalAddressItem selected;
			if (result.Items.Count == 1) {
				selected = result.Items[0];
			}
			else {
				var item = ShowPostalAddressSelectDialog(result.Items, viewModel);
				if (item == null) return;
				selected = item;
			}

			applyAddress(selected);
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"郵便番号検索に失敗しました: {ex.Message}", owner: owner);
		}
	}

	private static PostalAddressItem? ShowPostalAddressSelectDialog(IEnumerable<PostalAddressItem> items, object ownerViewModel) {
		var selWin = new SelectPostalAddressView();
		if (selWin.DataContext is not SelectPostalAddressViewModel vm) return null;
		vm.SetLocalData(items, "住所選択");
		if (ClientLib.ShowDialogView(selWin, ownerViewModel) != true) return null;
		return vm.Current;
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
