namespace CvWpfclient.Views._00System;

public partial class SysSchedulerJobMenteView : Helpers.BaseWindow {
	public SysSchedulerJobMenteView() {
		InitializeComponent();
	}

	protected override void OnClosed(EventArgs e) {
		base.OnClosed(e);
		// ×ボタン等で閉じた場合も専用チャネルを解放する
		if (DataContext is ViewModels._00System.SysSchedulerJobMenteViewModel vm) {
			vm.DisposeSchedulerChannel();
		}
	}
}
