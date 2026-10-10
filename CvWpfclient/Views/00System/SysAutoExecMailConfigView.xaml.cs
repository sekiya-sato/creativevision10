namespace CvWpfclient.Views._00System;

public partial class SysAutoExecMailConfigView : Helpers.BaseWindow {
	public SysAutoExecMailConfigView() {
		InitializeComponent();
	}

	protected override void OnClosed(EventArgs e) {
		base.OnClosed(e);
		// ×ボタン等で閉じた場合も専用チャネルを解放する
		if (DataContext is ViewModels._00System.SysAutoExecMailConfigViewModel vm) {
			vm.DisposeSchedulerChannel();
		}
	}
}
