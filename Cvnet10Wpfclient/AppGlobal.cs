
/*
# file name
AppGlobal.cs

# description
グローバル変数を管理するクラス

*/
global using MsgBoxResult = System.Windows.MessageBoxResult;
using Cvnet10Wpfclient.Helpers;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using ProtoBuf.Grpc;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;


namespace Cvnet10Wpfclient;
/// <summary>
/// グローバル変数
/// </summary>
public static class AppGlobal {
	// Backing field: 内部でのみ null 許容
	private static IConfigurationRoot? _config;
	private static string? _url;
	private static string? _dataDir;
	private static Guid? _clientId;
	private static string? _loginJwt;
	private static IServiceProvider? _serviceProvider;
	private static readonly ConcurrentDictionary<Type, object> _grpcServiceCache = new();
	/// <summary>
	/// サーバーのURL
	/// </summary>
	public static string Url => _url
		?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
	public static string DataDir => _dataDir
		?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
	public static Guid ClientId => _clientId ??= Guid.NewGuid();
	/// <summary>
	/// app.config内容
	/// </summary>
	public static IConfigurationRoot Config => _config
		?? throw new InvalidOperationException("AppGlobal has not been initialized. Call Init() at application startup.");
	/// <summary>
	/// ログイン認証後のJWT
	/// [JWT after login authentication]
	/// </summary>
	public static string? LoginJwt {
		get => _loginJwt;
		set => _loginJwt = value;
	}

	/// <summary>
	/// Config読込処理：application startup で一度だけ実行すること
	/// </summary>
	public static void Init(IConfigurationRoot config, IServiceProvider serviceProvider) {
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(serviceProvider);
		Debug.WriteLine("GlobalInitialize()実行");
		_config = config;
		_serviceProvider = serviceProvider;
		_url = _config.GetConnectionString("Url");
		_dataDir = ClientLib.GetDataDir();
		_grpcServiceCache.Clear();
		LogManager.GetCurrentClassLogger()?.Info($"---------------------------------\n AppGlobal.Init() 接続先Url={_url},実行フォルダ={Directory.GetCurrentDirectory()}");
		// あれば取得する
		if (string.IsNullOrWhiteSpace(LoginJwt)) {
			SetLoginJwt(_config.GetSection("Parameters")?["LoginJwt"]);
		}
	}

	public static void SetLoginJwt(string? loginJwt) {
		_loginJwt = loginJwt;
	}

	public static void ClearLoginJwt() {
		_loginJwt = string.Empty;
	}
	/// <summary>
	/// メタデータを取得する
	/// [Retrieve metadata]
	/// </summary>
	/// <returns></returns>
	public static CallContext GetDefaultCallContext() {
		return GetDefaultCallContext(CancellationToken.None);
	}
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



}
