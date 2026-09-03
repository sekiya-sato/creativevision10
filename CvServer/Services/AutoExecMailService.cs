using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;

namespace CvServer.Services;

/// <summary>
/// 自動実行結果メールの件名と本文。スケジューラ履歴の型には依存しない。
/// </summary>
public sealed record AutoExecMailMessage(string Subject, string Body);

/// <summary>
/// メールが送信されなかった理由。
/// </summary>
public enum AutoExecMailNotSentReason {
	None,
	InvalidConfiguration,
}

/// <summary>
/// 自動実行結果メールの送信結果。SMTP例外はこの結果へ変換せず呼出元へ伝播する。
/// </summary>
public sealed record AutoExecMailSendResult(
	bool Sent,
	AutoExecMailNotSentReason NotSentReason,
	AutoExecMailSettingsFailure SettingsFailure,
	string? FailureSettingName) {
	public static AutoExecMailSendResult Success { get; } = new(
		true,
		AutoExecMailNotSentReason.None,
		AutoExecMailSettingsFailure.None,
		null);
}

/// <summary>
/// メール送信結果を人が読める短い文にする。ログ・自動実行履歴のMemo・テスト送信の応答で共用する。
/// </summary>
public static class AutoExecMailResultText {
	/// <summary>設定不備の理由を日本語にする。</summary>
	public static string Describe(AutoExecMailSettingsFailure failure) => failure switch {
		AutoExecMailSettingsFailure.None => "設定は正常です",
		AutoExecMailSettingsFailure.MissingValue => "未入力の設定があります",
		AutoExecMailSettingsFailure.InvalidPort => "ポート番号が不正です",
		AutoExecMailSettingsFailure.UnsupportedSecurity => "暗号化方式が未対応の値です",
		AutoExecMailSettingsFailure.UnsupportedAuthMode => "認証方式が未対応の値です",
		AutoExecMailSettingsFailure.InvalidFromAddress => "送信元アドレスの形式が不正です",
		AutoExecMailSettingsFailure.InvalidToAddress => "送信先アドレスの形式が不正です",
		_ => "設定を確認できません",
	};

	/// <summary>設定不備の理由と対象設定名を1文にする。</summary>
	public static string Describe(AutoExecMailSettingsFailure failure, string? settingName) =>
		string.IsNullOrWhiteSpace(settingName)
			? Describe(failure)
			: $"{Describe(failure)}({settingName})";

	/// <summary>送信結果を1文にする。</summary>
	public static string Describe(AutoExecMailSendResult result) {
		ArgumentNullException.ThrowIfNull(result);
		return result.Sent
			? "送信しました"
			: $"送信しませんでした。{Describe(result.SettingsFailure, result.FailureSettingName)}";
	}
}

/// <summary>
/// 自動実行結果メールの送信境界。
/// </summary>
public interface IAutoExecMailService {
	Task<AutoExecMailSendResult> SendAsync(
		AutoExecMailMessage message,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// SMTP通信を分離する境界。
/// </summary>
public interface IAutoExecMailTransport {
	Task SendAsync(
		AutoExecMailSettings settings,
		MimeMessage message,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// MailKitを使用してSMTP送信する。暗号化方式と認証方式は設定に従う。
/// </summary>
public sealed class MailKitAutoExecMailTransport : IAutoExecMailTransport {
	/// <summary>設定の暗号化方式をMailKitの接続オプションへ変換する。</summary>
	public static SecureSocketOptions ToSecureSocketOptions(AutoExecMailSecurity security) => security switch {
		AutoExecMailSecurity.None => SecureSocketOptions.None,
		AutoExecMailSecurity.Auto => SecureSocketOptions.Auto,
		AutoExecMailSecurity.StartTls => SecureSocketOptions.StartTls,
		AutoExecMailSecurity.StartTlsWhenAvailable => SecureSocketOptions.StartTlsWhenAvailable,
		AutoExecMailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
		// 設定読込時に未対応値を弾いているため到達しない。将来の列挙追加をここで気付けるよう例外にする。
		_ => throw new ArgumentOutOfRangeException(nameof(security), security, "未対応の暗号化方式です。"),
	};

	public async Task SendAsync(
		AutoExecMailSettings settings,
		MimeMessage message,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(settings);

		using var client = new SmtpClient();
		await client.ConnectAsync(
			settings.Server,
			settings.Port,
			ToSecureSocketOptions(settings.Security),
			cancellationToken);

		// 認証不要のSMTPサーバーにAUTHを送ると拒否されるため、認証方式Noneでは何もしない。
		if (settings.AuthMode != AutoExecMailAuthMode.None) {
			await client.AuthenticateAsync(
				settings.UserId,
				settings.Credential,
				cancellationToken);
		}

		await client.SendAsync(message, cancellationToken);
		await client.DisconnectAsync(true, cancellationToken);
	}
}

/// <summary>
/// 設定を検証し、自動実行結果メールを組み立てて送信する。
/// </summary>
public sealed class AutoExecMailService : IAutoExecMailService {
	private readonly IAutoExecMailSettingsLoader _settingsLoader;
	private readonly IAutoExecMailTransport _transport;
	private readonly ILogger<AutoExecMailService> _logger;

	public AutoExecMailService(
		IAutoExecMailSettingsLoader settingsLoader,
		IAutoExecMailTransport transport,
		ILogger<AutoExecMailService> logger) {
		_settingsLoader = settingsLoader;
		_transport = transport;
		_logger = logger;
	}

	public async Task<AutoExecMailSendResult> SendAsync(
		AutoExecMailMessage message,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(message);

		var loadResult = _settingsLoader.Load();
		if (loadResult.Settings == null) {
			_logger.LogWarning(
				"自動実行結果メールを送信しません。理由: {Reason}, 設定名: {SettingName}",
				loadResult.Failure,
				loadResult.FailureSettingName);
			return new AutoExecMailSendResult(
				false,
				AutoExecMailNotSentReason.InvalidConfiguration,
				loadResult.Failure,
				loadResult.FailureSettingName);
		}

		var mail = CreateMimeMessage(loadResult.Settings, message);
		await _transport.SendAsync(loadResult.Settings, mail, cancellationToken);
		return AutoExecMailSendResult.Success;
	}

	private static MimeMessage CreateMimeMessage(
		AutoExecMailSettings settings,
		AutoExecMailMessage message) {
		var mail = new MimeMessage {
			Subject = message.Subject,
			Body = new TextPart(TextFormat.Plain) { Text = message.Body },
		};
		// 表示名は任意。空文字を渡すと空の表示名付きで送ってしまうため null にしてアドレスだけにする。
		var fromName = string.IsNullOrWhiteSpace(settings.FromName) ? null : settings.FromName;
		mail.From.Add(new MailboxAddress(fromName, settings.FromAddress));
		foreach (var toAddress in settings.ToAddresses)
			mail.To.Add(MailboxAddress.Parse(toAddress));
		return mail;
	}
}
