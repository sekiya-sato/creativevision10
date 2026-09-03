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
/// MailKitを使用してStartTLSでSMTP送信する。
/// </summary>
public sealed class MailKitAutoExecMailTransport : IAutoExecMailTransport {
	public async Task SendAsync(
		AutoExecMailSettings settings,
		MimeMessage message,
		CancellationToken cancellationToken = default) {
		using var client = new SmtpClient();
		await client.ConnectAsync(
			settings.Server,
			settings.Port,
			SecureSocketOptions.StartTls,
			cancellationToken);

		if (settings.AuthMode == AutoExecMailAuthMode.OAuth2) {
			await client.AuthenticateAsync(
				new SaslMechanismOAuth2(settings.UserId, settings.Credential),
				cancellationToken);
		}
		else {
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
		mail.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
		foreach (var toAddress in settings.ToAddresses)
			mail.To.Add(MailboxAddress.Parse(toAddress));
		return mail;
	}
}
