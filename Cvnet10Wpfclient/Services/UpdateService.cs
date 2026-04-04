using Cvnet10Wpfclient.Helpers;
using Microsoft.Extensions.Configuration;
using NLog;
using Velopack;
namespace Cvnet10Wpfclient.Services;

public sealed record UpdateCheckResult(
	bool IsUpdateAvailable,
	string Message,
	string CurrentVersion,
	string ConfiguredVersion,
	string FeedUrl);

public sealed record UpdateExecutionResult(
	bool IsSuccess,
	string Message);

public interface IUpdateService {
	Task<UpdateCheckResult> CheckForUpdateAsync();
	Task<UpdateExecutionResult> PerformUpdateAsync();
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

	public async Task<UpdateCheckResult> CheckForUpdateAsync() {
		var feedUrl = GetFeedUrl();
		var currentVersion = GetCurrentVersion();
		var configuredVersion = GetConfiguredVersion();
		if (string.IsNullOrWhiteSpace(feedUrl)) {
			_logger.Info("Update:FeedUrl が未設定のため、更新チェックをスキップします。");
			_pendingUpdate = null;
			return new UpdateCheckResult(false,
				"更新先 URL が未設定のため、更新確認を実行できません。",
				currentVersion,
				configuredVersion,
				feedUrl);
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			_pendingUpdate = await updateManager.CheckForUpdatesAsync();
			if (_pendingUpdate == null) {
				return new UpdateCheckResult(false,
					$"現在のバージョンは最新です。 現在={currentVersion} 設定={configuredVersion}",
					currentVersion,
					configuredVersion,
					feedUrl);
			}

			return new UpdateCheckResult(true,
				$"新しいバージョンが利用可能です。 現在={currentVersion} 設定={configuredVersion}",
				currentVersion,
				configuredVersion,
				feedUrl);
		}
		catch (Exception ex) {
			_logger.Error(ex, "更新チェック中にエラーが発生しました。");
			_pendingUpdate = null;
			return new UpdateCheckResult(false,
				$"更新チェックに失敗しました: {ex.Message}",
				currentVersion,
				configuredVersion,
				feedUrl);
		}
	}

	public async Task<UpdateExecutionResult> PerformUpdateAsync() {
		var feedUrl = GetFeedUrl();
		if (_pendingUpdate == null) {
			_logger.Info("適用可能な更新が見つかっていないため、更新処理をスキップします。");
			return new UpdateExecutionResult(false, "適用可能な更新が見つかっていません。先に更新確認を実行してください。");
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			await updateManager.DownloadUpdatesAsync(_pendingUpdate);
			updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
			_logger.Info("アップデートを適用し、再起動を開始します。");
			return new UpdateExecutionResult(true, "更新を適用して再起動します。");
		}
		catch (Exception ex) {
			_logger.Error(ex, "アップデートの実行中にエラーが発生しました。");
			return new UpdateExecutionResult(false, $"更新の適用に失敗しました: {ex.Message}");
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
