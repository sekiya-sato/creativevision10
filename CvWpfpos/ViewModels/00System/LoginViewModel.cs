using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfpos.Services;

namespace CvWpfpos.ViewModels._00System;

public partial class LoginViewModel : ObservableObject
{
    private readonly PosSettings settings;
    private readonly PosGrpcClient client;
    private readonly PosTokenStore tokenStore;

    [ObservableProperty] public partial string LoginId { get; set; } = string.Empty;
    [ObservableProperty] public partial string LoginPassword { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = "ログインIDとパスワードを入力してください。";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsAuthenticated { get; set; }
    [ObservableProperty] public partial bool IsCancelled { get; set; }

    /// <summary>XAML の DataContext 宣言用。AppGlobal から共有インスタンスを取得する。</summary>
    public LoginViewModel() : this(AppGlobal.Settings, AppGlobal.Client, AppGlobal.TokenStore, AppGlobal.LoginId) { }

    public LoginViewModel(PosSettings settings, PosGrpcClient client, PosTokenStore tokenStore, string loginId)
    {
        this.settings = settings;
        this.client = client;
        this.tokenStore = tokenStore;
        LoginId = loginId;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Login(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrWhiteSpace(LoginPassword))
        {
            StatusMessage = "ログインIDとパスワードを入力してください。";
            return;
        }

        IsBusy = true;
        StatusMessage = "認証中です…";
        try
        {
            var reply = await client.LoginAsync(LoginId, LoginPassword, cancellationToken);
            if (reply.Result != 0 || reply.JwtMessage.Length <= 10)
            {
                StatusMessage = reply.Result == -2 ? "社員未設定または有効期限切れのためログインできません。" : "ログインIDかパスワードが間違っています。";
                return;
            }

            settings.AccessToken = reply.JwtMessage;
            tokenStore.Save(LoginId, reply.JwtMessage, reply.Expire);
            AppGlobal.LoginId = LoginId;
            StatusMessage = "認証しました。売上入力を開きます…";

            // 認証成功。View 側がこのフラグを監視して売上入力画面へ遷移する
            IsAuthenticated = true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "ログインを中止しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ログインエラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ESC / キャンセルボタン。BaseWindow が ExitCommand を探して実行する。</summary>
    [RelayCommand]
    private void Exit()
    {
        if (LoginCommand.IsRunning) LoginCancelCommand.Execute(null);
        IsCancelled = true;
    }
}
