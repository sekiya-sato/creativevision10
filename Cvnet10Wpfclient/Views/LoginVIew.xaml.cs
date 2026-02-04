using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Wpfclient.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Cvnet10Wpfclient.Views
{
    /// <summary>
    /// ViewOrg.xaml の相互作用ロジック
    /// </summary>
    public partial class LoginView : Window
    {
        public LoginView()
        {
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
