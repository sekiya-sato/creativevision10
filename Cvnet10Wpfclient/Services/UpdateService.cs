using Cvnet10Wpfclient.Helpers;
using Microsoft.Extensions.Logging;

namespace Cvnet10Wpfclient.Services;

public interface IUpdateService {
	Task<bool> CheckForUpdateAsync();
	Task<bool> PerformUpdateAsync();
}

public class UpdateService : IUpdateService {
	private readonly ILogger<UpdateService> _logger;

	public UpdateService(ILogger<UpdateService> logger) {
		_logger = logger;
	}

	public async Task<bool> CheckForUpdateAsync() {
		// ClickOnceで実行されているか確認
		if (!ApplicationDeployment.IsNetworkDeployed) {
			_logger.LogInformation("ClickOnce環境ではないため、更新チェックをスキップします。");
			return false;
		}

		var ad = ApplicationDeployment.CurrentDeployment;
		try {
			var info = await Task.Run(() => ad.CheckForDetailedUpdate());
			return info.UpdateAvailable;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "更新チェック中にエラーが発生しました。");
			return false;
		}
	}

	public async Task<bool> PerformUpdateAsync() {
		var ad = ApplicationDeployment.CurrentDeployment;
		try {
			await Task.Run(() => ad.Update());
			_logger.LogInformation("アップデートが完了しました。再起動が必要です。");
			return true;
		}
		catch (Exception ex) {
			_logger.LogError(ex, "アップデートの実行中にエラーが発生しました。");
			return false;
		}
	}
}
