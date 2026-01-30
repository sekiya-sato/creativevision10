using CodeShare;
using Cvnet10AppShared;
using Cvnet10Base;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace Cvnet10Wpfclient.ViewModels {
    public partial class MainMenuViewModel {

		SysHistJwtSub subTestGetInfo() {
			var ipAddr = Common.GetIPAddress().FirstOrDefault();

			var jsub = new SysHistJwtSub {
				IpAddress = ipAddr.IPAddress.ToString(),
				MacAddress = ipAddr.MacAddress,
				Machine = Environment.MachineName,
				User = Environment.UserName,
				OsVer = Environment.OSVersion.Version.ToString(),
			};
			return jsub;
		}

		/// <summary>
		/// サービス ILoginService : CreateLogin のテスト
		/// </summary>
		/// <param name="channel"></param>
		/// <returns></returns>
		async Task<string> TestCreateLogin(GrpcChannel channel) {
			var loginService = channel.CreateGrpcService<ILoginService>();
			var now = DateTime.UtcNow;

			var loginRequest = new LoginRequest {
				LoginId = "TestUser",
				Name = "てすとユーザ",
				CryptPassword = Common.EncryptLoginRequest("TestUser", now),
				LoginDate = now,
				Info = Common.SerializeObject(subTestGetInfo()),
			};

			var reply = await loginService.CreateLoginAsync(loginRequest);
			if (reply.Result == 0) {
				AppCurrent.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppCurrent.LoginJwt}");
				return await Task.FromResult<string>($"ログイン成功: {reply.JwtMessage}");
			}
			else {
				return await Task.FromResult<string>("ログイン失敗");
			}
		}
		/// <summary>
		/// サービス ILoginService : Login のテスト
		/// </summary>
		/// <param name="channel"></param>
		/// <returns></returns>
		async Task<string> TestLogin(GrpcChannel channel) {
			var loginService = channel.CreateGrpcService<ILoginService>();
			var now = DateTime.UtcNow;

			var loginRequest = new LoginRequest {
				LoginId = "TestUser",
				Name = "てすとユーザ",
				CryptPassword = Common.EncryptLoginRequest("TestUser", now),
				LoginDate = now,
				Info = Common.SerializeObject(subTestGetInfo()),
			};

			var reply = await loginService.LoginAsync(loginRequest);
			if (reply.Result == 0) {
				AppCurrent.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppCurrent.LoginJwt}");
				if (reply.JwtMessage?.Length > 10) {
					AppCurrent.LoginJwt = reply.JwtMessage;
				}
				return await Task.FromResult<string>($"ログイン成功: {reply.JwtMessage}");
			}
			else {
				return await Task.FromResult<string>("ログイン失敗");
			}
		}
		string dummyToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoi44Gm44GZ44Go44Om44O844K2IiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiMCIsImV4cCI6MTc3ODMxNDEyMSwiaXNzIjoiSXNzdWVyX0RldmVsb3BtZW50In0.2Dj0vQ3ZmE4InY40r5n5420Tv3G-t4RoPAiJT5xJe5s";

		/// <summary>
		/// サービス ILoginService : LoginRefresh のテスト (承認情報なしでのアクセス)
		/// </summary>
		/// <param name="channel"></param>
		/// <returns></returns>
		async Task<string> TestLoginRefreshNoAuth(GrpcChannel channel) {
			var loginService = channel.CreateGrpcService<ILoginService>();
			var now = DateTime.UtcNow;
			var loginRequest = new LoginRefresh { Token = dummyToken, Info = Common.SerializeObject(subTestGetInfo()) };

			var reply = await loginService.LoginRefleshAsync(loginRequest);
			if (reply.Result == 0) {
				AppCurrent.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppCurrent.LoginJwt}");
				return await Task.FromResult<string>($"ログイン成功: {reply.JwtMessage}");
			}
			else {
				return await Task.FromResult<string>("ログイン失敗");
			}
		}
		/// <summary>
		/// サービス ILoginService : LoginRefresh のテスト (承認情報ありでのアクセス)
		/// </summary>
		/// <param name="channel"></param>
		/// <returns></returns>
		async Task<string> TestLoginRefresh(GrpcChannel channel) {
			var loginService = channel.CreateGrpcService<ILoginService>();
			var now = DateTime.UtcNow;
			var loginRequest = new LoginRefresh { Token = dummyToken,Info = Common.SerializeObject(subTestGetInfo()) };
			AppCurrent.LoginJwt = dummyToken;
			var reply = await loginService.LoginRefleshAsync(loginRequest, AppCurrent.GetDefaultCallContext());
			if (reply.Result == 0) {
				AppCurrent.LoginJwt = reply.JwtMessage;
				Debug.WriteLine($"{DateTime.Now} AppCurrent.LoginJwt={AppCurrent.LoginJwt}");
				return await Task.FromResult<string>($"ログイン成功: {reply.JwtMessage}");
			}
			else {
				return await Task.FromResult<string>("ログイン失敗");
			}
		}
		async Task<string> TestQueryMsg(GrpcChannel channel) {
			var coreService = channel.CreateGrpcService<ICvnetCoreService>();
			AppCurrent.LoginJwt = dummyToken;
			var msg = new CvnetMsg { Flag = CvnetFlag.MSg005_Test };
			var reply =  await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
			var payload = string.IsNullOrWhiteSpace(reply.DataMsg) ? "[]" : reply.DataMsg;
			var list = Common.DeserializeObject<List<Test202601Master>>(payload) ?? [];
			TestMasters = new ObservableCollection<Test202601Master>(list);
			SelectedTestMaster = TestMasters.FirstOrDefault();
			Debug.WriteLine(payload ?? "---");
			return await Task.FromResult($"取得件数: {TestMasters.Count}");
		}


	}
}
