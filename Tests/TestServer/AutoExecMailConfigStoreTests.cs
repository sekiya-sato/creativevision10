using CvBase;
using CvBaseSqlite;
using CvServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 設定画面から自動実行結果メール設定を読み書きする <see cref="AutoExecMailConfigStore"/> のテスト。
/// </summary>
[TestClass]
public class AutoExecMailConfigStoreTests {
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
	public void Read_行がひとつもない_すべて空で登録なしを返す() {
		var values = new AutoExecMailConfigStore(Db).Read();

		Assert.AreEqual(string.Empty, values.Server);
		Assert.AreEqual(string.Empty, values.Port);
		Assert.AreEqual(string.Empty, values.Security);
		Assert.AreEqual(string.Empty, values.AuthMode);
		Assert.AreEqual(string.Empty, values.UserId);
		Assert.AreEqual(string.Empty, values.FromAddress);
		Assert.AreEqual(string.Empty, values.FromName);
		Assert.AreEqual(string.Empty, values.ToAddress);
		Assert.IsFalse(values.HasCredential);
	}

	[TestMethod]
	public void Write_行がない状態から保存_Readで読み戻せて前後の空白は取り除かれる() {
		var store = new AutoExecMailConfigStore(Db);

		store.Write(Values(server: " smtp.example.com ", port: " 587 ", fromName: " 自動実行通知 "), "secret");

		var values = store.Read();
		Assert.AreEqual("smtp.example.com", values.Server);
		Assert.AreEqual("587", values.Port);
		Assert.AreEqual("自動実行通知", values.FromName);
		Assert.IsTrue(values.HasCredential);
		// パスワードは Read では返さない。値そのものはMasterConfigに保存されている。
		Assert.AreEqual("secret", StoredValue(MasterConfig.NameAutoExecMailUserPass));
	}

	[TestMethod]
	public void Write_既存行がある状態から保存_同じ行を更新して重複させない() {
		var store = new AutoExecMailConfigStore(Db);
		store.Write(Values(server: "old.example.com"), "secret");

		store.Write(Values(server: "new.example.com"), null);

		Assert.AreEqual("new.example.com", store.Read().Server);
		var rows = Db.Fetch<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", MasterConfig.NameAutoExecMailServerIp);
		Assert.AreEqual(1, rows.Count);
	}

	[TestMethod]
	public void Write_パスワードにnull_既存のパスワードを変更しない() {
		var store = new AutoExecMailConfigStore(Db);
		store.Write(Values(), "secret");

		store.Write(Values(server: "changed.example.com"), null);

		Assert.AreEqual("secret", StoredValue(MasterConfig.NameAutoExecMailUserPass));
		Assert.IsTrue(store.Read().HasCredential);
	}

	[TestMethod]
	public void Write_パスワードに空文字_既存のパスワードを消去する() {
		var store = new AutoExecMailConfigStore(Db);
		store.Write(Values(), "secret");

		store.Write(Values(), string.Empty);

		Assert.AreEqual(string.Empty, StoredValue(MasterConfig.NameAutoExecMailUserPass));
		Assert.IsFalse(store.Read().HasCredential);
	}

	[TestMethod]
	public void Write_パスワードの前後の空白_意味を持つので取り除かない() {
		var store = new AutoExecMailConfigStore(Db);

		store.Write(Values(), " pass ");

		Assert.AreEqual(" pass ", StoredValue(MasterConfig.NameAutoExecMailUserPass));
	}

	[TestMethod]
	public void Write_保存した値_Loaderがそのまま送信設定として読める() {
		new AutoExecMailConfigStore(Db).Write(
			Values(server: "localhost", port: "25", security: "None", authMode: "None", userId: "", fromName: ""),
			string.Empty);

		var result = new AutoExecMailSettingsLoader(Db).Load();

		Assert.IsTrue(result.IsValid, $"Failure={result.Failure}, SettingName={result.FailureSettingName}");
		Assert.IsNotNull(result.Settings);
		Assert.AreEqual("localhost", result.Settings.Server);
		Assert.AreEqual(25, result.Settings.Port);
		Assert.AreEqual(AutoExecMailSecurity.None, result.Settings.Security);
		Assert.AreEqual(AutoExecMailAuthMode.None, result.Settings.AuthMode);
	}

	[TestMethod]
	public void Validate_すべて空_未入力は許して有効を返す() {
		var validation = AutoExecMailConfigStore.Validate(AutoExecMailConfigValues.Empty);

		Assert.IsTrue(validation.IsValid);
		Assert.IsNull(validation.ErrorSettingName);
	}

	[TestMethod]
	public void Validate_入力済みの正しい値_有効を返す() {
		Assert.IsTrue(AutoExecMailConfigStore.Validate(Values()).IsValid);
	}

	[TestMethod]
	[DataRow("0")]
	[DataRow("65536")]
	[DataRow("not-number")]
	[DataRow("58 7")]
	public void Validate_不正なポート番号_ポート番号の設定名を返す(string port) {
		var validation = AutoExecMailConfigStore.Validate(Values(port: port));

		Assert.IsFalse(validation.IsValid);
		Assert.AreEqual(MasterConfig.NameAutoExecMailServerPort, validation.ErrorSettingName);
	}

	[TestMethod]
	public void Validate_未対応の暗号化方式_暗号化方式の設定名を返す() {
		var validation = AutoExecMailConfigStore.Validate(Values(security: "Tls13Only"));

		Assert.IsFalse(validation.IsValid);
		Assert.AreEqual(MasterConfig.NameAutoExecMailSecurity, validation.ErrorSettingName);
	}

	[TestMethod]
	public void Validate_未対応の認証方式_認証方式の設定名を返す() {
		var validation = AutoExecMailConfigStore.Validate(Values(authMode: "Ntlm"));

		Assert.IsFalse(validation.IsValid);
		Assert.AreEqual(MasterConfig.NameAutoExecMailAuthMode, validation.ErrorSettingName);
	}

	[TestMethod]
	public void Validate_不正な送信元アドレス_送信元アドレスの設定名を返す() {
		var validation = AutoExecMailConfigStore.Validate(Values(fromAddress: "invalid@@example.com"));

		Assert.IsFalse(validation.IsValid);
		Assert.AreEqual(MasterConfig.NameAutoExecMailFromAddr, validation.ErrorSettingName);
	}

	[TestMethod]
	[DataRow("invalid@@example.com")]
	[DataRow("first@example.com,")]
	[DataRow("first@example.com;;second@example.com")]
	public void Validate_不正な送信先アドレス_送信先アドレスの設定名を返す(string toAddress) {
		var validation = AutoExecMailConfigStore.Validate(Values(toAddress: toAddress));

		Assert.IsFalse(validation.IsValid);
		Assert.AreEqual(MasterConfig.NameAutoExecMailToAddr, validation.ErrorSettingName);
	}

	[TestMethod]
	public void Validate_複数の送信先アドレス_区切り文字で並べても有効() {
		Assert.IsTrue(AutoExecMailConfigStore.Validate(
			Values(toAddress: "first@example.com, second@example.com; third@example.com")).IsValid);
	}

	private static AutoExecMailConfigValues Values(
		string server = "smtp.example.com",
		string port = "587",
		string security = "StartTls",
		string authMode = "Password",
		string userId = "smtp-user",
		string fromAddress = "sender@example.com",
		string fromName = "CV10 自動実行",
		string toAddress = "admin@example.com") =>
		new(server, port, security, authMode, userId, fromAddress, fromName, toAddress, false);

	private string StoredValue(string name) =>
		Db.Single<MasterConfig>("WHERE Name = @0", name).Val ?? string.Empty;
}
