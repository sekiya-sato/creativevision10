using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Wpfclient.Models;
using System.Windows;

namespace Cvnet10Wpfclient.Views {
	/// <summary>
	/// SettingSystemView.xaml の相互作用ロジック
	/// </summary>
	public partial class SettingSystemView : Window {
		public SettingSystemView() {
			InitializeComponent();
			// メッセージの受信登録
			WeakReferenceMessenger.Default.Register<DialogCloseMessage>(this, (r, m) => {
				// Viewの文脈でDialogResultをセット
				this.DialogResult = m.DialogResult;
				this.Close();
			});
		}
		// メモリリーク防止のため、Unloadedなどで登録解除するのがベストプラクティス
		protected override void OnClosed(EventArgs e) {
			base.OnClosed(e);
			WeakReferenceMessenger.Default.UnregisterAll(this);
		}
	}
}
