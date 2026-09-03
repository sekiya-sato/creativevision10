using CvBase;
using CvBaseSqlite;
using CvServer.Services;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.CvServer;

[TestClass]
public class AutoExecMailSettingsLoaderTests {
	private ExDatabaseSqlite? _db;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		_db = new ExDatabaseSqlite(connection);
		Db.CreateTable(typeof(MasterConfig), true, false);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	[TestMethod]
	public void Load_有効なPassword設定_複数宛先を読み込む() {
		InsertValidSettings("Password");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsTrue(result.IsValid);
		Assert.IsNotNull(result.Settings);
		Assert.AreEqual(AutoExecMailAuthMode.Password, result.Settings.AuthMode);
		Assert.AreEqual(2, result.Settings.ToAddresses.Count);
		Assert.AreEqual("first@example.com", result.Settings.ToAddresses[0]);
		Assert.AreEqual("second@example.com", result.Settings.ToAddresses[1]);
	}

	[TestMethod]
	public void Load_パスワードを保持する_ToStringには出さない() {
		InsertValidSettings("Password", "secret-password");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsNotNull(result.Settings);
		Assert.AreEqual("secret-password", result.Settings.Credential);
		Assert.IsFalse(result.Settings.ToString().Contains("secret-password", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Load_OAuth2_扱わない認証方式なので設定無効理由を返す() {
		InsertValidSettings("OAuth2");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.UnsupportedAuthMode, result.Failure);
		Assert.AreEqual(MasterConfig.NameAutoExecMailAuthMode, result.FailureSettingName);
	}

	[TestMethod]
	public void Load_必須設定不足_設定無効理由を返す() {
		InsertValidSettings("Password");
		var row = Db.Single<MasterConfig>("WHERE Name = @0", MasterConfig.NameAutoExecMailUserPass);
		Db.Delete(row);

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(AutoExecMailSettingsFailure.MissingValue, result.Failure);
		Assert.AreEqual(MasterConfig.NameAutoExecMailUserPass, result.FailureSettingName);
	}

	[TestMethod]
	[DataRow(MasterConfig.NameAutoExecMailServerIp)]
	[DataRow(MasterConfig.NameAutoExecMailServerPort)]
	[DataRow(MasterConfig.NameAutoExecMailUserId)]
	[DataRow(MasterConfig.NameAutoExecMailUserPass)]
	[DataRow(MasterConfig.NameAutoExecMailSecurity)]
	[DataRow(MasterConfig.NameAutoExecMailAuthMode)]
	[DataRow(MasterConfig.NameAutoExecMailFromAddr)]
	[DataRow(MasterConfig.NameAutoExecMailToAddr)]
	public void Load_必須設定が空白_設定無効理由を返す(string settingName) {
		InsertValidSettings("Password");
		UpdateValue(settingName, " ");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.MissingValue, result.Failure);
		Assert.AreEqual(settingName, result.FailureSettingName);
	}

	[TestMethod]
	[DataRow("0")]
	[DataRow("65536")]
	[DataRow("not-number")]
	public void Load_不正なPort_設定無効理由を返す(string port) {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailServerPort, port);

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.InvalidPort, result.Failure);
	}

	[TestMethod]
	[DataRow("None", AutoExecMailSecurity.None)]
	[DataRow("Auto", AutoExecMailSecurity.Auto)]
	[DataRow("StartTls", AutoExecMailSecurity.StartTls)]
	[DataRow("StartTlsWhenAvailable", AutoExecMailSecurity.StartTlsWhenAvailable)]
	[DataRow("SslOnConnect", AutoExecMailSecurity.SslOnConnect)]
	public void Load_対応する暗号化方式_そのまま読み込む(string value, AutoExecMailSecurity expected) {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailSecurity, value);

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsNotNull(result.Settings);
		Assert.AreEqual(expected, result.Settings.Security);
	}

	[TestMethod]
	public void Load_未対応の暗号化方式_設定無効理由を返す() {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailSecurity, "Tls13Only");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.UnsupportedSecurity, result.Failure);
		Assert.AreEqual(MasterConfig.NameAutoExecMailSecurity, result.FailureSettingName);
	}

	[TestMethod]
	public void Load_認証なし_ユーザーIDとパスワードが空でも有効() {
		InsertValidSettings("None");
		UpdateValue(MasterConfig.NameAutoExecMailUserId, "");
		UpdateValue(MasterConfig.NameAutoExecMailUserPass, "");
		UpdateValue(MasterConfig.NameAutoExecMailSecurity, "None");
		UpdateValue(MasterConfig.NameAutoExecMailServerIp, "localhost");
		UpdateValue(MasterConfig.NameAutoExecMailServerPort, "25");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsTrue(result.IsValid);
		Assert.IsNotNull(result.Settings);
		Assert.AreEqual(AutoExecMailAuthMode.None, result.Settings.AuthMode);
		Assert.AreEqual(AutoExecMailSecurity.None, result.Settings.Security);
		Assert.AreEqual("localhost", result.Settings.Server);
		Assert.AreEqual(25, result.Settings.Port);
		Assert.AreEqual("", result.Settings.UserId);
		Assert.AreEqual("", result.Settings.Credential);
	}

	[TestMethod]
	public void Load_認証する_ユーザーIDが空なら設定無効理由を返す() {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailUserId, "");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.MissingValue, result.Failure);
		Assert.AreEqual(MasterConfig.NameAutoExecMailUserId, result.FailureSettingName);
	}

	[TestMethod]
	public void Load_送信元表示名が空_任意項目なので有効() {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailFromName, "");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsTrue(result.IsValid);
		Assert.IsNotNull(result.Settings);
		Assert.AreEqual("", result.Settings.FromName);
	}

	[TestMethod]
	public void Load_送信元表示名の行がない_任意項目なので有効() {
		InsertValidSettings("Password");
		Db.Delete(Db.Single<MasterConfig>("WHERE Name = @0", MasterConfig.NameAutoExecMailFromName));

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsTrue(result.IsValid);
	}

	[TestMethod]
	public void Load_未対応認証方式_設定無効理由を返す() {
		InsertValidSettings("0");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.UnsupportedAuthMode, result.Failure);
	}

	[TestMethod]
	[DataRow("from-address", AutoExecMailSettingsFailure.InvalidFromAddress)]
	[DataRow("to-address", AutoExecMailSettingsFailure.InvalidToAddress)]
	public void Load_不正なメールアドレス_設定無効理由を返す(
		string target,
		AutoExecMailSettingsFailure expectedFailure) {
		InsertValidSettings("Password");
		UpdateValue(
			target == "from-address"
				? MasterConfig.NameAutoExecMailFromAddr
				: MasterConfig.NameAutoExecMailToAddr,
			"invalid@@example.com");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(expectedFailure, result.Failure);
	}

	private void InsertValidSettings(string authMode, string credential = "password") {
		var values = new Dictionary<string, string> {
			[MasterConfig.NameAutoExecMailServerIp] = "smtp.example.com",
			[MasterConfig.NameAutoExecMailServerPort] = "587",
			[MasterConfig.NameAutoExecMailUserId] = "smtp-user",
			[MasterConfig.NameAutoExecMailUserPass] = credential,
			[MasterConfig.NameAutoExecMailSecurity] = "StartTls",
			[MasterConfig.NameAutoExecMailAuthMode] = authMode,
			[MasterConfig.NameAutoExecMailFromAddr] = "sender@example.com",
			[MasterConfig.NameAutoExecMailFromName] = "CV10 自動実行",
			[MasterConfig.NameAutoExecMailToAddr] = "first@example.com; second@example.com",
		};

		foreach (var (name, value) in values) {
			Db.Insert(new MasterConfig {
				Category = MasterConfig.CategoryAutoExec,
				Name = name,
				Val = value,
			});
		}
	}

	private void UpdateValue(string name, string value) {
		var row = Db.Single<MasterConfig>("WHERE Name = @0", name);
		row.Val = value;
		Db.Update(row, [nameof(MasterConfig.Val)]);
	}
}

