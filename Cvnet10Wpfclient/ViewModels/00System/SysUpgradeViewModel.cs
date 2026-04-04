using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.Services;
using NLog;

namespace Cvnet10Wpfclient.ViewModels._00System;

public partial class SysUpgradeViewModel : Helpers.BaseViewModel {
	private readonly IUpdateService _updateService;
	private readonly ILogger _logger;

	[ObservableProperty]
	private string _updateStatus = "最新バージョンを確認できます";

	[ObservableProperty]
	private bool _isUpdateAvailable;

	public SysUpgradeViewModel() {
		_logger = LogManager.GetCurrentClassLogger();
		_updateService = new UpdateService(_logger);
	}

	[RelayCommand]
	private async Task CheckUpdateAsync() {
		UpdateStatus = "チェック中...";
		IsUpdateAvailable = await _updateService.CheckForUpdateAsync();

		UpdateStatus = IsUpdateAvailable
			? "新しいバージョンが利用可能です。"
			: "現在のバージョンは最新です。";
	}

	[RelayCommand(CanExecute = nameof(IsUpdateAvailable))]
	private async Task ExecuteUpdateAsync() {
		UpdateStatus = "更新プログラムをダウンロード中...";
		bool success = await _updateService.PerformUpdateAsync();

		if (success) {
			UpdateStatus = "更新が完了しました。アプリケーションを再起動してください。";
			// 必要に応じてメッセージボックスを表示し、再起動を促す
		}
	}
}

