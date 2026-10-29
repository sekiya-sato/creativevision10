using CvWpfpos.Services;

namespace CvWpfpos;

/// <summary>
/// グローバル変数。CvWpfclient/AppGlobal.cs と同じ考え方で、
/// アプリ全体で 1 つだけ持つ設定・gRPC クライアント・周辺機器サービスを保持する。
/// ViewModel を XAML の DataContext で直接生成できるよう、生成は遅延初期化にしている
/// （チャネル生成だけでは通信しないため、デザイナ上で開いても副作用はない）。
/// </summary>
public static class AppGlobal
{
    private static readonly Lazy<PosSettings> lazySettings = new(PosSettings.Load);
    private static readonly Lazy<PosTokenStore> lazyTokenStore = new(() => new PosTokenStore());
    private static PosGrpcClient? client;
    private static PosPeripheralService? peripherals;

    /// <summary>appsettings.json の内容。AccessToken はログイン後に書き換わる。</summary>
    public static PosSettings Settings => lazySettings.Value;

    /// <summary>JWT の永続化先（%LOCALAPPDATA%\CVPOS\clientsettings.json）</summary>
    public static PosTokenStore TokenStore => lazyTokenStore.Value;

    public static PosGrpcClient Client => client ??= new PosGrpcClient(Settings);

    public static PosPeripheralService Peripherals => peripherals ??= new PosPeripheralService(Settings);

    /// <summary>ログイン済みのログインID（未ログインは空文字）</summary>
    public static string LoginId { get; set; } = string.Empty;

    /// <summary>認証済みか（JWT を保持しているか）</summary>
    public static bool IsAuthenticated => !string.IsNullOrWhiteSpace(Settings.AccessToken);

    /// <summary>アプリ終了時に一度だけ実行する</summary>
    public static void Shutdown()
    {
        peripherals?.Dispose();
        client?.Dispose();
        peripherals = null;
        client = null;
    }
}
