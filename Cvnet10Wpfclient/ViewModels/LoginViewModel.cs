using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Models;
using Cvnet10Wpfclient.Util;
using Cvnet8client.Views.Sub;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;

namespace Cvnet10Wpfclient.ViewModels;
public partial class LoginViewModel : ObservableObject {
	[ObservableProperty]
	private string? loginId;

	[ObservableProperty]
	private string? loginPassword;

	[ObservableProperty]
	LoginReply? loginData;

	[RelayCommand]
	private void Init() {
	}

	[RelayCommand]
	private void Exit() {
		WeakReferenceMessenger.Default.Send(new DialogCloseMessage(false));
	}
	[RelayCommand]
	private async Task Login() {
		var loginService = AppCurrent.GetgRPCService<ILoginService>();
		var now = DateTime.Now;
		if(string.IsNullOrEmpty(LoginId) || string.IsNullOrEmpty(LoginPassword)) {
			MessageEx.ShowErrorDialog("ログインID、パスワードを入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		var loginRequest = new LoginRequest {
			LoginId = LoginId,
			Name = "CvnetWpfClientユーザ " + DateTime.Now.ToDtStrDateTime(),
			CryptPassword = Common.EncryptLoginRequest(LoginPassword, now),
			LoginDate = now,
			Info = Common.SerializeObject(subGetInfo()),
		};

		var reply = await loginService.LoginAsync(loginRequest);
		if (reply.Result == 0) {
			AppCurrent.LoginJwt = reply.JwtMessage;
			Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppCurrent.LoginJwt}");
			if (reply.JwtMessage?.Length > 10) {
				AppCurrent.LoginJwt = reply.JwtMessage;
				LoginData = reply;
				// Viewに向けて「DialogResultをtrueにして閉じてくれ」というメッセージを送る
				WeakReferenceMessenger.Default.Send(new DialogCloseMessage(true));
			}
		}
		else {
			MessageEx.ShowErrorDialog("ログインIDかパスワードが間違っています", owner: ClientLib.GetActiveView(this));
		}
	}

	SysHistJwtSub subGetInfo() {
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

