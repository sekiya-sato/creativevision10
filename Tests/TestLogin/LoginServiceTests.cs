using CodeShare;
using CvAsset;
using CvBase;
using Newtonsoft.Json;

namespace TestLogin;

[TestClass]
public sealed class LoginServiceTests {
	[TestMethod]
	public async Task LoginAsync_WhenNoAccountsExist_AllowsInitialLogin() {
		using var context = new LoginServiceTestContext();
		var request = CreateLoginRequest("init-user", "InitPass!1", DateTime.UtcNow);

		var reply = await context.Service.LoginAsync(request);

		Assert.AreEqual(0, reply.Result);
		Assert.IsFalse(string.IsNullOrEmpty(reply.JwtMessage));

		var history = context.Database.Fetch<SysHistJwt>("SELECT * FROM SysHistJwt");
		Assert.HasCount(1, history);
		Assert.AreEqual(-9, history[0].Id_Login);
	}

	[TestMethod]
	public async Task LoginRefreshAsync_WithValidToken_ReturnsExtendedToken() {
		using var context = new LoginServiceTestContext();
		var loginDate = DateTime.UtcNow;
		var request = CreateLoginRequest("user01", "Secret!2", loginDate);

		var loginReply = await context.Service.LoginAsync(request);
		Assert.AreEqual(0, loginReply.Result);

		var refreshRequest = new LoginRefresh {
			Token = loginReply.JwtMessage,
			Info = CreateInfoJson(),
		};

		var refreshReply = await context.Service.LoginRefreshAsync(refreshRequest);

		Assert.AreEqual(0, refreshReply.Result);
		Assert.IsFalse(string.IsNullOrWhiteSpace(refreshReply.JwtMessage));

		var history = context.Database.Fetch<SysHistJwt>("SELECT * FROM SysHistJwt ORDER BY Id");
		Assert.HasCount(2, history);
		Assert.AreEqual("LoginRefreshAsync", history[1].Op);
	}

	[TestMethod]
	public async Task LoginAsync_WithValidShain_ReturnsToken() {
		using var context = new LoginServiceTestContext();
		var shain = context.CreateShain("S001", "有効社員", DateTime.Now.AddYears(1).ToDtStrDate2());
		context.CreateLogin("user01", shain.Id, "Secret!2", DateTime.UtcNow);
		var request = CreateLoginRequest("user01", "Secret!2", DateTime.UtcNow);

		var reply = await context.Service.LoginAsync(request);

		Assert.AreEqual(0, reply.Result);
		Assert.IsFalse(string.IsNullOrWhiteSpace(reply.JwtMessage));
	}

	[TestMethod]
	public async Task LoginAsync_WithExpiredShain_ReturnsError() {
		using var context = new LoginServiceTestContext();
		var shain = context.CreateShain("S001", "期限切れ社員", DateTime.Now.AddDays(-1).ToDtStrDate2());
		context.CreateLogin("user01", shain.Id, "Secret!2", DateTime.UtcNow);
		var request = CreateLoginRequest("user01", "Secret!2", DateTime.UtcNow);

		var reply = await context.Service.LoginAsync(request);

		Assert.AreEqual(-2, reply.Result);
	}

	[TestMethod]
	public async Task LoginAsync_WithNoShain_ReturnsError() {
		using var context = new LoginServiceTestContext();
		context.CreateLogin("user01", 0, "Secret!2", DateTime.UtcNow);
		var request = CreateLoginRequest("user01", "Secret!2", DateTime.UtcNow);

		var reply = await context.Service.LoginAsync(request);

		Assert.AreEqual(-2, reply.Result);
	}

	[TestMethod]
	public async Task LoginRefreshAsync_WithExpiredShain_ReturnsError() {
		using var context = new LoginServiceTestContext();
		var shain = context.CreateShain("S001", "期限切れ社員", DateTime.Now.AddYears(1).ToDtStrDate2());
		context.CreateLogin("user01", shain.Id, "Secret!2", DateTime.UtcNow);
		var loginRequest = CreateLoginRequest("user01", "Secret!2", DateTime.UtcNow);

		var loginReply = await context.Service.LoginAsync(loginRequest);
		Assert.AreEqual(0, loginReply.Result);

		// ログイン後に社員の有効期限を切らせてリフレッシュを検証
		// [After login, expire the employee and verify refresh fails]
		shain.ExpireDate = DateTime.Now.AddDays(-1).ToDtStrDate2();
		context.Database.Update(shain, [nameof(MasterShain.ExpireDate)]);

		var refreshRequest = new LoginRefresh {
			Token = loginReply.JwtMessage,
			Info = CreateInfoJson(),
		};
		var refreshReply = await context.Service.LoginRefreshAsync(refreshRequest);

		Assert.AreEqual(-2, refreshReply.Result);
	}

	[TestMethod]
	public async Task CreateLoginAsync_WithUniqueLoginId_PersistsUserAndHistory() {
		using var context = new LoginServiceTestContext();
		var request = CreateLoginRequest("new-user", "Create!3", DateTime.UtcNow);

		var reply = await context.Service.CreateLoginAsync(request);

		Assert.AreEqual(0, reply.Result);
		Assert.IsFalse(string.IsNullOrWhiteSpace(reply.JwtMessage));

		var logins = context.Database.Fetch<SysLogin>("SELECT * FROM SysLogin");
		Assert.HasCount(1, logins);
		Assert.AreEqual("new-user", logins[0].LoginId);

		var history = context.Database.Fetch<SysHistJwt>("SELECT * FROM SysHistJwt");
		Assert.HasCount(1, history);
		Assert.AreEqual("CreateLoginAsync", history[0].Op);
	}

	static LoginRequest CreateLoginRequest(string loginId, string password, DateTime loginDate) {
		return new LoginRequest {
			Name = "UnitTest",
			LoginId = loginId,
			LoginDate = loginDate,
			CryptPassword = Common.EncryptLoginRequest(password, loginDate),
			Info = CreateInfoJson(),
		};
	}

	static string CreateInfoJson() {
		var info = new SysHistJwtSub {
			Machine = "UT",
			User = "tester",
			OsVer = "test-os",
			IpAddress = "127.0.0.1",
			MacAddress = "00-00-00-00-00-00",
		};
		return JsonConvert.SerializeObject(info);
	}

}
