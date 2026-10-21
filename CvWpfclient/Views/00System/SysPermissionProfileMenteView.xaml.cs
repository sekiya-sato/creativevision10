using System.ComponentModel;

namespace CvWpfclient.Views._00System;

public partial class SysPermissionProfileMenteView : Helpers.BaseWindow {
	public SysPermissionProfileMenteView() {
		InitializeComponent();
	}

	/// <summary>未保存の設定があれば、閉じる前に破棄してよいか確認する（Esc・×・戻るボタン共通）</summary>
	protected override void OnClosing(CancelEventArgs e) {
		if (DataContext is ViewModels._00System.SysPermissionProfileMenteViewModel vm && !vm.ConfirmDiscard()) {
			e.Cancel = true;
			return;
		}
		base.OnClosing(e);
	}
}
