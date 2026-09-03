using CvBase;
using MimeKit;
using System.Globalization;

namespace CvServer.Services;

/// <summary>
/// 自動実行結果メールのSMTP認証方式。
/// </summary>
public enum AutoExecMailAuthMode {
	Password,
	OAuth2,
}

/// <summary>
/// 自動実行結果メールの設定値。
/// </summary>
public sealed class AutoExecMailSettings {
	public string Server { get; }
	public int Port { get; }
	public string UserId { get; }
	public string Credential { get; }
	public AutoExecMailAuthMode AuthMode { get; }
	public string FromAddress { get; }
	public string FromName { get; }
	public IReadOnlyList<string> ToAddresses { get; }

	public AutoExecMailSettings(
		string server,
		int port,
		string userId,
		string credential,
		AutoExecMailAuthMode authMode,
		string fromAddress,
		string fromName,
		IReadOnlyList<string> toAddresses) {
		Server = server;
		Port = port;
		UserId = userId;
		Credential = credential;
		AuthMode = authMode;
		FromAddress = fromAddress;
		FromName = fromName;
		ToAddresses = toAddresses;
	}

	public override string ToString() =>
		$"{nameof(AutoExecMailSettings)} {{ Server = {Server}, Port = {Port}, " +
		$"UserId = {UserId}, Credential = ***, AuthMode = {AuthMode}, " +
		$"FromAddress = {FromAddress}, FromName = {FromName}, ToAddressCount = {ToAddresses.Count} }}";
}

/// <summary>
/// メール設定を利用できない理由。
/// </summary>
public enum AutoExecMailSettingsFailure {
	None,
	MissingValue,
	InvalidPort,
	UnsupportedSecurity,
	UnsupportedAuthMode,
	InvalidFromAddress,
	InvalidToAddress,
}

/// <summary>
/// メール設定の読込結果。秘密値は保持せず、失敗した設定名だけを公開する。
/// </summary>
public sealed record AutoExecMailSettingsLoadResult(
	AutoExecMailSettings? Settings,
	AutoExecMailSettingsFailure Failure,
	string? FailureSettingName) {
	public bool IsValid => Settings != null;
}

/// <summary>
/// 自動実行結果メール設定の読込境界。
/// </summary>
public interface IAutoExecMailSettingsLoader {
	AutoExecMailSettingsLoadResult Load();
}

/// <summary>
/// <see cref="MasterConfig"/> から自動実行結果メール設定を読み込む。
/// </summary>
public sealed class AutoExecMailSettingsLoader : IAutoExecMailSettingsLoader {
	private const string RequiredSecurity = "StartTls";
	private readonly ExDatabase _db;

	private static readonly string[] RequiredNames = [
		MasterConfig.NameAutoExecMailServerIp,
		MasterConfig.NameAutoExecMailServerPort,
		MasterConfig.NameAutoExecMailUserId,
		MasterConfig.NameAutoExecMailUserPass,
		MasterConfig.NameAutoExecMailSecurity,
		MasterConfig.NameAutoExecMailAuthMode,
		MasterConfig.NameAutoExecMailFromAddr,
		MasterConfig.NameAutoExecMailFromName,
		MasterConfig.NameAutoExecMailToAddr,
	];

	public AutoExecMailSettingsLoader(ExDatabase db) {
		_db = db;
	}

	public AutoExecMailSettingsLoadResult Load() {
		var rows = _db.FetchDialect<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Category = @0", MasterConfig.CategoryAutoExec);
		var values = rows.ToDictionary(row => row.Name, row => row.Val, StringComparer.Ordinal);

		foreach (var name in RequiredNames) {
			if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
				return Invalid(AutoExecMailSettingsFailure.MissingValue, name);
		}

		if (!int.TryParse(values[MasterConfig.NameAutoExecMailServerPort].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
			|| port is < 1 or > 65535)
			return Invalid(AutoExecMailSettingsFailure.InvalidPort, MasterConfig.NameAutoExecMailServerPort);

		if (!string.Equals(values[MasterConfig.NameAutoExecMailSecurity].Trim(), RequiredSecurity, StringComparison.Ordinal))
			return Invalid(AutoExecMailSettingsFailure.UnsupportedSecurity, MasterConfig.NameAutoExecMailSecurity);

		var authModeValue = values[MasterConfig.NameAutoExecMailAuthMode].Trim();
		var authMode = authModeValue switch {
			"Password" => AutoExecMailAuthMode.Password,
			"OAuth2" => AutoExecMailAuthMode.OAuth2,
			_ => (AutoExecMailAuthMode?)null,
		};
		if (authMode == null)
			return Invalid(AutoExecMailSettingsFailure.UnsupportedAuthMode, MasterConfig.NameAutoExecMailAuthMode);

		var fromAddress = values[MasterConfig.NameAutoExecMailFromAddr].Trim();
		if (!MailboxAddress.TryParse(fromAddress, out var parsedFromAddress))
			return Invalid(AutoExecMailSettingsFailure.InvalidFromAddress, MasterConfig.NameAutoExecMailFromAddr);

		var toAddresses = values[MasterConfig.NameAutoExecMailToAddr]
			.Split([',', ';'], StringSplitOptions.TrimEntries);
		if (toAddresses.Length == 0 || toAddresses.Any(address =>
			string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(address, out _)))
			return Invalid(AutoExecMailSettingsFailure.InvalidToAddress, MasterConfig.NameAutoExecMailToAddr);

		return new AutoExecMailSettingsLoadResult(
			new AutoExecMailSettings(
				values[MasterConfig.NameAutoExecMailServerIp].Trim(),
				port,
				values[MasterConfig.NameAutoExecMailUserId].Trim(),
				values[MasterConfig.NameAutoExecMailUserPass],
				authMode.Value,
				parsedFromAddress.Address,
				values[MasterConfig.NameAutoExecMailFromName].Trim(),
				toAddresses),
			AutoExecMailSettingsFailure.None,
			null);
	}

	private static AutoExecMailSettingsLoadResult Invalid(
		AutoExecMailSettingsFailure failure,
		string settingName) => new(null, failure, settingName);
}
