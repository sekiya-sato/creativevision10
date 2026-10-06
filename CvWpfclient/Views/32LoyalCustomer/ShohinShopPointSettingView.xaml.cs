using CvWpfclient.ViewModels._32LoyalCustomer;
using System.ComponentModel;

namespace CvWpfclient.Views._32LoyalCustomer;

public partial class ShohinShopPointSettingView : Helpers.BaseWindow {
	public ShohinShopPointSettingView() {
		InitializeComponent();
	}

	protected override void OnClosing(CancelEventArgs e) {
		// 閉じるボタン・Esc・ウィンドウの×のいずれでも未保存の変更を確認する。
		if (DataContext is PointCampaignTargetViewModelBase vm && !vm.ConfirmClose()) {
			e.Cancel = true;
			return;
		}
		base.OnClosing(e);
	}
}
