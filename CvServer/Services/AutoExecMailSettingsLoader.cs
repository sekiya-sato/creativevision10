using CvBase;
using MimeKit;
using System.Globalization;

namespace CvServer.Services;

/// <summary>
/// 自動実行結果メールのSMTP認証方式。
/// <para>
/// OAuth2 は扱わない。アクセストークンの取得・更新の仕組みが必要になり、
/// トークンの保存先である <see cref="MasterConfig"/>.Val の長さにも収まらないため。
/// </para>
/// </summary>
public enum AutoExecMailAuthMode {
	/// <summary>認証しない。社内リレーなど認証不要なSMTPサーバー向け。</summary>
	None,
	/// <summary>ユーザーIDとパスワードで認証する。</summary>
	Password,
}

/// <summary>
/// 自動実行結果メールのSMTP暗号化方式。
/// </summary>
public enum AutoExecMailSecurity {
	/// <summary>暗号化しない。localhost:25 などの社内リレー向け。</summary>
	None,
	/// <summary>ポート番号とサーバーの応答から自動選択する。</summary>
	Auto,
	/// <summary>STARTTLSを必須とする。</summary>
	StartTls,
	/// <summary>サーバーが対応していればSTARTTLSを使う。</summary>
	StartTlsWhenAvailable,
	/// <summary>接続時からSSL/TLSで通信する（SMTPSポート465など）。</summary>
	SslOnConnect,
}

/// <summary>
/// 自動実行結果メールの設定値。
/// </summary>
public sealed class AutoExecMailSettings {
	public string Server { get; }
	public int Port { get; }
	public AutoExecMailSecurity Security { get; }
	public string UserId { get; }
	public string Credential { get; }
	public AutoExecMailAuthMode AuthMode { get; }
	public string FromAddress { get; }
	public string FromName { get; }
	public IReadOnlyList<string> ToAddresses { get; }

	public AutoExecMailSettings(
		string server,
		int port,
		AutoExecMailSecurity security,
		string userId,
		string credential,
		AutoExecMailAuthMode authMode,
		string fromAddress,
		string fromName,
		IReadOnlyList<string> toAddresses) {
		Server = server;
		Port = port;
		Security = security;
		UserId = userId;
		Credential = credential;
		AuthMode = authMode;
		FromAddress = fromAddress;
		FromName = fromName;
		ToAddresses = toAddresses;
	}

	public override string ToString() =>
		$"{nameof(AutoExecMailSettings)} {{ Server = {Server}, Port = {Port}, Security = {Security}, " +
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
	private readonly ExDatabase _db;

	/// <summary>暗号化方式に指定できる値。設定画面の選択肢とこの一覧を一致させる。</summary>
	public static readonly IReadOnlyList<string> SecurityValues = [
		nameof(AutoExecMailSecurity.None),
		nameof(AutoExecMailSecurity.Auto),
		nameof(AutoExecMailSecurity.StartTls),
		nameof(AutoExecMailSecurity.StartTlsWhenAvailable),
		nameof(AutoExecMailSecurity.SslOnConnect),
	];

	/// <summary>認証方式に指定できる値。設定画面の選択肢とこの一覧を一致させる。</summary>
	public static readonly IReadOnlyList<string> AuthModeValues = [
		nameof(AutoExecMailAuthMode.None),
		nameof(AutoExecMailAuthMode.Password),
	];

	/// <summary>認証方式にかかわらず必須の設定名。</summary>
	private static readonly string[] AlwaysRequiredNames = [
		MasterConfig.NameAutoExecMailServerIp,
		MasterConfig.NameAutoExecMailServerPort,
		MasterConfig.NameAutoExecMailSecurity,
		MasterConfig.NameAutoExecMailAuthMode,
		MasterConfig.NameAutoExecMailFromAddr,
		MasterConfig.NameAutoExecMailToAddr,
	];

	/// <summary>認証する場合にだけ必須の設定名。<see cref="AutoExecMailAuthMode.None"/> では空でよい。</summary>
	private static readonly string[] CredentialRequiredNames = [
		MasterConfig.NameAutoExecMailUserId,
		MasterConfig.NameAutoExecMailUserPass,
	];

	public AutoExecMailSettingsLoader(ExDatabase db) {
		_db = db;
	}

