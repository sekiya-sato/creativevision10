global using MsgBoxResult = System.Windows.MessageBoxResult;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;


namespace CvWpfclient;
/// <summary>
/// グローバル変数
/// </summary>
public static partial class AppGlobal {
	private static ILogger? _logger;
	// Backing field: 内部でのみ null 許容
	private static IConfigurationRoot? _config;
	private static Guid? _clientId;
	private static IServiceProvider? _serviceProvider;
	private static readonly ConcurrentDictionary<Type, object> _grpcServiceCache = new();
	/// <summary>
	/// サーバーのURL
	/// </summary>
	public static string Url => _config?.GetConnectionString("Url")
		?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
	public static string DataDir => ClientLib.GetDataDir()
		?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
	public static Guid ClientId {
		get {
			if (_clientId == null)
				_clientId = Guid.NewGuid();
			return (Guid)_clientId;
		}
	}
	public static string FitPosition => _config?["Application:FitPosition"] ?? "Center";
	public static string WeatherRegion => _config?["Application:WeatherRegion"] ?? "Tokyo";
	public static string JmaWeatherAreaCode => _config?["Application:JmaWeatherAreaCode"] ?? "130000";
	public static int Limit => int.TryParse(_config?["Application:Limit"], out var limit) ? limit : 100; // デフォルトは100件
	public static bool DebugMode => _config?["Application:DebugMode"]?.ToLower() == "true";
	public static ClientApplication Application => new ClientApplication {
		LoginId = _config?["Application:LoginId"] ?? string.Empty,
		LoginPass = _config?["Application:LoginPass"] ?? string.Empty,
		WeatherRegion = WeatherRegion,
		JmaWeatherAreaCode = JmaWeatherAreaCode,
		FitPosition = FitPosition,
		Limit = Limit,
		DebugMode = DebugMode
	};
	/// <summary>
	/// ログイン認証後のJWT
	/// [JWT after login authentication]
	/// </summary>
	public static string? LoginJwt {
		get => _config?["Application:LoginJwt"] ?? string.Empty;
		set => _config?["Application:LoginJwt"] = value;
	}

	public static Models.InfoUser StaticInfoUser = new();
	public static InfoServer StaticInfoServer = new();

	/// <summary>
	/// ログイン中ユーザのロール(SysLogin.Id_Role)。メニューのロール別表示に使用する。
	/// ログイン前および未設定(0)は標準として扱う。
	/// </summary>
	public static EnumLoginRole CurrentRole { get; set; } = EnumLoginRole.Standard;

	/// <summary>
	/// LoginReply.Role の数値をロールへ変換する。未定義値は標準として扱う。
	/// </summary>
	public static EnumLoginRole ToLoginRole(long role) =>
		Enum.IsDefined(typeof(EnumLoginRole), (int)role) ? (EnumLoginRole)(int)role : EnumLoginRole.Standard;


	/// <summary>
	/// Config読込処理：application startup で一度だけ実行すること
	/// </summary>
	public static void Init(IConfigurationRoot config, IServiceProvider serviceProvider, ILogger logger) {
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(serviceProvider);
		_logger = logger;
		_logger.LogInformation("GlobalInitialize()実行");
		_config = config;
		_serviceProvider = serviceProvider;
		_grpcServiceCache.Clear();
		_logger.LogWarning($"---------------------------------\n AppGlobal.Init() 接続先Url={Url},実行フォルダ={Directory.GetCurrentDirectory()}");
		// あれば取得する
		if (string.IsNullOrWhiteSpace(LoginJwt)) {
			SetLoginJwt(_config.GetSection("Application")?["LoginJwt"]);
		}
	}

	public static void SetLoginJwt(string? loginJwt) => LoginJwt = loginJwt;

	public static void ClearLoginJwt() => LoginJwt = string.Empty;
	/// <summary>
	/// メタデータを取得する
	/// [Retrieve metadata]
	/// </summary>
	/// <returns></returns>
	public static CallContext GetDefaultCallContext() => GetDefaultCallContext(CancellationToken.None);
	public static CallContext GetDefaultCallContext(CancellationToken cancellationToken) {
		var callOptions = new CallOptions(headers: CreateDefaultMetadata(), cancellationToken: cancellationToken);
		return new CallContext(
					callOptions: callOptions,
					flags: CallContextFlags.CaptureMetadata);
	}
	public static CallContext GetDefaultCallContext(CancellationToken cancellationToken, TimeSpan timeout) {
		if (timeout <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(timeout), "gRPC timeout は 0 より大きい必要があります。");
		}

		var callOptions = new CallOptions(
			headers: CreateDefaultMetadata(),
			deadline: DateTime.UtcNow.Add(timeout),
			cancellationToken: cancellationToken);
		return new CallContext(
					callOptions: callOptions,
					flags: CallContextFlags.CaptureMetadata);
	}

	private static Metadata CreateDefaultMetadata() {
		// 認証ヘッダーは CallContext 側を正とする。
		// 匿名呼び出しでも LoginJwt が空なら "Authorization: Bearer " を送る実装のままなので、
		// 将来 LoginAsync などで未送信へ変えたくなった場合はここを起点に見直すこと。
		return new Metadata {
			new Metadata.Entry("X-ClientId", ClientId.ToString()),
			new Metadata.Entry("Authorization", $"Bearer {LoginJwt}"),
		};
	}
	/// <summary>
	/// gRPCサービスを取得する
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	public static T GetGrpcService<T>() where T : class {
		var provider = _serviceProvider
			?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
		return (T)_grpcServiceCache.GetOrAdd(typeof(T), _ => {
			var service = provider.GetRequiredService<T>();
			return service ?? throw new InvalidOperationException($"Service '{typeof(T).Name}' could not be resolved.");
		});
	}
	/// <summary>
	/// メモリ上の設定値を更新する。呼び出し後、必要に応じて gRPC サービスの再構築などを行うこと。
	/// </summary>
	/// <param name="url"></param>
	/// <param name="loginId"></param>
	/// <param name="loginPass"></param>
	/// <exception cref="InvalidOperationException"></exception>
	public static void UpdateConfigValues(string? url = null, string? loginId = null, string? loginPass = null, string? weatherRegion = null, string? fitPosition = null, int? limit = null, string? jmaWeatherAreaCode = null, bool? debugMode = null) {
		if (_config == null) {
			throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
		}
		if (url != null) {
			_config["ConnectionStrings:Url"] = url;
			_grpcServiceCache.Clear();
		}
		if (loginId != null) {
			_config["Application:LoginId"] = loginId;
		}
		if (loginPass != null) {
			_config["Application:LoginPass"] = loginPass;
		}
		if (weatherRegion != null) {
			_config["Application:WeatherRegion"] = weatherRegion;
		}
		if (jmaWeatherAreaCode != null) {
			_config["Application:JmaWeatherAreaCode"] = jmaWeatherAreaCode;
		}
		if (fitPosition != null) {
			_config["Application:FitPosition"] = fitPosition;
		}
		if (limit != null) {
			_config["Application:Limit"] = limit.Value.ToString(CultureInfo.InvariantCulture);
		}
		if (debugMode != null) {
			_config["Application:DebugMode"] = debugMode.Value ? "true" : "false";
		}
	}


}
