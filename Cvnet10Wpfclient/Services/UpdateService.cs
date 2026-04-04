using Cvnet10Wpfclient.Helpers;
using Microsoft.Extensions.Configuration;
using NLog;
using Velopack;
namespace Cvnet10Wpfclient.Services;

public interface IUpdateService {
	Task<bool> CheckForUpdateAsync();
	Task<bool> PerformUpdateAsync();
	string GetCurrentVersion();
	string GetConfiguredVersion();
	string GetFeedUrl();
}

public class UpdateService : IUpdateService {
	private readonly ILogger _logger;
	private readonly IConfiguration _configuration;
	private UpdateInfo? _pendingUpdate;

	public UpdateService(ILogger logger, IConfiguration configuration) {
		_logger = logger;
		_configuration = configuration;
	}

	public async Task<bool> CheckForUpdateAsync() {
		var feedUrl = GetFeedUrl();
		if (string.IsNullOrWhiteSpace(feedUrl)) {
			_logger.Info("Update:FeedUrl が未設定のため、更新チェックをスキップします。");
			_pendingUpdate = null;
			return false;
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			_pendingUpdate = await updateManager.CheckForUpdatesAsync();
			return _pendingUpdate != null;
		}
		catch (Exception ex) {
			_logger.Error(ex, "更新チェック中にエラーが発生しました。");
			_pendingUpdate = null;
			return false;
		}
	}

	public async Task<bool> PerformUpdateAsync() {
		var feedUrl = GetFeedUrl();
		if (_pendingUpdate == null) {
			_logger.Info("適用可能な更新が見つかっていないため、更新処理をスキップします。");
			return false;
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			await updateManager.DownloadUpdatesAsync(_pendingUpdate);
			updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
			_logger.Info("アップデートを適用し、再起動を開始します。");
			return true;
		}
		catch (Exception ex) {
			_logger.Error(ex, "アップデートの実行中にエラーが発生しました。");
			return false;
		}
	}

	public string GetCurrentVersion() {
		return System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty;
	}

	public string GetConfiguredVersion() {
		return _configuration["Application:Version"] ?? string.Empty;
	}

	public string GetFeedUrl() {
		return _configuration["Update:FeedUrl"] ?? string.Empty;
	}
}
