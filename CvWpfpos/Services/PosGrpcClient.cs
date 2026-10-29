using CodeShare;
using CvAsset;
using CvBase;
using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using System.Net.Http;

namespace CvWpfpos.Services;

public sealed class PosGrpcClient : IDisposable {
	private readonly GrpcChannel channel;
	private readonly ILoginService loginService;
	private readonly ICoreService coreService;
	private readonly PosSettings settings;
	private readonly Guid clientId = Guid.NewGuid();

	public PosGrpcClient(PosSettings settings) {
		this.settings = settings;
		channel = GrpcChannel.ForAddress(settings.ServerUrl, new GrpcChannelOptions { HttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan } });
		loginService = channel.CreateGrpcService<ILoginService>();
		coreService = channel.CreateGrpcService<ICoreService>();
	}

	public async Task<PosProduct?> LookupProductAsync(string barcode, CancellationToken cancellationToken) {
		return await QueryPosAsync<PosBarcodeLookupRequest, PosProduct>(
			CvFlag.Msg070_PosLookupProduct,
			new PosBarcodeLookupRequest { Barcode = barcode },
			cancellationToken,
			allowNotFound: true);
	}

	public async Task<PosCheckoutResponse> CheckoutAsync(PosCheckoutRequest request, CancellationToken cancellationToken) {
		return await QueryPosAsync<PosCheckoutRequest, PosCheckoutResponse>(CvFlag.Msg071_PosCheckout, request, cancellationToken)
			?? throw new InvalidOperationException("POS売上確定の応答がありません。");
	}

	public async Task<PosCancelSaleResponse> CancelSaleAsync(PosCancelSaleRequest request, CancellationToken cancellationToken) {
		return await QueryPosAsync<PosCancelSaleRequest, PosCancelSaleResponse>(CvFlag.Msg072_PosCancelSale, request, cancellationToken)
			?? throw new InvalidOperationException("POS売上取消の応答がありません。");
	}

	public async Task<PosSaveSeisanResponse> SaveSeisanAsync(PosSaveSeisanRequest request, CancellationToken cancellationToken) {
		return await QueryPosAsync<PosSaveSeisanRequest, PosSaveSeisanResponse>(CvFlag.Msg073_PosSaveSeisan, request, cancellationToken)
			?? throw new InvalidOperationException("POS日次精算の応答がありません。");
	}

	public Task<LoginReply> LoginAsync(string loginId, string password, CancellationToken cancellationToken) {
		var now = DateTime.Now;
		var ip = Common.GetIPAddress().FirstOrDefault();
		var request = new LoginRequest {
			LoginId = loginId,
			Name = $"CV POS ユーザ {now:yyyy/MM/dd HH:mm:ss}",
			CryptPassword = Common.EncryptLoginRequest(password, now),
			LoginDate = now,
			Info = Common.SerializeObject(new { IpAddress = ip.IPAddress?.ToString() ?? string.Empty, MacAddress = ip.MacAddress ?? string.Empty, Machine = Environment.MachineName, User = Environment.UserName, OsVer = Environment.OSVersion.Version.ToString() }),
		};
		return loginService.LoginAsync(request, CreateCallContext(cancellationToken, false));
	}

	public Task<LoginReply> RefreshLoginAsync(CancellationToken cancellationToken) =>
		loginService.LoginRefreshAsync(new LoginRefresh { Token = settings.AccessToken, Info = "{}" }, CreateCallContext(cancellationToken));

	/// <summary>ICoreService.Msg101_Op_Query を使ってサーバからデータを取得します。</summary>
	public async Task<List<T>> QueryListAsync<T>(string? where, string? order, string[]? parameters, int? maxCount, CancellationToken cancellationToken) {
		var param = new QueryListParam(typeof(T), where, order, parameters, maxCount);
		var msg = new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var reply = await coreService.QueryMsgAsync(msg, CreateCallContext(cancellationToken));
		if (reply.Code < 0) throw new InvalidOperationException(reply.DataMsg);
		var list = JsonConvert.DeserializeObject<List<T>>(reply.DataMsg);
		return list ?? [];
	}

	private async Task<TResponse?> QueryPosAsync<TRequest, TResponse>(CvFlag flag, TRequest request, CancellationToken cancellationToken, bool allowNotFound = false)
		where TRequest : class
		where TResponse : class {
		var reply = await coreService.QueryMsgAsync(new CvMsg {
			Flag = flag,
			DataType = typeof(TRequest),
			DataMsg = Common.SerializeObject(request),
		}, CreateCallContext(cancellationToken));

		if (reply.Flag != flag) {
			throw new InvalidOperationException($"POS応答フラグが不正です。期待値={flag} 実際={reply.Flag}");
		}
		if (allowNotFound && reply.Code == CvMsgErrorCode.NotFound) return null;
		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option);
		if (reply.DataType != typeof(TResponse)) {
			throw new InvalidOperationException($"POS応答型が不正です。期待値={typeof(TResponse).Name} 実際={reply.DataType?.Name}");
		}

		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as TResponse
			?? throw new InvalidOperationException("POS応答の復元に失敗しました。");
	}

	private CallContext CreateCallContext(CancellationToken cancellationToken, bool includeAccessToken = true) {
		var headers = new Metadata { new("X-ClientId", clientId.ToString()) };
		if (includeAccessToken && !string.IsNullOrWhiteSpace(settings.AccessToken)) headers.Add("Authorization", $"Bearer {settings.AccessToken}");
		return new CallContext(new CallOptions(headers: headers, cancellationToken: cancellationToken));
	}

	public void Dispose() => channel.Dispose();
}
