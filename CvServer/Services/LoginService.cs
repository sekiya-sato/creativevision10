using CodeShare;
using CvAsset;
using CvBase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


namespace CvServer.Services;

public partial class LoginService : ILoginService {
	private readonly ILogger<LoginService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly ExDatabase _db;
	// private readonly ISchedulerService _scheduler;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly JwtSettings _jwtSettings;
	private readonly AppGlobal _appGlobal;
	private readonly JwtSecurityTokenHandler _tokenHandler = new();
	public LoginService(ILogger<LoginService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor,
		ExDatabase db, JwtSettings? jwtSettings = null, AppGlobal? appGlobal = null) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		ArgumentNullException.ThrowIfNull(db);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_db = db;
		// _scheduler = scheduler;
		_httpContextAccessor = httpContextAccessor;
		_jwtSettings = jwtSettings ?? new JwtSettings(configuration);
		_appGlobal = appGlobal ?? AppGlobal.Shared;
	}

	/// <summary>
	/// Login処理を行いJWTを返す
	/// [Performs login processing and returns a JWT]
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[AllowAnonymous]
	public Task<LoginReply> LoginAsync(LoginRequest request, ProtoBuf.Grpc.CallContext context = default) {
		var claims = new List<Claim> { // Nameだけ入れて256byte程度。EmailやPasswordもいれるとサイズ増える。600byte程度。
										   // [About 256 bytes with just the name. Including email and password increases the size to about 600 bytes]
			new Claim(ClaimTypes.Name, request.Name),
			};

		var cnt = _db.Fetch<long>($"SELECT count(*) cnt FROM SysLogin").FirstOrDefault();
		if (cnt == 0) {
			// レコードが0件の場合、初回起動とみなし無条件でログイン成功させる
			var initLogin = new SysLogin {
				LoginId = request.LoginId,
				CryptPassword = request.CryptPassword,
				Vdc = request.LoginDate.ToUnixTime(),
				Vdu = request.LoginDate.ToUnixTime(),
				ExpDate = DateTime.Now.AddYears(1).ToDtStrDateTimeShort(),
				LastDate = DateTime.Now.ToDtStrDateTimeShort(),
			};
			var jwt = CreateToken(claims, _jwtSettings.Lifetime);
			if (request.Info != null) {
				InsertLoginHistory(jwt, request.Info, "LoginAsync First", -9);
			}
			return Task.FromResult(CreateLoginReply(jwt, role: 0, includeInfoPayload: true));
		}
		var loginData = _db.Fetch<SysLogin>($"where LoginId=@0", [request.LoginId]).FirstOrDefault();

		if (loginData == null) {
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
		}
		else { // パスワードと有効期限のチェック [Checks for password and expiration date]
			   // もらったパスワードを復元してみる Decryptのpassが違ってるとException
			   // [Try to restore the received password; if the pass for Decrypt is incorrect, an exception occurs]
			var restorePass = Common.DecryptLoginRequest(request.CryptPassword, request.LoginDate);

			var orgPlanePass = (loginData.CryptPassword != null) ? Common.DecryptLoginRequest(loginData.CryptPassword, loginData.VdateC) : "";
			if (orgPlanePass != restorePass)
				return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
			if (DateTime.Now.ToDtStrDateTimeShort().CompareTo(loginData.ExpDate) > 0) // Nowのほうが大きければエラー [If "Now" is greater, an error occurs]
				return Task.FromResult(new LoginReply { JwtMessage = "", Result = -2 });
			var userCheck = ValidateUserExpiration(loginData.Id_Shain);
			if (userCheck != null)
				return Task.FromResult(userCheck);
			loginData.Vdu = Common.GetVdate();
			loginData.LastDate = loginData.VdateU.ToDtStrDateTimeShort();
			_db.Update(loginData, ["Vdu", "LastDate"]);
			claims.Add(new Claim(ClaimTypes.Role,
				(loginData.Id_Role != 0) ? loginData.Id_Role.ToString() : loginData.Id_Shain.ToString()));
			claims.Add(new Claim(ClaimTypes.SerialNumber, loginData.Id.ToString()));
		}
		var loginJwt = CreateToken(claims, _jwtSettings.Lifetime);
		InsertLoginHistory(loginJwt, request.Info, "LoginAsync", loginData.Id);
		return Task.FromResult(CreateLoginReply(loginJwt, loginData.Id_Role, includeInfoPayload: true));
	}
	/// <summary>
	/// トークン作成共通ロジック
	/// [Common logic for token creation]
	/// </summary>
	/// <param name="claims"></param>
	/// <param name="lifetime"></param>
	/// <returns></returns>
	private JwtSecurityToken CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime, string? issuer = null) {
		return new JwtSecurityToken(
			issuer: _jwtSettings.ResolveIssuer(issuer),
			claims: claims,
			expires: DateTime.UtcNow.Add(lifetime),
			signingCredentials: _jwtSettings.CreateSigningCredentials());
	}
	/// <summary>
	/// リフレッシュトークンの取得(app.settings.jsonのRefreshtime 分)
	/// [Obtaining the refresh token (based on Refreshtime in app.settings.json)]
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="SecurityTokenException"></exception>
	[Authorize]
	public Task<LoginReply> LoginRefreshAsync(LoginRefresh request, ProtoBuf.Grpc.CallContext context = default) {
		// トークンからexpires を取得して、新しいトークンを作成する [Retrieve expires from the token and create a new token]
		// トークンを解析 [Parse the token]
		var jsonToken = _tokenHandler.ReadToken(request.Token) as JwtSecurityToken;
		if (jsonToken == null) {
			throw new SecurityTokenException("Invalid token");
		}
		// トークンに紐づくSysLoginの社員有効期限をチェック（初回起動トークンはSerialNumberが無いためスキップ）
		// [Check the employee expiration of the SysLogin associated with the token; skip for first-launch tokens without SerialNumber]
		var serialNumberClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.SerialNumber);
		// リフレッシュ後もクライアントのロール別メニューを維持するため、SysLoginのロールを引き継ぐ
		long refreshRole = 0;
		if (serialNumberClaim != null && long.TryParse(serialNumberClaim.Value, out var loginId)) {
			var loginDataRefresh = _db.Fetch<SysLogin>($"where Id=@0", [loginId]).FirstOrDefault();
			if (loginDataRefresh == null)
				return Task.FromResult(new LoginReply { JwtMessage = "", Result = -2 });
			var userCheck = ValidateUserExpiration(loginDataRefresh.Id_Shain);
			if (userCheck != null)
				return Task.FromResult(userCheck);
			refreshRole = loginDataRefresh.Id_Role;
		}
		var jwt = CreateToken(jsonToken.Claims, _jwtSettings.RefreshLifetime, jsonToken.Issuer);
		InsertLoginHistory(jwt, request.Info, "LoginRefreshAsync");
		return Task.FromResult(CreateLoginReply(jwt, refreshRole, includeInfoPayload: false));
	}

	/// <summary>
	/// SysLoginレコード作成処理を行いJWTを返す
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	//[AllowAnonymous]
	[Authorize]
	public Task<LoginReply> CreateLoginAsync(LoginRequest request, ProtoBuf.Grpc.CallContext context = default) {
		var loginData = _db.Fetch<SysLogin>($"where LoginId=@0", [request.LoginId]).FirstOrDefault();
		if (loginData != null) {
			// すでに同IDが存在する場合はエラー
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
		}
		var claims = new List<Claim> {
			new Claim(ClaimTypes.Name, request.Name),
			};
		var restorePass = Common.DecryptLoginRequest(request.CryptPassword, request.LoginDate);
		var vdate = Common.GetVdate();
		var initLogin = new SysLogin {
			LoginId = request.LoginId,
			CryptPassword = Common.EncryptLoginRequest(restorePass, new DateTime(vdate).ToLocalTime()),
			Vdc = vdate,
			Vdu = vdate,
			ExpDate = Common.FromUtcTicks(vdate).AddYears(1).ToDtStrDateTimeShort(), // 1年有効 [Valid for 1 year]
			LastDate = Common.FromUtcTicks(vdate).ToDtStrDateTimeShort(),
		};
		_db.Insert<SysLogin>(initLogin);
		var jwt = CreateToken(claims, _jwtSettings.Lifetime);
		InsertLoginHistory(jwt, request.Info, "CreateLoginAsync", initLogin.Id);
		return Task.FromResult(CreateLoginReply(jwt, role: 0, includeInfoPayload: true));
	}
	/// <summary>
	/// 社員マスタの有効期限をチェックする
	/// [Check the employee master expiration date]
	/// </summary>
	/// <param name="idShain"></param>
	/// <returns>OKの場合null、エラーの場合エラー用LoginReply</returns>
	LoginReply? ValidateUserExpiration(long idShain) {
		if (idShain == 0)
			return new LoginReply { JwtMessage = "", Result = -2 };
		var shain = _db.Fetch<MasterShain>($"where Id=@0", [idShain]).FirstOrDefault();
		if (shain == null)
			return new LoginReply { JwtMessage = "", Result = -2 };
		if (!string.IsNullOrEmpty(shain.ExpireDate) && shain.ExpireDate.CompareTo(DateTime.Now.ToDtStrDate2()) < 0)
			return new LoginReply { JwtMessage = "", Result = -2 };
		return null;
	}

	/// <summary>
	/// 追加情報をセットする
	/// </summary>
	/// <returns></returns>
	private string GetAddInfo() {
		return Common.SerializeObject(_appGlobal.VerInfo);
	}

	private LoginReply CreateLoginReply(JwtSecurityToken jwt, long role, bool includeInfoPayload) {
		var reply = new LoginReply {
			JwtMessage = _tokenHandler.WriteToken(jwt),
			Result = 0,
			Expire = jwt.ValidTo.ToLocalTime(),
			Role = role,
		};
		if (includeInfoPayload) {
			reply.InfoPayload = GetAddInfo();
		}
		return reply;
	}

	private void InsertLoginHistory(JwtSecurityToken jwt, string info, string operation, long loginId = 0) {
		var loginHistory = new SysHistJwt {
			Id_Login = loginId,
			JwtUnixTime = jwt.ValidTo.ToUnixTime(),
			ExpDate = jwt.ValidTo.ToLocalTime().ToDtStrDateTimeShort(),
			Ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? ".",
			Jsub = Common.DeserializeObject<SysHistJwtSub>(info) ?? new(),
			Op = operation,
		};
		_db.Insert(loginHistory);
	}
}
