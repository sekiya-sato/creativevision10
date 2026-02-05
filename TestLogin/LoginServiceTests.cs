using System.Globalization;
using System.Net;
using CodeShare;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10DomainLogic;
using Cvnet10Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task LoginRefleshAsync_WithValidToken_ReturnsExtendedToken() {
        using var context = new LoginServiceTestContext();
        var loginDate = DateTime.UtcNow;
        InsertLoginRecord(context, "user01", "Secret!2", loginDate);
        var request = CreateLoginRequest("user01", "Secret!2", loginDate);

        var loginReply = await context.Service.LoginAsync(request);
        Assert.AreEqual(0, loginReply.Result);

        var refreshRequest = new LoginRefresh {
            Token = loginReply.JwtMessage,
            Info = CreateInfoJson(),
        };

        var refreshReply = await context.Service.LoginRefleshAsync(refreshRequest);

        Assert.AreEqual(0, refreshReply.Result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(refreshReply.JwtMessage));

        var history = context.Database.Fetch<SysHistJwt>("SELECT * FROM SysHistJwt ORDER BY Id");
        Assert.HasCount(2, history);
        Assert.AreEqual("LoginRefleshAsync", history[1].Op);
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

    static SysLogin InsertLoginRecord(LoginServiceTestContext context, string loginId, string password, DateTime loginDate) {
        var ticks = loginDate.ToUniversalTime().Ticks;
        var storedPassword = Common.EncryptLoginRequest(password, new DateTime(ticks).ToLocalTime());
        var entity = new SysLogin {
            LoginId = loginId,
            CryptPassword = storedPassword,
            ExpDate = loginDate.AddDays(1).ToDtStrDateTimeShort(),
            LastDate = loginDate.ToDtStrDateTimeShort(),
            Id_Role = 1,
            Id_Shain = 1,
            Vdc = ticks,
            Vdu = ticks,
        };
        context.Database.Insert(entity);
        return entity;
    }

}
