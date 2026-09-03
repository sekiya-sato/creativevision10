using CvBase;
using MimeKit;
using System.Globalization;

namespace CvServer.Services;

/// <summary>
/// 自動実行結果メール設定の生の文字列値。<see cref="MasterConfig"/> に入る値をそのまま持つ。
/// パスワードは含めず、登録済みかどうかだけを <see cref="HasCredential"/> で表す。
/// </summary>
public sealed record AutoExecMailConfigValues(
	string Server,
	string Port,
	string Security,
	string AuthMode,
	string UserId,
	string FromAddress,
	string FromName,
	string ToAddress,
	bool HasCredential) {
	public static AutoExecMailConfigValues Empty { get; } = new(
		string.Empty, string.Empty, string.Empty, string.Empty,
		string.Empty, string.Empty, string.Empty, string.Empty, false);
}

/// <summary>
/// 設定画面から受け取った値の形式チェック結果。空欄は「未入力」として許し、
/// 「入力されているが形式が誤っている」ものだけを弾く。
/// </summary>
public sealed record AutoExecMailConfigValidation(bool IsValid, string? ErrorSettingName, string? ErrorMessage) {
	public static AutoExecMailConfigValidation Ok { get; } = new(true, null, null);
}

/// <summary>
/// <see cref="MasterConfig"/> の自動実行結果メール設定を読み書きする。
/// 読み取り専用の <see cref="AutoExecMailSettingsLoader"/> と違い、設定画面からの保存を担う。
/// </summary>
public sealed class AutoExecMailConfigStore {
	private readonly ExDatabase _db;

	public AutoExecMailConfigStore(ExDatabase db) {
		_db = db;
	}

	/// <summary>保存済みの設定値を読み出す。パスワードは値を返さず、登録済みかどうかだけを返す。</summary>
	public AutoExecMailConfigValues Read() {
		var values = _db
			.FetchDialect<MasterConfig>(
				$"SELECT * FROM {nameof(MasterConfig)} WHERE Category = @0", MasterConfig.CategoryAutoExec)
			.ToDictionary(row => row.Name, row => row.Val ?? string.Empty, StringComparer.Ordinal);

		string Value(string name) => values.TryGetValue(name, out var value) ? value : string.Empty;

		return new AutoExecMailConfigValues(
			Value(MasterConfig.NameAutoExecMailServerIp),
			Value(MasterConfig.NameAutoExecMailServerPort),
			Value(MasterConfig.NameAutoExecMailSecurity),
			Value(MasterConfig.NameAutoExecMailAuthMode),
			Value(MasterConfig.NameAutoExecMailUserId),
			Value(MasterConfig.NameAutoExecMailFromAddr),
			Value(MasterConfig.NameAutoExecMailFromName),
			Value(MasterConfig.NameAutoExecMailToAddr),
			!string.IsNullOrEmpty(Value(MasterConfig.NameAutoExecMailUserPass)));
	}

	/// <summary>
	/// 設定値を保存する。
	/// </summary>
	/// <param name="values">保存する設定値。前後の空白は取り除いて保存する</param>
	/// <param name="credential">
	/// パスワード。<c>null</c> なら保存済みの値を変更しない。
	/// 空文字なら保存済みの値を消去する。
	/// </param>
	public void Write(AutoExecMailConfigValues values, string? credential) {
		ArgumentNullException.ThrowIfNull(values);

		Upsert(MasterConfig.NameAutoExecMailServerIp, values.Server.Trim());
		Upsert(MasterConfig.NameAutoExecMailServerPort, values.Port.Trim());
		Upsert(MasterConfig.NameAutoExecMailSecurity, values.Security.Trim());
		Upsert(MasterConfig.NameAutoExecMailAuthMode, values.AuthMode.Trim());
		Upsert(MasterConfig.NameAutoExecMailUserId, values.UserId.Trim());
		Upsert(MasterConfig.NameAutoExecMailFromAddr, values.FromAddress.Trim());
		Upsert(MasterConfig.NameAutoExecMailFromName, values.FromName.Trim());
		Upsert(MasterConfig.NameAutoExecMailToAddr, values.ToAddress.Trim());

		// パスワードは前後の空白も意味を持ちうるので Trim しない。
		if (credential != null)
			Upsert(MasterConfig.NameAutoExecMailUserPass, credential);
	}

	/// <summary>
	/// 設定画面から受け取った値の形式を確認する。空欄は未入力として許すため、
	/// ここを通っても送信できるとは限らない（送信可否は <see cref="AutoExecMailSettingsLoader.Load"/> が判定する）。
	/// </summary>
	public static AutoExecMailConfigValidation Validate(AutoExecMailConfigValues values) {
		ArgumentNullException.ThrowIfNull(values);

		var port = values.Port.Trim();
		if (port.Length > 0
			&& (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber)
				|| portNumber is < 1 or > 65535))
			return Error(MasterConfig.NameAutoExecMailServerPort, "ポート番号は1～65535の数字で入力してください。");

		var security = values.Security.Trim();
		if (security.Length > 0 && AutoExecMailSettingsLoader.ParseSecurity(security) == null)
			return Error(
				MasterConfig.NameAutoExecMailSecurity,
				$"暗号化方式は次のいずれかを指定してください: {string.Join(", ", AutoExecMailSettingsLoader.SecurityValues)}");

		var authMode = values.AuthMode.Trim();
		if (authMode.Length > 0 && AutoExecMailSettingsLoader.ParseAuthMode(authMode) == null)
			return Error(
				MasterConfig.NameAutoExecMailAuthMode,
				$"認証方式は次のいずれかを指定してください: {string.Join(", ", AutoExecMailSettingsLoader.AuthModeValues)}");

		var fromAddress = values.FromAddress.Trim();
		if (fromAddress.Length > 0 && !MailboxAddress.TryParse(fromAddress, out _))
			return Error(MasterConfig.NameAutoExecMailFromAddr, "送信元アドレスの形式が正しくありません。");

		var toAddress = values.ToAddress.Trim();
		if (toAddress.Length > 0) {
			var addresses = toAddress.Split([',', ';'], StringSplitOptions.TrimEntries);
			if (addresses.Any(address => string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(address, out _)))
				return Error(
					MasterConfig.NameAutoExecMailToAddr,
					"送信先アドレスの形式が正しくありません。複数指定はカンマまたはセミコロンで区切ってください。");
		}

		return AutoExecMailConfigValidation.Ok;
	}

	private static AutoExecMailConfigValidation Error(string settingName, string message) =>
		new(false, settingName, message);

	/// <summary>
	/// <see cref="MasterConfig"/> を Name で検索し、あれば Val/Vdu を更新、無ければ新規登録する。
	/// 通常の初期データ登録で9行そろっているため、新規登録は初期データ登録前のDB向けの保険。
	/// </summary>
	private void Upsert(string name, string val) {
		var vdate = DateTime.Now.ToUniversalTime().Ticks;
		var existing = _db.FirstOrDefault<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", name);
		if (existing != null) {
			existing.Val = val;
			existing.Vdu = vdate;
			_db.Update(existing, ["Val", "Vdu"]);
			return;
		}

		_db.Insert(new MasterConfig {
			Category = MasterConfig.CategoryAutoExec,
			Name = name,
			Val = val,
			Memo = "自動実行結果メールの設定",
			Vdc = vdate,
			Vdu = vdate,
		});
	}
}
