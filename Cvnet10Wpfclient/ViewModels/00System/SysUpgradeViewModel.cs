using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Wpfclient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
		var configuration = App.AppHost?.Services.GetRequiredService<IConfiguration>()
			?? throw new InvalidOperationException("IConfiguration を取得できません。");
		_updateService = new UpdateService(_logger, configuration);
	}

	[RelayCommand]
	private async Task CheckUpdateAsync() {
		UpdateStatus = "更新を確認中...";
		IsUpdateAvailable = await _updateService.CheckForUpdateAsync();

		UpdateStatus = IsUpdateAvailable
			? $"新しいバージョンが利用可能です。 現在={_updateService.GetCurrentVersion()} 設定={_updateService.GetConfiguredVersion()}"
			: "現在のバージョンは最新です。";
	}

	[RelayCommand(CanExecute = nameof(IsUpdateAvailable))]
	private async Task ExecuteUpdateAsync() {
		UpdateStatus = "更新プログラムをダウンロード中...";
		bool success = await _updateService.PerformUpdateAsync();

		if (success) {
			UpdateStatus = "更新を適用して再起動します。";
			// 必要に応じてメッセージボックスを表示し、再起動を促す
		}
	}
}

