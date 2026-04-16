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
using System.IO;


namespace CvWpfclient;
/// <summary>
/// グローバル変数
/// </summary>
public static class AppGlobal {
	private static ILogger? _logger;
	// Backing field: 内部でのみ null 許容
	private static IConfigurationRoot? _config;
	private static Guid? _clientId;
	private static string? _loginJwt;
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
	public static string FitPosition => _config?["Parameters:FitPosition"] ?? "Center";
	public static string WeatherRegion => _config?["Parameters:WeatherRegion"] ?? "Tokyo";
	public static ClientParameters Parameters => new ClientParameters {
		LoginId = _config?["Parameters:LoginId"] ?? string.Empty,
		LoginPass = _config?["Parameters:LoginPass"] ?? string.Empty,
		LoginJwt = _config?["Parameters:LoginJwt"] ?? string.Empty
	};
	/// <summary>
	/// ログイン認証後のJWT
	/// [JWT after login authentication]
	/// </summary>
	public static string? LoginJwt {
		get => _config?["Parameters:LoginJwt"] ?? string.Empty;
		set => _config?["Parameters:LoginJwt"] = value;
	}

	public static Models.InfoUser StaticInfoUser = new();
	public static InfoServer StaticInfoServer = new();


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
			SetLoginJwt(_config.GetSection("Parameters")?["LoginJwt"]);
		}
	}

	public static void SetLoginJwt(string? loginJwt) => _loginJwt = loginJwt;

	public static void ClearLoginJwt() => _loginJwt = string.Empty;
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
	public static void UpdateConfigValues(string? url = null, string? loginId = null, string? loginPass = null) {
		if (_config == null) {
			throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
		}
		if (url != null) {
			_config["ConnectionStrings:Url"] = url;
			_grpcServiceCache.Clear();
		}
		if (loginId != null) {
			_config["Parameters:LoginId"] = loginId;
		}
		if (loginPass != null) {
			_config["Parameters:LoginPass"] = loginPass;
		}
	}


}
