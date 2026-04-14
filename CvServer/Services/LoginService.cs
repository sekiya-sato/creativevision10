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
	// private readonly IScheduler _scheduler;
	private readonly IHttpContextAccessor _httpContextAccessor;
	public LoginService(ILogger<LoginService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, ExDatabase db) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		_db = db;
		// _scheduler = scheduler;
		_httpContextAccessor = httpContextAccessor;
	}
	/// <summary>
	/// Login処理を行いJWTを返す
	/// [Performs login processing and returns a JWT]
	/// </summary>
	/// <param name="userRequest"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[AllowAnonymous]
	public Task<LoginReply> LoginAsync(LoginRequest userRequest, ProtoBuf.Grpc.CallContext context = default) {
		var claims = new List<Claim> { // Nameだけ入れて256byte程度。EmailやPasswordもいれるとサイズ増える。600byte程度。
										   // [About 256 bytes with just the name. Including email and password increases the size to about 600 bytes]
			new Claim(ClaimTypes.Name, userRequest.Name),
			};

		var cnt = _db.Fetch<long>($"SELECT count(*) cnt FROM SysLogin").FirstOrDefault();
		if (cnt == 0) {
			// レコードが0件の場合、初回起動とみなし無条件でログイン成功させる
			var initLogin = new SysLogin {
				LoginId = userRequest.LoginId,
				CryptPassword = userRequest.CryptPassword,
				Vdc = userRequest.LoginDate.ToUnixTime(),
				Vdu = userRequest.LoginDate.ToUnixTime(),
				ExpDate = DateTime.Now.AddYears(1).ToDtStrDateTimeShort(),
				LastDate = DateTime.Now.ToDtStrDateTimeShort(),
			};
			if (_configuration.GetSection("WebAuthJwt") != null) {
				var lifetime = TimeSpan.FromMinutes(1);
				var webauthjwt = _configuration.GetSection("WebAuthJwt");
				if (int.TryParse(webauthjwt.GetSection("Lifetime")?.Value, out int minutes))
					lifetime = TimeSpan.FromMinutes(minutes);
				var jwt = createToken(
					issuer: webauthjwt.GetSection("Issuer")?.Value ?? "issuer",
					claims: claims,
					lifetime: lifetime,
					seckey: webauthjwt.GetSection("SecretKey")?.Value ?? "veryveryhardsecurity-keys.needtoolong");
				var retJwtData = new JwtSecurityTokenHandler().WriteToken(jwt);
				var ret = new LoginReply { JwtMessage = retJwtData, Result = 0, Expire = jwt.ValidTo.ToLocalTime(), InfoPayload = GetAddInfo() };
				var loginHist = new SysHistJwt {
					Id_Login = -9, // 初回ログインは-9固定
					JwtUnixTime = jwt.ValidTo.ToUnixTime(),
					ExpDate = jwt.ValidTo.ToLocalTime().ToDtStrDateTimeShort(),
					Ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? ".",
					Jsub = Common.DeserializeObject<SysHistJwtSub>(userRequest.Info) ?? new(),
					Op = "LoginAsync First"
				};
				if (userRequest.Info != null)

					_db.Insert<SysHistJwt>(loginHist);
				return Task.FromResult(ret);
			}

		}
		var loginData = _db.Fetch<SysLogin>($"where LoginId=@0", [userRequest.LoginId]).FirstOrDefault();

		if (loginData == null) {
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
		}
		else { // パスワードと有効期限のチェック [Checks for password and expiration date]
			   // もらったパスワードを復元してみる Decryptのpassが違ってるとException
			   // [Try to restore the received password; if the pass for Decrypt is incorrect, an exception occurs]
			var restorePass = Common.DecryptLoginRequest(userRequest.CryptPassword, userRequest.LoginDate);

			var wrk = Common.EncryptLoginRequest("123", loginData.VdateC);
			var orgPlanePass = (loginData.CryptPassword != null) ? Common.DecryptLoginRequest(loginData.CryptPassword, loginData.VdateC) : "";
			if (orgPlanePass != restorePass)
				return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
			if (DateTime.Now.ToDtStrDateTimeShort().CompareTo(loginData.ExpDate) > 0) // Nowのほうが大きければエラー [If "Now" is greater, an error occurs]
				return Task.FromResult(new LoginReply { JwtMessage = "", Result = -2 });
			loginData.Vdu = Common.GetVdate();
			loginData.LastDate = loginData.VdateU.ToDtStrDateTimeShort();
			_db.Update(loginData, ["Vdu", "LastDate"]);
			claims.Add(new Claim(ClaimTypes.Role,
				(loginData.Id_Role != 0) ? loginData.Id_Role.ToString() : loginData.Id_Shain.ToString()));
			claims.Add(new Claim(ClaimTypes.SerialNumber, loginData.Id.ToString()));
		}
		// this._configuration.GetSection("WebAuthJwt").GetSection("Lifetime").Value
		if (_configuration.GetSection("WebAuthJwt") != null) {
			var lifetime = TimeSpan.FromMinutes(1);
			var webauthjwt = _configuration.GetSection("WebAuthJwt");
			if (int.TryParse(webauthjwt.GetSection("Lifetime")?.Value, out int minutes))
				lifetime = TimeSpan.FromMinutes(minutes);
			var jwt = createToken(
				issuer: webauthjwt.GetSection("Issuer")?.Value ?? "issuer",
				claims: claims,
				lifetime: lifetime,
				seckey: webauthjwt.GetSection("SecretKey")?.Value ?? "veryveryhardsecurity-keys.needtoolong");
			var retJwtData = new JwtSecurityTokenHandler().WriteToken(jwt);
			//var expire = new DateTime(jwt.ValidTo.ToLocalTime().Ticks, DateTimeKind.Local); // ここで設定してもgRPCシリアライザでKindが落ちる
			// [Even if set here, the Kind crashes in the gRPC serializer]
			// UNIX_EPOCH はUTC 1970/01/01 00:00 からの経過秒数
			var ret = new LoginReply { JwtMessage = retJwtData, Result = 0, Expire = jwt.ValidTo.ToLocalTime(), InfoPayload = GetAddInfo() };
			var loginHist = new SysHistJwt {
				Id_Login = loginData.Id,
				JwtUnixTime = jwt.ValidTo.ToUnixTime(),
				ExpDate = jwt.ValidTo.ToLocalTime().ToDtStrDateTimeShort(),
				Ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? ".",
				Jsub = Common.DeserializeObject<SysHistJwtSub>(userRequest.Info) ?? new(),
				Op = "LoginAsync"
			};
			_db.Insert<SysHistJwt>(loginHist);

			return Task.FromResult(ret);
		}
		else
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
	}
	/// <summary>
	/// トークン作成共通ロジック
	/// [Common logic for token creation]
	/// </summary>
	/// <param name="issuer"></param>
	/// <param name="audience"></param>
	/// <param name="claims"></param>
	/// <param name="lifetime"></param>
	/// <param name="seckey"></param>
	/// <returns></returns>
	JwtSecurityToken createToken(string issuer, IEnumerable<Claim> claims,
		TimeSpan lifetime, string seckey) {
		var jwt = new JwtSecurityToken(
			issuer: issuer,
			claims: claims,
			expires: DateTime.UtcNow.Add(lifetime),
			signingCredentials: new SigningCredentials(
				new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(seckey)),
				SecurityAlgorithms.HmacSha256)
			);
		return jwt;
	}
	/// <summary>
	/// リフレッシュトークンの取得(app.settings.jsonのRefreshtime 分)
	/// [Obtaining the refresh token (based on Refreshtime in app.settings.json)]
	/// </summary>
	/// <param name="userRequest"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="SecurityTokenException"></exception>
	[Authorize]
	public Task<LoginReply> LoginRefleshAsync(LoginRefresh userRequest, ProtoBuf.Grpc.CallContext context = default) {
		// トークンからexpires を取得して、新しいトークンを作成する [Retrieve expires from the token and create a new token]
		// トークンを解析 [Parse the token]
		var handler = new JwtSecurityTokenHandler();
		var jsonToken = handler.ReadToken(userRequest.Token) as JwtSecurityToken;
		if (jsonToken == null) {
			throw new SecurityTokenException("Invalid token");
		}
		// 有効期限を取得 [Obtain the expiration date]
		var expires = jsonToken.ValidTo;
		if (_configuration.GetSection("WebAuthJwt") == null)
			throw new SecurityTokenException("Invalid configuration");
		var webauthjwt = _configuration.GetSection("WebAuthJwt");
		var lifetime = TimeSpan.FromMinutes(1);
		if (int.TryParse(webauthjwt.GetSection("Refreshtime")?.Value, out int minutes))
			lifetime = TimeSpan.FromMinutes(minutes);
		var jwt = createToken(
			issuer: jsonToken.Issuer,
			claims: jsonToken.Claims,
			lifetime: lifetime,
			seckey: webauthjwt.GetSection("SecretKey")?.Value ?? "veryveryhardsecurity-keys.needtoolong");
		var newToken = new JwtSecurityTokenHandler().WriteToken(jwt);
		var loginHist = new SysHistJwt {
			JwtUnixTime = jwt.ValidTo.ToUnixTime(),
			ExpDate = jwt.ValidTo.ToLocalTime().ToDtStrDateTimeShort(),
			Ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? ".",
			Jsub = Common.DeserializeObject<SysHistJwtSub>(userRequest.Info) ?? new(),
			Op = "LoginRefleshAsync"
		};
		_db.Insert<SysHistJwt>(loginHist);

		//var expire = new DateTime(jwt.ValidTo.ToLocalTime().Ticks, DateTimeKind.Local); // ここで設定してもgRPCシリアライザでKindが落ちる
		// [Even if set here, the Kind crashes in the gRPC serializer]				
		return Task.FromResult(new LoginReply { JwtMessage = newToken, Result = 0, Expire = jwt.ValidTo.ToLocalTime() });
	}
	/// <summary>
	/// SysLoginレコード作成処理を行いJWTを返す
	/// </summary>
	/// <param name="userRequest"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	//[AllowAnonymous]
	[Authorize]
	public Task<LoginReply> CreateLoginAsync(LoginRequest userRequest, ProtoBuf.Grpc.CallContext context = default) {
		var loginData = _db.Fetch<SysLogin>($"where LoginId=@0", [userRequest.LoginId]).FirstOrDefault();
		if (loginData != null) {
			// すでに同IDが存在する場合はエラー
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
		}
		var claims = new List<Claim> {
			new Claim(ClaimTypes.Name, userRequest.Name),
			};
		var restorePass = Common.DecryptLoginRequest(userRequest.CryptPassword, userRequest.LoginDate);
		var vdate = Common.GetVdate();
		var initLogin = new SysLogin {
			LoginId = userRequest.LoginId,
			CryptPassword = Common.EncryptLoginRequest(restorePass, new DateTime(vdate).ToLocalTime()),
			Vdc = vdate,
			Vdu = vdate,
			ExpDate = Common.FromUtcTicks(vdate).AddYears(1).ToDtStrDateTimeShort(), // 1年有効 [Valid for 1 year]
			LastDate = Common.FromUtcTicks(vdate).ToDtStrDateTimeShort(),
		};
		_db.Insert<SysLogin>(initLogin);
		// this._configuration.GetSection("WebAuthJwt").GetSection("Lifetime").Value
		if (_configuration.GetSection("WebAuthJwt") != null) {
			var lifetime = TimeSpan.FromMinutes(1);
			var webauthjwt = _configuration.GetSection("WebAuthJwt");
			if (int.TryParse(webauthjwt.GetSection("Lifetime")?.Value, out int minutes))
				lifetime = TimeSpan.FromMinutes(minutes);
			var jwt = createToken(
				issuer: webauthjwt.GetSection("Issuer")?.Value ?? "issuer",
				claims: claims,
				lifetime: lifetime,
				seckey: webauthjwt.GetSection("SecretKey")?.Value ?? "veryveryhardsecurity-keys.needtoolong");
			var retJwtData = new JwtSecurityTokenHandler().WriteToken(jwt);
			var ret = new LoginReply { JwtMessage = retJwtData, Result = 0, Expire = jwt.ValidTo.ToLocalTime(), InfoPayload = GetAddInfo() };
			var loginHist = new SysHistJwt {
				Id_Login = initLogin.Id,
				JwtUnixTime = jwt.ValidTo.ToUnixTime(),
				ExpDate = jwt.ValidTo.ToLocalTime().ToDtStrDateTimeShort(),
				Ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? ".",
				Jsub = Common.DeserializeObject<SysHistJwtSub>(userRequest.Info) ?? new(),
				Op = "CreateLoginAsync"
			};
			_db.Insert<SysHistJwt>(loginHist);

			return Task.FromResult(ret);
		}
		else
			return Task.FromResult(new LoginReply { JwtMessage = "", Result = -1 });
	}
	/// <summary>
	/// 追加情報をセットする
	/// </summary>
	/// <returns></returns>
	private string GetAddInfo() {
		return Common.SerializeObject(new AppGlobal().VerInfo);
	}
}
