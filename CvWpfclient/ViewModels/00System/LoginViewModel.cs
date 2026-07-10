using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Microsoft.Extensions.Logging;

namespace CvWpfclient.ViewModels._00System;

public partial class LoginViewModel : Helpers.BaseViewModel {
	private const int LoginResultEmployeeInvalid = -2;
	private const string LoginEmployeeInvalidMessage = "社員未設定または有効期限切れのためログインできません。";
	private const string RefreshEmployeeInvalidMessage = "社員未設定または有効期限切れのためログインRefreshができませんでした。";
	private readonly ILogger<LoginViewModel> _logger;
	[ObservableProperty]
	public partial string? LoginId { get; set; }

	[ObservableProperty]
	public partial string? LoginPassword { get; set; }

	[ObservableProperty]
	public partial LoginReply? LoginData { get; set; }

	[ObservableProperty]
	public partial bool IsVisibleLoginTab { get; set; } = true; // true:ログインタブ、false:ログインリフレッシュのタブ

	public LoginViewModel() {
		_logger = new NLogExtender<LoginViewModel>();
	}

	[RelayCommand]
	private void Init() {
		var parameters = AppGlobal.Application;
		LoginId = parameters.LoginId;
		LoginPassword = parameters.LoginPass;
		if (InitParam == 1) {
			IsVisibleLoginTab = false;
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Login(CancellationToken cancellationToken) {
		var loginService = AppGlobal.GetGrpcService<ILoginService>();
		var now = DateTime.Now;
		if (string.IsNullOrEmpty(LoginId) || string.IsNullOrEmpty(LoginPassword)) {
			MessageEx.ShowErrorDialog("ログインID、パスワードを入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		cancellationToken.ThrowIfCancellationRequested();
		var loginRequest = new LoginRequest {
			LoginId = LoginId,
			Name = "CvnetWpfClientユーザ " + DateTime.Now.ToDtStrDateTime(),
			CryptPassword = Common.EncryptLoginRequest(LoginPassword, now),
			LoginDate = now,
			Info = Common.SerializeObject(SubGetInfo()),
		};
		try {
			var reply = await loginService.LoginAsync(loginRequest, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Result == 0) {
				if (reply.JwtMessage?.Length > 10) {
					AppGlobal.SetLoginJwt(reply.JwtMessage);
					//await App.RestartHostAsync(cancellationToken);
					_logger.LogDebug("{Now} AppGlobal.LoginJwt={LoginJwt}", DateTime.Now, MaskJwt(AppGlobal.LoginJwt));
					LoginData = reply;
					ExitWithResultTrue();
					return;
				}
			}
			else {
				ShowLoginFailure(reply, "ログインIDかパスワードが間違っています", LoginEmployeeInvalidMessage);
			}
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog(ex.Message, owner: ClientLib.GetActiveView(this));
		}
	}

	SysHistJwtSub SubGetInfo() {
		var ipAddr = Common.GetIPAddress().FirstOrDefault();

		var jsub = new SysHistJwtSub {
			IpAddress = ipAddr.IPAddress.ToString(),
			MacAddress = ipAddr.MacAddress,
			Machine = Environment.MachineName,
			User = Environment.UserName,
			OsVer = Environment.OSVersion.Version.ToString(),
		};
		return jsub;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Refresh(CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(AppGlobal.LoginJwt))
			return;
		var loginService = AppGlobal.GetGrpcService<ILoginService>();
		var loginRefresh = new LoginRefresh() { Token = AppGlobal.LoginJwt, Info = Common.SerializeObject(SubGetInfo()) };
		cancellationToken.ThrowIfCancellationRequested();
		try {
			LoginReply reply = new() { JwtMessage = string.Empty };
			var refreshToken = string.Empty;
			reply = await loginService.LoginRefreshAsync(loginRefresh, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Result == 0) {
				if (reply.JwtMessage?.Length > 10) {
					AppGlobal.SetLoginJwt(reply.JwtMessage);
					// await App.RestartHostAsync(cancellationToken);
					_logger.LogDebug("{Now} AppGlobal.LoginJwt={LoginJwt}", DateTime.Now, MaskJwt(AppGlobal.LoginJwt));
					LoginData = reply;
					ExitWithResultTrue();
					return;
				}
			}
			if (reply.Result != 0 || string.IsNullOrEmpty(reply.JwtMessage)) {
				AppGlobal.ClearLoginJwt();
				ShowLoginFailure(reply, "ログインRefreshができませんでした", RefreshEmployeeInvalidMessage);
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog(ex.Message, owner: ClientLib.GetActiveView(this));
			return;
		}
	}

	private static string MaskJwt(string? token) {
		if (string.IsNullOrWhiteSpace(token)) {
			return string.Empty;
		}

		if (token.Length <= 16) {
			return "***";
		}

		return $"{token[..8]}...{token[^8..]}";
	}

	private void ShowLoginFailure(LoginReply reply, string defaultMessage, string employeeInvalidMessage) {
		var message = reply.Result == LoginResultEmployeeInvalid ? employeeInvalidMessage : defaultMessage;
		MessageEx.ShowErrorDialog(message, owner: ClientLib.GetActiveView(this));
	}
}
