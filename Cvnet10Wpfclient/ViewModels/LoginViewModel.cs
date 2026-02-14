using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using System.Diagnostics;

namespace Cvnet10Wpfclient.ViewModels;
public partial class LoginViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	private string? loginId;

	[ObservableProperty]
	private string? loginPassword;

	[ObservableProperty]
	LoginReply? loginData;

	[RelayCommand]
	private void Init() {
		var parameters = AppGlobal.Config.GetSection("Parameters");
		LoginId = parameters?.GetSection("LoginId")?.Value;
		LoginPassword = parameters?.GetSection("LoginPass")?.Value;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Login(CancellationToken cancellationToken) {
		var loginService = AppGlobal.GetgRPCService<ILoginService>();
		var now = DateTime.Now;
		if(string.IsNullOrEmpty(LoginId) || string.IsNullOrEmpty(LoginPassword)) {
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
				AppGlobal.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppGlobal.LoginJwt}");
				if (reply.JwtMessage?.Length > 10) {
					AppGlobal.LoginJwt = reply.JwtMessage;
					LoginData = reply;
					ExitWithResultTrue();
				}
			}
			else {
				MessageEx.ShowErrorDialog("ログインIDかパスワードが間違っています", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (OperationCanceledException) {
			return;
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


}

