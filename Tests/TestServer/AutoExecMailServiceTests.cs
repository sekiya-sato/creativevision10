using CvBase;
using CvBaseSqlite;
using CvServer.Services;
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
	public void Load_有効なOAuth2設定_UserPassをアクセストークンとして保持する() {
		InsertValidSettings("OAuth2", "access-token");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsNotNull(result.Settings);
		Assert.AreEqual(AutoExecMailAuthMode.OAuth2, result.Settings.AuthMode);
		Assert.AreEqual("access-token", result.Settings.Credential);
		Assert.IsFalse(result.Settings.ToString().Contains("access-token", StringComparison.Ordinal));
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
	[DataRow(MasterConfig.NameAutoExecMailFromName)]
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
	public void Load_StartTls以外_設定無効理由を返す() {
		InsertValidSettings("Password");
		UpdateValue(MasterConfig.NameAutoExecMailSecurity, "SslOnConnect");

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.AreEqual(AutoExecMailSettingsFailure.UnsupportedSecurity, result.Failure);
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

	private static AutoExecMailSettings ValidSettings() => new(
		"smtp.example.com",
		587,
		"smtp-user",
		"password",
		AutoExecMailAuthMode.Password,
		"sender@example.com",
		"CV10 自動実行",
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