[TestClass]
public class AutoExecMailServiceTests {
	[TestMethod]
	public async Task SendAsync_設定有効_渡された件名と本文を送信する() {
		var settings = ValidSettings();
		var transport = new RecordingTransport();
		var service = new AutoExecMailService(
			new StubSettingsLoader(new AutoExecMailSettingsLoadResult(
				settings,
				AutoExecMailSettingsFailure.None,
				null)),
			transport,
			NullLogger<AutoExecMailService>.Instance);
		var input = new AutoExecMailMessage("自動実行結果", "1行目\r\n2行目");

		var result = await service.SendAsync(input);

		Assert.IsTrue(result.Sent);
		Assert.IsNotNull(transport.Message);
		Assert.AreEqual(input.Subject, transport.Message.Subject);
		Assert.AreEqual(input.Body, transport.Message.TextBody);
		Assert.AreEqual("sender@example.com", transport.Message.From.Mailboxes.Single().Address);
		Assert.AreEqual(2, transport.Message.To.Count);
	}

	[TestMethod]
	public async Task SendAsync_設定無効_SMTPへ接続せず非送信理由を返す() {
		var transport = new RecordingTransport();
		var service = new AutoExecMailService(
			new StubSettingsLoader(new AutoExecMailSettingsLoadResult(
				null,
				AutoExecMailSettingsFailure.MissingValue,
				MasterConfig.NameAutoExecMailUserPass)),
			transport,
			NullLogger<AutoExecMailService>.Instance);

		var result = await service.SendAsync(new AutoExecMailMessage("subject", "body"));

		Assert.IsFalse(result.Sent);
		Assert.AreEqual(AutoExecMailNotSentReason.InvalidConfiguration, result.NotSentReason);
		Assert.AreEqual(AutoExecMailSettingsFailure.MissingValue, result.SettingsFailure);
		Assert.IsNull(transport.Message);
	}