	public AutoExecMailSettingsLoadResult Load() {
		var rows = _db.FetchDialect<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Category = @0", MasterConfig.CategoryAutoExec);
		var values = rows.ToDictionary(row => row.Name, row => row.Val, StringComparer.Ordinal);

		// 行が無い場合と空値の場合を同じ「未設定」として扱う。FromName は任意なのでここには含めない。
		string Value(string name) => values.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;

		foreach (var name in AlwaysRequiredNames) {
			if (string.IsNullOrWhiteSpace(Value(name)))
				return Invalid(AutoExecMailSettingsFailure.MissingValue, name);
		}

		if (!int.TryParse(Value(MasterConfig.NameAutoExecMailServerPort).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
			|| port is < 1 or > 65535)
			return Invalid(AutoExecMailSettingsFailure.InvalidPort, MasterConfig.NameAutoExecMailServerPort);

		var security = ParseSecurity(Value(MasterConfig.NameAutoExecMailSecurity).Trim());
		if (security == null)
			return Invalid(AutoExecMailSettingsFailure.UnsupportedSecurity, MasterConfig.NameAutoExecMailSecurity);

		var authMode = ParseAuthMode(Value(MasterConfig.NameAutoExecMailAuthMode).Trim());
		if (authMode == null)
			return Invalid(AutoExecMailSettingsFailure.UnsupportedAuthMode, MasterConfig.NameAutoExecMailAuthMode);

		if (authMode != AutoExecMailAuthMode.None) {
			foreach (var name in CredentialRequiredNames) {
				if (string.IsNullOrWhiteSpace(Value(name)))
					return Invalid(AutoExecMailSettingsFailure.MissingValue, name);
			}
		}

		var fromAddress = Value(MasterConfig.NameAutoExecMailFromAddr).Trim();
		if (!MailboxAddress.TryParse(fromAddress, out var parsedFromAddress))
			return Invalid(AutoExecMailSettingsFailure.InvalidFromAddress, MasterConfig.NameAutoExecMailFromAddr);

		var toAddresses = Value(MasterConfig.NameAutoExecMailToAddr)
			.Split([',', ';'], StringSplitOptions.TrimEntries);
		if (toAddresses.Length == 0 || toAddresses.Any(address =>
			string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(address, out _)))
			return Invalid(AutoExecMailSettingsFailure.InvalidToAddress, MasterConfig.NameAutoExecMailToAddr);

		return new AutoExecMailSettingsLoadResult(
			new AutoExecMailSettings(
				Value(MasterConfig.NameAutoExecMailServerIp).Trim(),
				port,
				security.Value,
				Value(MasterConfig.NameAutoExecMailUserId).Trim(),
				Value(MasterConfig.NameAutoExecMailUserPass),
				authMode.Value,
				parsedFromAddress.Address,
				Value(MasterConfig.NameAutoExecMailFromName).Trim(),
				toAddresses),
			AutoExecMailSettingsFailure.None,
			null);
	}

	/// <summary>暗号化方式の設定値を列挙へ変換する。未対応の値は <c>null</c> を返す。</summary>
	public static AutoExecMailSecurity? ParseSecurity(string? value) => value switch {
		nameof(AutoExecMailSecurity.None) => AutoExecMailSecurity.None,
		nameof(AutoExecMailSecurity.Auto) => AutoExecMailSecurity.Auto,
		nameof(AutoExecMailSecurity.StartTls) => AutoExecMailSecurity.StartTls,
		nameof(AutoExecMailSecurity.StartTlsWhenAvailable) => AutoExecMailSecurity.StartTlsWhenAvailable,
		nameof(AutoExecMailSecurity.SslOnConnect) => AutoExecMailSecurity.SslOnConnect,
		_ => null,
	};

	/// <summary>認証方式の設定値を列挙へ変換する。未対応の値は <c>null</c> を返す。</summary>
	public static AutoExecMailAuthMode? ParseAuthMode(string? value) => value switch {
		nameof(AutoExecMailAuthMode.None) => AutoExecMailAuthMode.None,
		nameof(AutoExecMailAuthMode.Password) => AutoExecMailAuthMode.Password,
		_ => null,
	};

	private static AutoExecMailSettingsLoadResult Invalid(
		AutoExecMailSettingsFailure failure,
		string settingName) => new(null, failure, settingName);
}
