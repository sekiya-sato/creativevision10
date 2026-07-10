using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectPostalAddressViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	public partial string Title { get; set; } = "住所選択";

	[ObservableProperty]
	public partial ObservableCollection<PostalAddressItem>? ListData { get; set; }

	[ObservableProperty]
	public partial PostalAddressItem? Current { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	public void SetLocalData(IEnumerable<PostalAddressItem> items, string windowTitle = "住所選択") {
		Title = windowTitle;
		ListData = new ObservableCollection<PostalAddressItem>(items);
		Count = ListData.Count;
		Current = ListData.FirstOrDefault();
	}

	[RelayCommand]
	public void DoSelect() {
		if (Current != null) {
			ClientLib.ExitDialogResult(this, true);
		}
		else {
			MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	public void Exit() {
		ClientLib.ExitDialogResult(this, false);
	}
}
