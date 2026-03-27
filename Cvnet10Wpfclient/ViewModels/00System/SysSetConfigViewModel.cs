using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.Helpers;
using Cvnet10Wpfclient.Services;
using Microsoft.Extensions.Configuration;

namespace Cvnet10Wpfclient.ViewModels._00System;

public partial class SysSetConfigViewModel : Helpers.BaseViewModel {
	private SystemSettingsStore _store = new();
	private SystemSettingsDocument _currentSettings = new();
	private string _originalUrl = string.Empty;


	[ObservableProperty]
	private string url = string.Empty;

	[ObservableProperty]
	private string loginId = string.Empty;

	[ObservableProperty]
	private string loginPassword = string.Empty;

	[RelayCommand]
	private void Init() {
		LoadSettings();
	}
	/*
	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ExitWithResultFalse();
		}
	}*/


	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SaveAsync(CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(Url)) {
			MessageEx.ShowErrorDialog("接続先 URL を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();
		_currentSettings.ConnectionStrings.Url = Url;
		_currentSettings.Parameters.LoginId = LoginId;
		_currentSettings.Parameters.LoginPass = LoginPassword;

		try {
			_store.Save(_currentSettings);
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"設定の保存に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
			return;
		}

		var urlChanged = !string.Equals(_originalUrl, Url, StringComparison.OrdinalIgnoreCase);
		if (urlChanged) {
			try {
				await App.RestartHostAsync(cancellationToken);
			}
			catch (Exception ex) {
				MessageEx.ShowErrorDialog($"接続先の再構築に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
				return;
			}
		}

		_originalUrl = Url;
		ExitWithResultTrue();
	}

	private void LoadSettings() {
		_currentSettings = _store.Load();
		Url = _currentSettings.ConnectionStrings.Url ?? AppGlobal.Config.GetConnectionString("Url") ?? string.Empty;
		LoginId = _currentSettings.Parameters.LoginId ?? AppGlobal.Config.GetSection("Parameters")?["LoginId"] ?? string.Empty;
		LoginPassword = _currentSettings.Parameters.LoginPass ?? AppGlobal.Config.GetSection("Parameters")?["LoginPass"] ?? string.Empty;
		_originalUrl = Url;
	}

	[ObservableProperty]
	string testMessage = string.Empty;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task Test01(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
		var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg001_CopyReply };
		msg.DataType = typeof(string);
		msg.DataMsg = $"{DateTime.Now.ToLongDateString()} {DateTime.Now.ToLongTimeString()}";
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		TestMessage = reply.DataMsg;
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task Test02(CancellationToken ct) {
		throw new NotImplementedException("テスト未実装例外");
	}


}
