using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.Services;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace CvWpfclient.ViewModels._00System;

public partial class SysUpgradeViewModel : Helpers.BaseViewModel {
	private readonly IUpdateService _updateService;
	private readonly ILogger _logger;

	[ObservableProperty]
	private string _updateStatus = "最新バージョンを確認できます";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteUpdateCommand))]
	private bool _isUpdateAvailable;

	[ObservableProperty]
	string optionMessage = string.Empty;

	public SysUpgradeViewModel() {
		_logger = LogManager.GetCurrentClassLogger();
		_updateService = App.AppHost?.Services.GetRequiredService<IUpdateService>()
			?? throw new InvalidOperationException("IUpdateService を取得できません。");
		RefreshOptionMessage();
	}

	[RelayCommand]
	private async Task CheckUpdateAsync() {
		try {
			UpdateStatus = "更新を確認中...";
			var result = await _updateService.CheckForUpdateAsync();
			IsUpdateAvailable = result.IsUpdateAvailable;
			UpdateStatus = result.Message;
			var newVersion = result.NewVersion ?? string.Empty;

			RefreshOptionMessage(newVersion);
		}
		catch (Exception ex) {
			_logger.Error(ex, "手動の更新確認でエラーが発生しました。");
			IsUpdateAvailable = false;
			UpdateStatus = $"更新チェックに失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(UpdateStatus, owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand(CanExecute = nameof(IsUpdateAvailable))]
	private async Task ExecuteUpdateAsync() {
		try {
			UpdateStatus = "更新プログラムをダウンロード中...";
			var result = await _updateService.PerformUpdateAsync();
			UpdateStatus = result.Message;

			if (!result.IsSuccess) {
				MessageEx.ShowErrorDialog(result.Message, owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			_logger.Error(ex, "手動の更新適用でエラーが発生しました。");
			UpdateStatus = $"更新の適用に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(UpdateStatus, owner: ClientLib.GetActiveView(this));
		}
	}

	private void RefreshOptionMessage(string newVersion = "") {
		if (!string.IsNullOrEmpty(newVersion)) {
			OptionMessage = $"新しいバージョン {newVersion} が利用可能です！\n" +
				$"FeedUrl={_updateService.GetFeedUrl()}\n" +
				$"CurrentVersion={_updateService.GetCurrentVersion()}";
			return;
		}
		OptionMessage = $"FeedUrl={_updateService.GetFeedUrl()}\n" +
			$"CurrentVersion={_updateService.GetCurrentVersion()}";
	}
}

