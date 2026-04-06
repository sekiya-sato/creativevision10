using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velopack;
namespace CvWpfclient.Services;

public sealed record UpdateCheckResult(
	bool IsUpdateAvailable,
	string Message,
	string CurrentVersion,
	string NewVersion,
	string FeedUrl);

public sealed record UpdateExecutionResult(
	bool IsSuccess,
	string Message);

public interface IUpdateService {
	Task<UpdateCheckResult> CheckForUpdateAsync();
	Task<UpdateExecutionResult> PerformUpdateAsync();
	string GetCurrentVersion();
	string GetFileVersion();
	string GetFeedUrl();
}

public class UpdateService : IUpdateService {
	private readonly ILogger<UpdateService> _logger;
	private readonly IConfiguration _configuration;
	private UpdateInfo? _pendingUpdate;

	public UpdateService(ILogger<UpdateService> logger, IConfiguration configuration) {
		_logger = logger;
		_configuration = configuration;
	}

	public async Task<UpdateCheckResult> CheckForUpdateAsync() {
		var feedUrl = GetFeedUrl();
		var currentVersion = GetCurrentVersion();
		if (string.IsNullOrWhiteSpace(feedUrl)) {
			_logger.LogInformation("Update:FeedUrl が未設定のため、更新チェックをスキップします。");
			_pendingUpdate = null;
			return new UpdateCheckResult(false,
				"更新先 URL が未設定のため、更新確認を実行できません。",
				currentVersion,
				string.Empty,
				feedUrl);
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			_pendingUpdate = await updateManager.CheckForUpdatesAsync();
			if (_pendingUpdate == null) {
				return new UpdateCheckResult(false,
					$"現在のバージョンは最新です。 現在={currentVersion}",
					currentVersion,
					string.Empty,
					feedUrl);
			}
			var newVersion = _pendingUpdate.TargetFullRelease.Version?.ToString() ?? string.Empty;
			return new UpdateCheckResult(true,
				$"新しいバージョンが利用可能です。 現在={currentVersion} 最新={newVersion}",
				currentVersion,
				newVersion,
				feedUrl);
		}
		catch (Exception ex) {
		_logger.LogError(ex, "更新チェック中にエラーが発生しました。");
		_pendingUpdate = null;
			return new UpdateCheckResult(false,
				$"更新チェックに失敗しました: {ex.Message}",
				currentVersion,
				string.Empty,
				feedUrl);
		}
	}

	public async Task<UpdateExecutionResult> PerformUpdateAsync() {
		var feedUrl = GetFeedUrl();
		if (_pendingUpdate == null) {
			_logger.LogInformation("適用可能な更新が見つかっていないため、更新処理をスキップします。");
			return new UpdateExecutionResult(false, "適用可能な更新が見つかっていません。先に更新確認を実行してください。");
		}

		try {
			var updateManager = new UpdateManager(feedUrl);
			await updateManager.DownloadUpdatesAsync(_pendingUpdate);
			updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
			_logger.LogInformation("アップデートを適用し、再起動を開始します。");
			return new UpdateExecutionResult(true, "更新を適用して再起動します。");
		}
		catch (Exception ex) {
			_logger.LogError(ex, "アップデートの実行中にエラーが発生しました。");
			return new UpdateExecutionResult(false, $"更新の適用に失敗しました: {ex.Message}");
		}
	}

	public string GetFileVersion() { // x.x.x.x
		return System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty;
	}

	public string GetCurrentVersion() { // x.x.x
		return _configuration["Application:Version"] ?? string.Empty;
	}

	public string GetFeedUrl() {
		return _configuration["Update:FeedUrl"] ?? string.Empty;
	}
}
