using Cvnet10Wpfclient.Util;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using System.Diagnostics;
using System.Net.Http;


namespace Cvnet10Wpfclient {
	/// <summary>
	/// グローバル変数
	/// </summary>
	public static class AppCurrent {
		// Backing field: 内部でのみ null 許容
		private static IConfigurationRoot? _config;
		private static string? _url;
		private static string? _dataDir;
		private static Guid? _clientId;
		/// <summary>
		/// サーバーのURL
		/// </summary>
		public static string Url => _url
			?? throw new InvalidOperationException("AppCurrent has not been initialized. Call Init() at application startup.");
		public static string DataDir => _dataDir
			?? throw new InvalidOperationException("AppCurrent has not been initialized. Call Init() at application startup.");
		public static Guid ClientId => _clientId ??= Guid.NewGuid();
		/// <summary>
		/// app.config内容
		/// </summary>
		public static IConfigurationRoot Config => _config
			?? throw new InvalidOperationException("AppCurrent has not been initialized. Call Init() at application startup.");
		/// <summary>
		/// ログイン認証後のJWT
		/// [JWT after login authentication]
		/// </summary>
		public static string? LoginJwt;

		readonly static HttpClient grpcHttpClient = CreateGrpcHttpClient();
		/// <summary>
		/// Config読込処理：application startup で一度だけ実行すること
		/// </summary>
		public static void Init(IConfigurationRoot config) {
			if (_config != null) return;
			Debug.WriteLine("GlobalInitialize()実行");
			_config = config;
			_url = _config.GetConnectionString("Url");
			_dataDir = ClientLib.GetDataDir();
			// あれば取得する
			LoginJwt = _config.GetSection("AppStrings")?["LoginJwt"];
			// ToDo: ダミーJSONをセット。 実際にはログイン処理で取得すること
			LoginJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiQ3ZuZXRXcGZDbGllbnTjg6bjg7zjgrYgMjAyNi8wMi8wNCAxNjoxNzo0MSIsImV4cCI6MTc3ODgyOTQ2NCwiaXNzIjoiSXNzdWVyX0RldmVsb3BtZW50In0.bm3PHtk85gPMFfMfl92VnRwuKGZlPVzt2-qLl3Alcx4";
		}
		/// <summary>
		/// メタデータを取得する
		/// [Retrieve metadata]
		/// </summary>
		/// <returns></returns>
		public static CallContext GetDefaultCallContext() {
			var callOptions = new CallOptions(headers: new Metadata {
					new Metadata.Entry("X-ClientId", ClientId.ToString()),
					new Metadata.Entry("Authorization", $"Bearer {LoginJwt}"),
				});
			var callContext = new CallContext(
							callOptions: callOptions,
							flags: CallContextFlags.CaptureMetadata);
			return callContext;
		}
		static HttpClient CreateGrpcHttpClient() {
			var client = new HttpClient {
				Timeout = TimeSpan.FromSeconds(300)
			};
			client.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
			return client;
		}
		public static T GetgRPCService<T>() where T : class {
			GrpcChannel grpcChannel = GrpcChannel.ForAddress(
				AppCurrent.Url,
				new GrpcChannelOptions { HttpClient = grpcHttpClient });

			var coreService = grpcChannel.CreateGrpcService<T>();
			if (coreService == null) {
				Debug.WriteLine("サービス取得失敗");
				// 未実装
				throw new NotImplementedException();
			}
			return coreService;
		}




	}
}