	[TestMethod]
	public async Task SendAsync_SMTP失敗_例外を呼出元へ伝播する() {
		var service = new AutoExecMailService(
			new StubSettingsLoader(new AutoExecMailSettingsLoadResult(
				ValidSettings(),
				AutoExecMailSettingsFailure.None,
				null)),
			new ThrowingTransport(),
			NullLogger<AutoExecMailService>.Instance);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => service.SendAsync(new AutoExecMailMessage("subject", "body")));
	}

	[TestMethod]
	public async Task SendAsync_送信元表示名が空_アドレスだけを差出人にする() {
		var transport = new RecordingTransport();
		var service = new AutoExecMailService(
			new StubSettingsLoader(new AutoExecMailSettingsLoadResult(
				ValidSettings(fromName: ""),
				AutoExecMailSettingsFailure.None,
				null)),
			transport,
			NullLogger<AutoExecMailService>.Instance);

		await service.SendAsync(new AutoExecMailMessage("subject", "body"));

		Assert.IsNotNull(transport.Message);
		var from = transport.Message.From.Mailboxes.Single();
		Assert.AreEqual("sender@example.com", from.Address);
		Assert.IsTrue(string.IsNullOrEmpty(from.Name));
	}

	[TestMethod]
	[DataRow(AutoExecMailSecurity.None, SecureSocketOptions.None)]
	[DataRow(AutoExecMailSecurity.Auto, SecureSocketOptions.Auto)]
	[DataRow(AutoExecMailSecurity.StartTls, SecureSocketOptions.StartTls)]
	[DataRow(AutoExecMailSecurity.StartTlsWhenAvailable, SecureSocketOptions.StartTlsWhenAvailable)]
	[DataRow(AutoExecMailSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
	public void ToSecureSocketOptions_設定の暗号化方式をMailKitのオプションへ変換する(
		AutoExecMailSecurity security,
		SecureSocketOptions expected) {
		Assert.AreEqual(expected, MailKitAutoExecMailTransport.ToSecureSocketOptions(security));
	}

	[TestMethod]
	public void Describe_未送信の結果_理由と設定名を含む() {
		var result = new AutoExecMailSendResult(
			false,
			AutoExecMailNotSentReason.InvalidConfiguration,
			AutoExecMailSettingsFailure.MissingValue,
			MasterConfig.NameAutoExecMailToAddr);

		var text = AutoExecMailResultText.Describe(result);

		Assert.IsTrue(text.Contains("送信しませんでした", StringComparison.Ordinal));
		Assert.IsTrue(text.Contains(MasterConfig.NameAutoExecMailToAddr, StringComparison.Ordinal));
	}

	[TestMethod]
	public void Describe_送信済みの結果_送信したことを示す() {
		Assert.AreEqual("送信しました", AutoExecMailResultText.Describe(AutoExecMailSendResult.Success));
	}

	private static AutoExecMailSettings ValidSettings(
		AutoExecMailAuthMode authMode = AutoExecMailAuthMode.Password,
		string fromName = "CV10 自動実行") => new(
		"smtp.example.com",
		587,
		AutoExecMailSecurity.StartTls,
		"smtp-user",
		"password",
		authMode,
		"sender@example.com",
		fromName,
		["first@example.com", "second@example.com"]);

	private sealed class StubSettingsLoader : IAutoExecMailSettingsLoader {
		private readonly AutoExecMailSettingsLoadResult _result;

		public StubSettingsLoader(AutoExecMailSettingsLoadResult result) {
			_result = result;
		}

		public AutoExecMailSettingsLoadResult Load() => _result;
	}

	private sealed class RecordingTransport : IAutoExecMailTransport {
		public MimeMessage? Message { get; private set; }

		public Task SendAsync(
			AutoExecMailSettings settings,
			MimeMessage message,
			CancellationToken cancellationToken = default) {
			Message = message;
			return Task.CompletedTask;
		}
	}

	private sealed class ThrowingTransport : IAutoExecMailTransport {
		public Task SendAsync(
			AutoExecMailSettings settings,
			MimeMessage message,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("SMTP failure");
	}
}
