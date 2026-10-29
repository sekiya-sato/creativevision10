using CvWpfpos.Views;
using CvWpfpos.Views._00System;
using System.Windows;

namespace CvWpfpos;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ログインダイアログを閉じた時点ではまだメインウィンドウが無いため、
        // 既定の OnLastWindowClose だとアプリごと終了してしまう。
        // 売上入力画面を開くまでは明示終了に切り替えておく。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!await TryRestoreSessionAsync() && !ShowLogin())
        {
            AppGlobal.Shutdown();
            Shutdown();
            return;
        }

        // ログイン完了。そのまま売上入力へ遷移する
        ShowMenu();
    }

    /// <summary>保存済み JWT のリフレッシュを試みる。成功すればログイン画面を出さない。</summary>
    private static async Task<bool> TryRestoreSessionAsync()
    {
        var savedToken = AppGlobal.TokenStore.Load();
		var configuredToken = AppGlobal.Settings.AccessToken;
		AppGlobal.LoginId = savedToken.LoginId;

        foreach (var accessToken in new[] { configuredToken, savedToken.Token }.Where(token => !string.IsNullOrWhiteSpace(token)).Distinct())
        {
            AppGlobal.Settings.AccessToken = accessToken;
            try
            {
                var reply = await AppGlobal.Client.RefreshLoginAsync(CancellationToken.None);
                if (reply.Result == 0 && reply.JwtMessage.Length > 10)
                {
                    AppGlobal.Settings.AccessToken = reply.JwtMessage;
                    AppGlobal.TokenStore.Save(savedToken.LoginId, reply.JwtMessage, reply.Expire);
                    return true;
                }
            }
            catch
            {
                // Ignore: サーバー未起動・通信エラー時は次のトークンまたはログイン画面へフォールバックする
            }
        }

        // ログインIDはログイン画面の初期値として残す
        AppGlobal.Settings.AccessToken = string.Empty;
        AppGlobal.TokenStore.Clear();
        return false;
    }

    /// <summary>DataContext は LoginView.xaml で宣言済み（AppGlobal 経由で共有インスタンスを取得する）。</summary>
    private static bool ShowLogin() => new LoginView().ShowDialog() == true;

    private void ShowMenu()
    {
        var window = new MenuView();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppGlobal.Shutdown();
        base.OnExit(e);
    }
}
